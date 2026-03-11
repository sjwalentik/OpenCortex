using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient(PortalSettings.ApiHttpClientName);
builder.Services.AddHttpClient(PortalSettings.FirebaseHttpClientName);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", (IConfiguration configuration) =>
{
    var settings = PortalSettings.FromConfiguration(configuration);
    return Results.Ok(new
    {
        service = "OpenCortex.Portal",
        apiBaseUrlConfigured = settings.HasApiBaseUrl,
        hostedAuthConfigured = settings.HasHostedAuth,
    });
});

app.MapGet("/portal-config", (IConfiguration configuration) =>
{
    var settings = PortalSettings.FromConfiguration(configuration);
    return Results.Ok(new
    {
        apiBaseUrlConfigured = settings.HasApiBaseUrl,
        hostedAuthConfigured = settings.HasHostedAuth,
        firebaseProjectId = settings.FirebaseProjectId,
        firebaseApiKey = settings.FirebaseApiKey,
        firebaseAuthDomain = settings.FirebaseAuthDomain,
        mcpBaseUrl = settings.McpBaseUrl,
        operatorConsoleUrl = settings.OperatorConsoleUrl,
        authMode = settings.HasHostedAuth
            ? "firebase-email-password-google"
            : "disabled",
        notes = BuildPortalNotes(settings),
    });
});

app.MapPost("/portal-auth/login", async (
    PortalLoginRequest request,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var settings = PortalSettings.FromConfiguration(configuration);
    if (!settings.HasHostedAuth)
    {
        return BuildHostedAuthNotConfiguredResult();
    }

    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "Email and password are required." });
    }

    var client = httpClientFactory.CreateClient(PortalSettings.FirebaseHttpClientName);
    return await SendFirebaseIdentityRequestAsync(
        client,
        settings.BuildIdentityToolkitUri("accounts:signInWithPassword"),
        new
        {
            email = request.Email.Trim(),
            password = request.Password,
            returnSecureToken = true,
        },
        cancellationToken);
});

app.MapPost("/portal-auth/register", async (
    PortalLoginRequest request,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var settings = PortalSettings.FromConfiguration(configuration);
    if (!settings.HasHostedAuth)
    {
        return BuildHostedAuthNotConfiguredResult();
    }

    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "Email and password are required." });
    }

    var client = httpClientFactory.CreateClient(PortalSettings.FirebaseHttpClientName);
    return await SendFirebaseIdentityRequestAsync(
        client,
        settings.BuildIdentityToolkitUri("accounts:signUp"),
        new
        {
            email = request.Email.Trim(),
            password = request.Password,
            returnSecureToken = true,
        },
        cancellationToken);
});

app.MapPost("/portal-auth/refresh", async (
    PortalRefreshRequest request,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var settings = PortalSettings.FromConfiguration(configuration);
    if (!settings.HasHostedAuth)
    {
        return BuildHostedAuthNotConfiguredResult();
    }

    if (string.IsNullOrWhiteSpace(request.RefreshToken))
    {
        return Results.BadRequest(new { message = "Refresh token is required." });
    }

    var client = httpClientFactory.CreateClient(PortalSettings.FirebaseHttpClientName);
    using var content = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["grant_type"] = "refresh_token",
        ["refresh_token"] = request.RefreshToken.Trim(),
    });

    using var response = await client.PostAsync(
        settings.BuildSecureTokenUri(),
        content,
        cancellationToken);

    var payload = await response.Content.ReadFromJsonAsync<FirebaseRefreshResponse>(cancellationToken: cancellationToken);
    if (!response.IsSuccessStatusCode || payload is null)
    {
        return await BuildFirebaseErrorResultAsync(response, cancellationToken);
    }

    return Results.Ok(new
    {
        idToken = payload.IdToken,
        refreshToken = payload.RefreshToken,
        expiresIn = payload.ExpiresIn,
        userId = payload.UserId,
        projectId = payload.ProjectId,
    });
});

