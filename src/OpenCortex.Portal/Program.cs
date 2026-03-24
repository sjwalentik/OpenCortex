using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient(PortalSettings.ApiHttpClientName, client =>
{
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient(PortalSettings.FirebaseHttpClientName);

var app = builder.Build();

// ---------------------------------------------------------------------------
// Security headers middleware
// ---------------------------------------------------------------------------

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["X-XSS-Protection"] = "1; mode=block";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

    // CSP for React SPA - allows inline scripts for Vite, Firebase, and API connections
    var settings = PortalSettings.FromConfiguration(app.Configuration);
    var apiOrigin = settings.ApiBaseUri?.GetLeftPart(UriPartial.Authority) ?? "";
    var firebaseOrigins = "https://*.firebaseapp.com https://*.googleapis.com https://identitytoolkit.googleapis.com https://securetoken.googleapis.com";
    var firebaseScriptOrigins = "https://www.gstatic.com https://apis.google.com";
    var firebaseConnectOrigins = "https://www.gstatic.com https://apis.google.com https://www.googleapis.com";
    var firebaseFrameOrigins = "https://*.firebaseapp.com https://accounts.google.com https://*.google.com";

    headers["Content-Security-Policy"] = string.Join("; ",
        "default-src 'self'",
        $"connect-src 'self' {apiOrigin} {firebaseOrigins} {firebaseConnectOrigins}".Trim(),
        $"script-src 'self' 'unsafe-inline' 'unsafe-eval' {firebaseScriptOrigins}".Trim(),
        "style-src 'self' 'unsafe-inline'",
        "img-src 'self' data: https:",
        "font-src 'self' data:",
        $"frame-src 'self' {firebaseFrameOrigins}".Trim(),
        "frame-ancestors 'none'");

    await next();
});

app.UseStaticFiles();

app.MapGet("/", () => Results.Redirect("/app/index.html"));
app.MapGet("/app", () => Results.Redirect("/app/index.html"));
app.MapGet("/index.html", () => Results.Redirect("/app/index.html"));
app.MapGet("/legacy", () => Results.Redirect("/app/index.html"));

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

app.MapMethods("/portal-api/{**path}", ["GET", "POST", "PUT", "PATCH", "DELETE"], async (
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

    httpContext.Response.StatusCode = (int)downstreamResponse.StatusCode;
    CopyDownstreamResponseHeaders(downstreamResponse, httpContext.Response);

    await using var downstreamStream = await downstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
    await downstreamStream.CopyToAsync(httpContext.Response.Body, cancellationToken);

    return Results.Empty;
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
            normalizedPath is "tenant/me" or "tenant/me/memory-brain" or "tenant/me/workspace-runtime" or "tenant/brains" or "tenant/billing/plan" or "tenant/tokens" or "tenant/conversations"
            || normalizedPath.StartsWith("tenant/brains/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("tenant/conversations/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedPath, "tenant/query", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("api/chat/", StringComparison.OrdinalIgnoreCase)
            || IsProviderConfigPath(normalizedPath),
        "POST" =>
            normalizedPath is "tenant/tokens" or "tenant/conversations"
            || string.Equals(normalizedPath, "tenant/query", StringComparison.OrdinalIgnoreCase)
            || IsTenantBrainDocumentCollectionPath(normalizedPath)
            || IsTenantBrainDocumentRestorePath(normalizedPath)
            || IsTenantBrainReindexPath(normalizedPath)
            || IsChatCompletionPath(normalizedPath)
            || IsProviderConfigTogglePath(normalizedPath)
            || IsProviderConfigHostedActionPath(normalizedPath)
            || IsProviderConfigOAuthActionPath(normalizedPath),
        "PUT" =>
            IsTenantBrainDocumentItemPath(normalizedPath)
            || string.Equals(normalizedPath, "tenant/me/memory-brain", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedPath, "tenant/me/workspace-runtime", StringComparison.OrdinalIgnoreCase)
            || IsProviderConfigItemPath(normalizedPath),
        "PATCH" => IsTenantConversationItemPath(normalizedPath),
        "DELETE" =>
            normalizedPath.StartsWith("tenant/tokens/", StringComparison.OrdinalIgnoreCase)
            || IsTenantBrainDocumentItemPath(normalizedPath)
            || IsTenantConversationItemPath(normalizedPath)
            || IsProviderConfigItemPath(normalizedPath),
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

static bool IsTenantConversationItemPath(string normalizedPath)
{
    var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return segments.Length == 3
        && string.Equals(segments[0], "tenant", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[1], "conversations", StringComparison.OrdinalIgnoreCase);
}

static bool IsChatCompletionPath(string normalizedPath)
{
    return string.Equals(normalizedPath, "api/chat/completions", StringComparison.OrdinalIgnoreCase)
        || string.Equals(normalizedPath, "api/chat/completions/stream", StringComparison.OrdinalIgnoreCase)
        || string.Equals(normalizedPath, "api/chat/completions/agentic", StringComparison.OrdinalIgnoreCase)
        || string.Equals(normalizedPath, "api/chat/completions/agentic/stream", StringComparison.OrdinalIgnoreCase);
}

static bool IsProviderConfigPath(string normalizedPath)
{
    // Matches: api/providers/config, api/providers/config/, api/providers/config/available, api/providers/config/{providerId}
    return string.Equals(normalizedPath, "api/providers/config", StringComparison.OrdinalIgnoreCase)
        || normalizedPath.StartsWith("api/providers/config/", StringComparison.OrdinalIgnoreCase);
}

static bool IsProviderConfigItemPath(string normalizedPath)
{
    // Matches: api/providers/config/{providerId} (exactly 4 segments)
    var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return segments.Length == 4
        && string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[1], "providers", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[2], "config", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(segments[3], "available", StringComparison.OrdinalIgnoreCase);
}

static bool IsProviderConfigTogglePath(string normalizedPath)
{
    // Matches: api/providers/config/{providerId}/toggle
    var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return segments.Length == 5
        && string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[1], "providers", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[2], "config", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[4], "toggle", StringComparison.OrdinalIgnoreCase);
}

static bool IsProviderConfigHostedActionPath(string normalizedPath)
{
    // Matches: api/providers/config/{providerId}/hosted-login/start|complete|cancel
    var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return segments.Length == 6
        && string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[1], "providers", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[2], "config", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[4], "hosted-login", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(segments[5], "start", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segments[5], "complete", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segments[5], "cancel", StringComparison.OrdinalIgnoreCase));
}

static void CopyDownstreamResponseHeaders(HttpResponseMessage downstreamResponse, HttpResponse response)
{
    foreach (var header in downstreamResponse.Headers)
    {
        if (IsHopByHopHeader(header.Key))
        {
            continue;
        }

        response.Headers[header.Key] = header.Value.ToArray();
    }

    foreach (var header in downstreamResponse.Content.Headers)
    {
        if (IsHopByHopHeader(header.Key))
        {
            continue;
        }

        response.Headers[header.Key] = header.Value.ToArray();
    }

    response.Headers.Remove("transfer-encoding");
}

static bool IsHopByHopHeader(string headerName) =>
    headerName.Equals("Connection", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("TE", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Trailer", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);

static bool IsProviderConfigOAuthActionPath(string normalizedPath)
{
    // Matches: api/providers/config/{providerId}/oauth/disconnect or /refresh
    var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return segments.Length == 6
        && string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[1], "providers", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[2], "config", StringComparison.OrdinalIgnoreCase)
        && string.Equals(segments[4], "oauth", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(segments[5], "disconnect", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segments[5], "refresh", StringComparison.OrdinalIgnoreCase));
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

