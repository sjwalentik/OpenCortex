namespace OpenCortex.Core.OAuth;

/// <summary>
/// OAuth configuration for LLM providers.
/// </summary>
public sealed class ProviderOAuthConfig
{
    public const string SectionName = "OpenCortex:OAuth";

    public AnthropicOAuthConfig Anthropic { get; set; } = new();
    public OpenAIOAuthConfig OpenAI { get; set; } = new();
    public GitHubOAuthConfig GitHub { get; set; } = new();
}

public sealed class AnthropicOAuthConfig
{
    /// <summary>
    /// OAuth client ID from Anthropic Console.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth client secret (store in user secrets).
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Redirect URI registered with Anthropic.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Anthropic OAuth authorization endpoint.
    /// </summary>
    public string AuthorizationEndpoint { get; set; } = "https://console.anthropic.com/oauth/authorize";

    /// <summary>
    /// Anthropic OAuth token endpoint.
    /// </summary>
    public string TokenEndpoint { get; set; } = "https://api.anthropic.com/oauth/token";

    /// <summary>
    /// Required scopes for API access.
    /// </summary>
    public string[] Scopes { get; set; } = ["api:read", "api:write"];

    public bool IsConfigured => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
}

public sealed class OpenAIOAuthConfig
{
    /// <summary>
    /// OAuth client ID from OpenAI.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth client secret (store in user secrets).
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Redirect URI registered with OpenAI.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// OpenAI OAuth authorization endpoint.
    /// </summary>
    public string AuthorizationEndpoint { get; set; } = "https://platform.openai.com/oauth/authorize";

    /// <summary>
    /// OpenAI OAuth token endpoint.
    /// </summary>
    public string TokenEndpoint { get; set; } = "https://api.openai.com/oauth/token";

    /// <summary>
    /// Required scopes for API access.
    /// </summary>
    public string[] Scopes { get; set; } = ["model.read", "model.request"];

    public bool IsConfigured => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
}

public sealed class GitHubOAuthConfig
{
    /// <summary>
    /// OAuth client ID from GitHub OAuth App.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth client secret (store in user secrets).
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Redirect URI registered with GitHub.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// GitHub OAuth authorization endpoint.
    /// </summary>
    public string AuthorizationEndpoint { get; set; } = "https://github.com/login/oauth/authorize";

    /// <summary>
    /// GitHub OAuth token endpoint.
    /// </summary>
    public string TokenEndpoint { get; set; } = "https://github.com/login/oauth/access_token";

    /// <summary>
    /// Required scopes for repo access.
    /// </summary>
    public string[] Scopes { get; set; } = ["repo", "read:user"];

    public bool IsConfigured => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
}