app.MapMethods("/portal-api/{**path}", ["GET", "POST", "PUT", "DELETE"], async (
    string path,
    HttpContext httpContext,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!IsAllowedPortalApiPath(path, httpContext.Request.Method))
    {
        return Results.NotFound(new { message = "Portal route not found." });
    }

    var settings = PortalSettings.FromConfiguration(configuration);
    if (!settings.HasApiBaseUrl)
    {
        return Results.Problem(
            title: "Portal API base URL is not configured",
            detail: "Set Portal:ApiBaseUrl for OpenCortex.Portal before using tenant workspace routes.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    using var downstreamRequest = new HttpRequestMessage(
        new HttpMethod(httpContext.Request.Method),
        new Uri(settings.ApiBaseUri!, $"{path}{httpContext.Request.QueryString}"));

    if (!string.IsNullOrWhiteSpace(httpContext.Request.Headers.Authorization))
    {
        downstreamRequest.Headers.TryAddWithoutValidation(
            "Authorization",
            httpContext.Request.Headers.Authorization.ToString());
    }

    if (httpContext.Request.ContentLength is > 0)
    {
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        downstreamRequest.Content = new StringContent(
            body,
            Encoding.UTF8,
            httpContext.Request.ContentType ?? "application/json");
    }

    var client = httpClientFactory.CreateClient(PortalSettings.ApiHttpClientName);
    using var downstreamResponse = await client.SendAsync(
        downstreamRequest,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken);

    var responseBody = await downstreamResponse.Content.ReadAsStringAsync(cancellationToken);
    var contentType = downstreamResponse.Content.Headers.ContentType?.ToString() ?? "application/json";
    return Results.Content(responseBody, contentType, Encoding.UTF8, (int)downstreamResponse.StatusCode);
});

app.Run();

static string[] BuildPortalNotes(PortalSettings settings)
{
    var notes = new List<string>();

    notes.Add(settings.HasHostedAuth
        ? "Portal browser auth is configured for Firebase email/password plus Firebase-native Google popup sign-in."
        : "Configure Portal:Auth:FirebaseProjectId and Portal:Auth:FirebaseApiKey to enable browser auth.");

    if (settings.HasHostedAuth)
    {
        notes.Add("Enable the Google provider in Firebase Authentication to use Google sign-in in the portal.");
    }

    notes.Add(settings.HasApiBaseUrl
        ? "Portal API proxy is configured."
        : "Configure Portal:ApiBaseUrl to point at OpenCortex.Api before using tenant routes.");

    notes.Add(string.IsNullOrWhiteSpace(settings.McpBaseUrl)
        ? "Configure Portal:McpBaseUrl to surface copy-ready MCP connection details in the tools page."
        : "Portal tools page includes copy-ready MCP connection details.");

    return notes.ToArray();
}

static IResult BuildHostedAuthNotConfiguredResult() =>
    Results.Problem(
        title: "Portal hosted auth is not configured",
        detail: "Set Portal:Auth:FirebaseProjectId and Portal:Auth:FirebaseApiKey before using browser sign-in.",
        statusCode: StatusCodes.Status500InternalServerError);

static async Task<IResult> SendFirebaseIdentityRequestAsync(
    HttpClient client,
    Uri endpoint,
    object body,
    CancellationToken cancellationToken)
{
    using var response = await client.PostAsJsonAsync(endpoint, body, cancellationToken);
    var payload = await response.Content.ReadFromJsonAsync<FirebaseSignInResponse>(cancellationToken: cancellationToken);
    if (!response.IsSuccessStatusCode || payload is null)
    {
        return await BuildFirebaseErrorResultAsync(response, cancellationToken);
    }

    return Results.Ok(new
    {
        idToken = payload.IdToken,
        refreshToken = payload.RefreshToken,
        expiresIn = payload.ExpiresIn,
        email = payload.Email,
        displayName = payload.DisplayName,
        localId = payload.LocalId,
        registered = payload.Registered,
    });
}

static async Task<IResult> BuildFirebaseErrorResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
{
    var payload = await response.Content.ReadFromJsonAsync<FirebaseErrorEnvelope>(cancellationToken: cancellationToken);
    var code = payload?.Error?.Message ?? $"HTTP_{(int)response.StatusCode}";
    var message = MapFirebaseErrorCode(code);

    return Results.Json(
        new
        {
            type = "firebase_auth_error",
            title = "Firebase authentication failed",
            detail = message,
            providerCode = code,
        },
        statusCode: (int)response.StatusCode);
}

static string MapFirebaseErrorCode(string code) =>
    code switch
    {
        "EMAIL_NOT_FOUND" => "No Firebase user exists for that email address.",
        "INVALID_PASSWORD" => "The password is incorrect.",
        "INVALID_LOGIN_CREDENTIALS" => "The email or password is incorrect.",
        "EMAIL_EXISTS" => "A Firebase user already exists for that email address.",
        "TOKEN_EXPIRED" => "The browser session has expired. Sign in again.",
        "USER_DISABLED" => "This Firebase user account has been disabled.",
        _ when code.StartsWith("WEAK_PASSWORD", StringComparison.OrdinalIgnoreCase) => "Password must be at least 6 characters long.",
        _ => $"Firebase returned '{code}'.",
    };

