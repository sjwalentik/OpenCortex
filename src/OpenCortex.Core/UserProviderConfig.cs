namespace OpenCortex.Core;

/// <summary>
/// User-specific provider configuration. Users configure their own provider access.
/// </summary>
public sealed class UserProviderConfig
{
    public Guid ConfigId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid UserId { get; set; }
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Authentication type: "api_key", "oauth"
    /// </summary>
    public string AuthType { get; set; } = "api_key";

    /// <summary>
    /// Encrypted API key (for api_key auth type).
    /// </summary>
    public string? EncryptedApiKey { get; set; }

    /// <summary>
    /// OAuth access token (for oauth auth type).
    /// </summary>
    public string? EncryptedAccessToken { get; set; }

    /// <summary>
    /// OAuth refresh token (for oauth auth type).
    /// </summary>
    public string? EncryptedRefreshToken { get; set; }

    /// <summary>
    /// Token expiration time (for oauth auth type).
    /// </summary>
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// Provider-specific settings as JSON (e.g., preferred model, base URL for Ollama).
    /// </summary>
    public string? SettingsJson { get; set; }

    /// <summary>
    /// Whether this provider config is active.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Provider settings stored per-user.
/// </summary>
public sealed class UserProviderSettings
{
    public string? DefaultModel { get; set; }
    public string? BaseUrl { get; set; }
    public int? MaxTokens { get; set; }
    public double? Temperature { get; set; }
}
