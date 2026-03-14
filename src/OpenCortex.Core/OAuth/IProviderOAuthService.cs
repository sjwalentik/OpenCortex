namespace OpenCortex.Core.OAuth;

/// <summary>
/// Service for handling OAuth flows with LLM providers.
/// </summary>
public interface IProviderOAuthService
{
    /// <summary>
    /// Generate the authorization URL for a provider.
    /// </summary>
    string GetAuthorizationUrl(string providerId, Guid userId, string? state = null);

    /// <summary>
    /// Exchange an authorization code for tokens.
    /// </summary>
    Task<OAuthTokenResult> ExchangeCodeAsync(string providerId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh an expired access token.
    /// </summary>
    Task<OAuthTokenResult> RefreshTokenAsync(string providerId, string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke tokens for a provider.
    /// </summary>
    Task RevokeTokenAsync(string providerId, string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if OAuth is configured for a provider.
    /// </summary>
    bool IsOAuthConfigured(string providerId);
}

/// <summary>
/// Result of an OAuth token exchange or refresh.
/// </summary>
public sealed class OAuthTokenResult
{
    public bool Success { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }
}