static bool IsAllowedPortalApiPath(string path, string method)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return false;
    }

    var normalizedPath = path.Trim('/');
    var normalizedMethod = method.Trim().ToUpperInvariant();

    return normalizedMethod switch
    {
        "GET" =>
            normalizedPath is "tenant/me" or "tenant/brains" or "tenant/billing/plan" or "tenant/tokens"
            || normalizedPath.StartsWith("tenant/brains/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedPath, "tenant/query", StringComparison.OrdinalIgnoreCase),
        "POST" =>
            normalizedPath is "tenant/tokens"
            || string.Equals(normalizedPath, "tenant/query", StringComparison.OrdinalIgnoreCase)
            || IsTenantBrainDocumentCollectionPath(normalizedPath)
            || IsTenantBrainDocumentRestorePath(normalizedPath)
            || IsTenantBrainReindexPath(normalizedPath),
        "PUT" => IsTenantBrainDocumentItemPath(normalizedPath),
        "DELETE" =>
            normalizedPath.StartsWith("tenant/tokens/", StringComparison.OrdinalIgnoreCase)
            || IsTenantBrainDocumentItemPath(normalizedPath),
        _ => false,
    };
}

static bool IsTenantBrainDocumentCollectionPath(string normalizedPath)
{
    var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return segments.Length == 4
        && string.Equals(segments[0], "tenant", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[1], "brains", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[3], "documents", StringComparison.OrdinalIgnoreCase);
}

static bool IsTenantBrainDocumentItemPath(string normalizedPath)
{
    var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return segments.Length == 5
        && string.Equals(segments[0], "tenant", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[1], "brains", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[3], "documents", StringComparison.OrdinalIgnoreCase);
}

static bool IsTenantBrainDocumentRestorePath(string normalizedPath)
{
    var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return segments.Length == 8
        && string.Equals(segments[0], "tenant", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[1], "brains", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[3], "documents", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[5], "versions", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[7], "restore", StringComparison.OrdinalIgnoreCase);
}

static bool IsTenantBrainReindexPath(string normalizedPath)
{
    var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return segments.Length == 4
        && string.Equals(segments[0], "tenant", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[1], "brains", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[3], "reindex", StringComparison.OrdinalIgnoreCase);
}

internal sealed record PortalLoginRequest(string Email, string Password);

internal sealed record PortalRefreshRequest(string RefreshToken);

internal sealed record FirebaseSignInResponse(
    string IdToken,
    string RefreshToken,
    string ExpiresIn,
    string Email,
    string? DisplayName,
    string LocalId,
    bool Registered);

internal sealed record FirebaseRefreshResponse(
    [property: JsonPropertyName("id_token")] string IdToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] string ExpiresIn,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("project_id")] string ProjectId);

internal sealed record FirebaseErrorEnvelope(FirebaseErrorBody? Error);

internal sealed record FirebaseErrorBody(string Message);

internal sealed class PortalSettings
{
    public const string ApiHttpClientName = "portal-api";
    public const string FirebaseHttpClientName = "portal-firebase";

    public Uri? ApiBaseUri { get; init; }
    public string McpBaseUrl { get; init; } = string.Empty;
    public string FirebaseProjectId { get; init; } = string.Empty;
    public string FirebaseApiKey { get; init; } = string.Empty;
    public string OperatorConsoleUrl =>
        ApiBaseUri is null
            ? string.Empty
            : new Uri(ApiBaseUri, "admin/").ToString();
    public string FirebaseAuthDomain =>
        string.IsNullOrWhiteSpace(FirebaseProjectId)
            ? string.Empty
            : $"{FirebaseProjectId}.firebaseapp.com";

    public bool HasApiBaseUrl => ApiBaseUri is not null;
    public bool HasHostedAuth =>
        !string.IsNullOrWhiteSpace(FirebaseProjectId)
        && !string.IsNullOrWhiteSpace(FirebaseApiKey);

    public static PortalSettings FromConfiguration(IConfiguration configuration)
    {
        var apiBaseUrl = configuration["Portal:ApiBaseUrl"];
        var hasApiBaseUri = Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiBaseUri);

        return new PortalSettings
        {
            ApiBaseUri = hasApiBaseUri ? apiBaseUri : null,
            McpBaseUrl = configuration["Portal:McpBaseUrl"] ?? string.Empty,
            FirebaseProjectId = configuration["Portal:Auth:FirebaseProjectId"] ?? string.Empty,
            FirebaseApiKey = configuration["Portal:Auth:FirebaseApiKey"] ?? string.Empty,
        };
    }

    public Uri BuildIdentityToolkitUri(string action) =>
        new($"https://identitytoolkit.googleapis.com/v1/{action}?key={Uri.EscapeDataString(FirebaseApiKey)}");

    public Uri BuildSecureTokenUri() =>
        new($"https://securetoken.googleapis.com/v1/token?key={Uri.EscapeDataString(FirebaseApiKey)}");
}
