using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenCortex.Core.OAuth;

/// <summary>
/// Handles OAuth flows for Anthropic and OpenAI.
/// </summary>
public sealed class ProviderOAuthService : IProviderOAuthService
{
    private const int OAuthStateLifetimeMinutes = 15;
    private readonly ProviderOAuthConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProviderOAuthService> _logger;

    public ProviderOAuthService(
        IOptions<ProviderOAuthConfig> config,
        HttpClient httpClient,
        ILogger<ProviderOAuthService> logger)
    {
        _config = config.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    public bool IsOAuthConfigured(string providerId)
    {
        return providerId.ToLowerInvariant() switch
        {
            "anthropic" => _config.Anthropic.IsConfigured,
            "openai" => _config.OpenAI.IsConfigured,
            "github" => _config.GitHub.IsConfigured,
            _ => false
        };
    }

    public string GetAuthorizationUrl(string providerId, Guid userId, string? state = null)
    {
        var providerConfig = GetProviderConfig(providerId);
        if (providerConfig is null)
        {
            throw new ArgumentException($"OAuth not configured for provider: {providerId}");
        }

        var stateValue = CreateSignedState(providerConfig, userId, state);

        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["client_id"] = providerConfig.ClientId;
        queryParams["redirect_uri"] = providerConfig.RedirectUri;
        queryParams["response_type"] = "code";
        queryParams["scope"] = string.Join(" ", providerConfig.Scopes);
        queryParams["state"] = stateValue;

        return $"{providerConfig.AuthorizationEndpoint}?{queryParams}";
    }

    public bool TryValidateState(string providerId, string state, out Guid userId, out string? returnUrl)
    {
        userId = Guid.Empty;
        returnUrl = null;

        var providerConfig = GetProviderConfig(providerId);
        if (providerConfig is null || string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        var separatorIndex = state.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == state.Length - 1)
        {
            return false;
        }

        var payloadSegment = state[..separatorIndex];
        var signatureSegment = state[(separatorIndex + 1)..];

        byte[] payloadBytes;
        byte[] providedSignature;
        try
        {
            payloadBytes = Base64UrlDecode(payloadSegment);
            providedSignature = Base64UrlDecode(signatureSegment);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(providerConfig.ClientSecret));
        var expectedSignature = hmac.ComputeHash(payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature))
        {
            return false;
        }

        OAuthStatePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OAuthStatePayload>(payloadBytes);
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload is null
            || !Guid.TryParse(payload.UserId, out userId)
            || payload.IssuedAtUnixSeconds <= 0)
        {
            return false;
        }

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAtUnixSeconds);
        var now = DateTimeOffset.UtcNow;
        if (issuedAt > now.AddMinutes(1)
            || issuedAt < now.AddMinutes(-OAuthStateLifetimeMinutes))
        {
            return false;
        }

        returnUrl = NormalizeReturnUrl(payload.ReturnUrl);
        return true;
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(string providerId, string code, CancellationToken cancellationToken = default)
    {
        var providerConfig = GetProviderConfig(providerId);
        if (providerConfig is null)
        {
            return new OAuthTokenResult { Success = false, Error = "provider_not_configured" };
        }

        try
        {
            var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = providerConfig.RedirectUri,
                ["client_id"] = providerConfig.ClientId,
                ["client_secret"] = providerConfig.ClientSecret
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, providerConfig.TokenEndpoint)
            {
                Content = requestContent
            };
            // GitHub requires Accept: application/json to return JSON instead of form-urlencoded
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OAuth token exchange failed for {ProviderId}: {StatusCode} - {Content}",
                    providerId, response.StatusCode, content);

                var errorResponse = JsonSerializer.Deserialize<OAuthErrorResponse>(content);
                return new OAuthTokenResult
                {
                    Success = false,
                    Error = errorResponse?.Error ?? "token_exchange_failed",
                    ErrorDescription = errorResponse?.ErrorDescription ?? content
                };
            }

            var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(content);
            if (tokenResponse is null)
            {
                return new OAuthTokenResult { Success = false, Error = "invalid_response" };
            }

            return new OAuthTokenResult
            {
                Success = true,
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAt = tokenResponse.ExpiresIn.HasValue
                    ? DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn.Value)
                    : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth token exchange error for {ProviderId}", providerId);
            return new OAuthTokenResult
            {
                Success = false,
                Error = "exception",
                ErrorDescription = ex.Message
            };
        }
    }

    public async Task<OAuthTokenResult> RefreshTokenAsync(string providerId, string refreshToken, CancellationToken cancellationToken = default)
    {
        var providerConfig = GetProviderConfig(providerId);
        if (providerConfig is null)
        {
            return new OAuthTokenResult { Success = false, Error = "provider_not_configured" };
        }

        try
        {
            var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = providerConfig.ClientId,
                ["client_secret"] = providerConfig.ClientSecret
            });

            var response = await _httpClient.PostAsync(providerConfig.TokenEndpoint, requestContent, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OAuth token refresh failed for {ProviderId}: {StatusCode}",
                    providerId, response.StatusCode);

                var errorResponse = JsonSerializer.Deserialize<OAuthErrorResponse>(content);
                return new OAuthTokenResult
                {
                    Success = false,
                    Error = errorResponse?.Error ?? "refresh_failed",
                    ErrorDescription = errorResponse?.ErrorDescription
                };
            }

            var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(content);
            if (tokenResponse is null)
            {
                return new OAuthTokenResult { Success = false, Error = "invalid_response" };
            }

            return new OAuthTokenResult
            {
                Success = true,
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken ?? refreshToken, // Some providers don't return new refresh token
                ExpiresAt = tokenResponse.ExpiresIn.HasValue
                    ? DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn.Value)
                    : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth token refresh error for {ProviderId}", providerId);
            return new OAuthTokenResult
            {
                Success = false,
                Error = "exception",
                ErrorDescription = ex.Message
            };
        }
    }

    public async Task RevokeTokenAsync(string providerId, string accessToken, CancellationToken cancellationToken = default)
    {
        var providerConfig = GetProviderConfig(providerId);
        if (providerConfig is null) return;

        try
        {
            // Revocation endpoint varies by provider
            var revokeEndpoint = providerId.ToLowerInvariant() switch
            {
                "anthropic" => "https://api.anthropic.com/oauth/revoke",
                "openai" => "https://api.openai.com/oauth/revoke",
                _ => null
            };

            if (revokeEndpoint is null) return;

            var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = accessToken,
                ["client_id"] = providerConfig.ClientId,
                ["client_secret"] = providerConfig.ClientSecret
            });

            await _httpClient.PostAsync(revokeEndpoint, requestContent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to revoke OAuth token for {ProviderId}", providerId);
        }
    }

    private ProviderOAuthEndpoints? GetProviderConfig(string providerId)
    {
        return providerId.ToLowerInvariant() switch
        {
            "anthropic" when _config.Anthropic.IsConfigured => new ProviderOAuthEndpoints(
                _config.Anthropic.ClientId,
                _config.Anthropic.ClientSecret,
                _config.Anthropic.RedirectUri,
                _config.Anthropic.AuthorizationEndpoint,
                _config.Anthropic.TokenEndpoint,
                _config.Anthropic.Scopes
            ),
            "openai" when _config.OpenAI.IsConfigured => new ProviderOAuthEndpoints(
                _config.OpenAI.ClientId,
                _config.OpenAI.ClientSecret,
                _config.OpenAI.RedirectUri,
                _config.OpenAI.AuthorizationEndpoint,
                _config.OpenAI.TokenEndpoint,
                _config.OpenAI.Scopes
            ),
            "github" when _config.GitHub.IsConfigured => new ProviderOAuthEndpoints(
                _config.GitHub.ClientId,
                _config.GitHub.ClientSecret,
                _config.GitHub.RedirectUri,
                _config.GitHub.AuthorizationEndpoint,
                _config.GitHub.TokenEndpoint,
                _config.GitHub.Scopes
            ),
            _ => null
        };
    }

    private static string CreateSignedState(ProviderOAuthEndpoints providerConfig, Guid userId, string? returnUrl)
    {
        var payload = new OAuthStatePayload(
            userId.ToString(),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            string.IsNullOrWhiteSpace(returnUrl) ? null : returnUrl.Trim());

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(providerConfig.ClientSecret));
        var signature = hmac.ComputeHash(payloadBytes);

        return $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
    }

    private static string? NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return null;
        }

        return Uri.TryCreate(returnUrl, UriKind.RelativeOrAbsolute, out var parsed)
            ? parsed.ToString()
            : null;
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value
            .Replace('-', '+')
            .Replace('_', '/');

        var padded = (normalized.Length % 4) switch
        {
            0 => normalized,
            2 => normalized + "==",
            3 => normalized + "=",
            _ => throw new FormatException("Invalid base64url value.")
        };

        return Convert.FromBase64String(padded);
    }

    private sealed record ProviderOAuthEndpoints(
        string ClientId,
        string ClientSecret,
        string RedirectUri,
        string AuthorizationEndpoint,
        string TokenEndpoint,
        string[] Scopes
    );

    private sealed record OAuthStatePayload(
        string UserId,
        long IssuedAtUnixSeconds,
        string? ReturnUrl);

    private sealed class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }

    private sealed class OAuthErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }
}
