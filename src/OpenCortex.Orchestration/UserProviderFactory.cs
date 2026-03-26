using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCortex.Core;
using OpenCortex.Core.OAuth;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Security;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Orchestration;

/// <summary>
/// Factory that creates provider instances with user-specific credentials.
/// Supports both API key and OAuth authentication.
/// </summary>
public sealed class UserProviderFactory : IUserProviderFactory
{
    private readonly IUserProviderConfigRepository _configRepository;
    private readonly ICredentialEncryption _encryption;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Tools.IWorkspaceManager _workspaceManager;
    private readonly IProviderOAuthService _oauthService;
    private readonly IApiTokenStore _apiTokenStore;
    private readonly WorkspaceTenantIds _tenantIds;
    private readonly string _workspaceMcpServerUrl;
    private readonly ILogger<UserProviderFactory> _logger;

    public UserProviderFactory(
        IUserProviderConfigRepository configRepository,
        ICredentialEncryption encryption,
        IHttpClientFactory httpClientFactory,
        Tools.IWorkspaceManager workspaceManager,
        IProviderOAuthService oauthService,
        IApiTokenStore apiTokenStore,
        WorkspaceTenantIds tenantIds,
        IConfiguration configuration,
        ILogger<UserProviderFactory> logger)
    {
        _configRepository = configRepository;
        _encryption = encryption;
        _httpClientFactory = httpClientFactory;
        _workspaceManager = workspaceManager;
        _oauthService = oauthService;
        _apiTokenStore = apiTokenStore;
        _tenantIds = tenantIds;
        _logger = logger;

        var mcpBase = configuration["MCP_INTERNAL_URL"];
        _workspaceMcpServerUrl = string.IsNullOrWhiteSpace(mcpBase)
            ? string.Empty
            : mcpBase.TrimEnd('/') + "/mcp";
    }

    public async Task<IModelProvider?> GetProviderForUserAsync(Guid customerId, Guid userId, string providerId, CancellationToken cancellationToken = default)
    {
        var normalizedProviderId = NormalizeProviderId(providerId);
        var config = await _configRepository.GetAsync(customerId, userId, normalizedProviderId, cancellationToken);
        if (config is null || !config.IsEnabled)
        {
            _logger.LogDebug("No enabled config found for customer {CustomerId} user {UserId} provider {ProviderId}", customerId, userId, normalizedProviderId);
            return null;
        }

        // Check if OAuth token needs refresh
        if (config.AuthType == "oauth" && await NeedsTokenRefreshAsync(config, cancellationToken))
        {
            config = await RefreshOAuthTokenAsync(config, cancellationToken);
            if (config is null)
            {
                return null;
            }
        }

        // Refresh session_json OAuth credentials if they are near expiry
        if (string.Equals(normalizedProviderId, "claude-cli", StringComparison.OrdinalIgnoreCase)
            && string.Equals(config.AuthType, "session_json", StringComparison.OrdinalIgnoreCase))
        {
            await RefreshClaudeCliSessionJsonIfNeededAsync(config, cancellationToken);
        }

        // Ensure a valid workspace MCP token exists for claude-cli providers
        if (string.Equals(normalizedProviderId, "claude-cli", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_workspaceMcpServerUrl))
        {
            await EnsureClaudeMcpTokenAsync(customerId, userId, config, cancellationToken);
        }

        var githubToken = await GetGitHubCredentialAsync(customerId, userId, cancellationToken);
        return CreateProvider(config, githubToken);
    }

    public async Task<IReadOnlyList<IModelProvider>> GetProvidersForUserAsync(Guid customerId, Guid userId, CancellationToken cancellationToken = default)
    {
        var configs = await _configRepository.ListByUserAsync(customerId, userId, cancellationToken);
        var providers = new List<IModelProvider>();
        var githubToken = TryDecryptCredential(configs.FirstOrDefault(c =>
            string.Equals(c.ProviderId, "github", StringComparison.OrdinalIgnoreCase)
            && c.IsEnabled));

        foreach (var config in configs.Where(c => c.IsEnabled))
        {
            var currentConfig = config;

            // Check if OAuth token needs refresh
            if (currentConfig.AuthType == "oauth" && await NeedsTokenRefreshAsync(currentConfig, cancellationToken))
            {
                currentConfig = await RefreshOAuthTokenAsync(currentConfig, cancellationToken);
                if (currentConfig is null)
                {
                    continue;
                }
            }

            // Refresh session_json OAuth credentials if they are near expiry
            if (string.Equals(currentConfig.ProviderId, "claude-cli", StringComparison.OrdinalIgnoreCase)
                && string.Equals(currentConfig.AuthType, "session_json", StringComparison.OrdinalIgnoreCase))
            {
                await RefreshClaudeCliSessionJsonIfNeededAsync(currentConfig, cancellationToken);
            }

            // Ensure a valid workspace MCP token exists for claude-cli providers
            if (string.Equals(currentConfig.ProviderId, "claude-cli", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(_workspaceMcpServerUrl))
            {
                await EnsureClaudeMcpTokenAsync(customerId, userId, currentConfig, cancellationToken);
            }

            var provider = CreateProvider(currentConfig, githubToken);
            if (provider is not null)
            {
                providers.Add(provider);
            }
        }

        return providers;
    }

    public async Task<bool> HasConfiguredProvidersAsync(Guid customerId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _configRepository.HasAnyConfiguredAsync(customerId, userId, cancellationToken);
    }

    private Task<bool> NeedsTokenRefreshAsync(UserProviderConfig config, CancellationToken cancellationToken)
    {
        // Refresh if token expires within 5 minutes
        if (config.TokenExpiresAt.HasValue && config.TokenExpiresAt.Value < DateTime.UtcNow.AddMinutes(5))
        {
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    private async Task<UserProviderConfig?> RefreshOAuthTokenAsync(UserProviderConfig config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(config.EncryptedRefreshToken))
        {
            _logger.LogWarning("OAuth token expired and no refresh token available for {ProviderId}", config.ProviderId);
            return null;
        }

        var refreshToken = _encryption.Decrypt(config.EncryptedRefreshToken);
        var result = await _oauthService.RefreshTokenAsync(config.ProviderId, refreshToken, cancellationToken);

        if (!result.Success)
        {
            var safeError = Execution.ErrorRedaction.Sanitize(
                "OAuth token refresh failed.",
                result.Error,
                null);
            _logger.LogWarning("Failed to refresh OAuth token for {ProviderId}: {Error}", config.ProviderId, safeError);
            return null;
        }

        // Update stored tokens
        config.EncryptedAccessToken = _encryption.Encrypt(result.AccessToken!);
        if (!string.IsNullOrEmpty(result.RefreshToken))
        {
            config.EncryptedRefreshToken = _encryption.Encrypt(result.RefreshToken);
        }
        config.TokenExpiresAt = result.ExpiresAt;

        await _configRepository.UpsertAsync(config, cancellationToken);

        _logger.LogDebug("Refreshed OAuth token for {ProviderId}, expires at {ExpiresAt}", config.ProviderId, result.ExpiresAt);
        return config;
    }

    private async Task<string?> GetGitHubCredentialAsync(Guid customerId, Guid userId, CancellationToken cancellationToken)
    {
        var githubConfig = await _configRepository.GetAsync(customerId, userId, "github", cancellationToken);
        return githubConfig is { IsEnabled: true }
            ? TryDecryptCredential(githubConfig)
            : null;
    }

    private IModelProvider? CreateProvider(UserProviderConfig config, string? githubToken)
    {
        try
        {
            var settings = config.SettingsJson is not null
                ? JsonSerializer.Deserialize<UserProviderSettings>(config.SettingsJson)
                : null;

            return config.ProviderId.ToLowerInvariant() switch
            {
                "anthropic" => CreateAnthropicProvider(config, settings),
                "openai" => CreateOpenAIProvider(config, settings),
                "openai-codex" => CreateCodexProvider(config, settings, githubToken),
                "claude-cli" => CreateClaudeCliProvider(config, settings, githubToken),
                "ollama" or "ollama-remote" => CreateOllamaProvider(config, settings),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create provider {ProviderId} for user {UserId}", config.ProviderId, config.UserId);
            return null;
        }
    }

    private IModelProvider? CreateAnthropicProvider(UserProviderConfig config, UserProviderSettings? settings)
    {
        // Get credential based on auth type
        string? credential = config.AuthType switch
        {
            "oauth" when !string.IsNullOrEmpty(config.EncryptedAccessToken) =>
                _encryption.Decrypt(config.EncryptedAccessToken),
            "api_key" when !string.IsNullOrEmpty(config.EncryptedApiKey) =>
                _encryption.Decrypt(config.EncryptedApiKey),
            _ => null
        };

        if (string.IsNullOrEmpty(credential))
        {
            _logger.LogWarning("No valid credential found for Anthropic provider");
            return null;
        }

        var httpClient = _httpClientFactory.CreateClient("Anthropic");

        var options = Providers.Anthropic.AnthropicOptions.CreateDefault(credential) with
        {
            DefaultModel = settings?.DefaultModel ?? "claude-sonnet-4-6-20260313"
        };

        return new Providers.Anthropic.AnthropicProvider(
            httpClient,
            Options.Create(options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Providers.Anthropic.AnthropicProvider>.Instance);
    }

    private IModelProvider? CreateOpenAIProvider(UserProviderConfig config, UserProviderSettings? settings)
    {
        // Get credential based on auth type
        string? credential = config.AuthType switch
        {
            "oauth" when !string.IsNullOrEmpty(config.EncryptedAccessToken) =>
                _encryption.Decrypt(config.EncryptedAccessToken),
            "api_key" when !string.IsNullOrEmpty(config.EncryptedApiKey) =>
                _encryption.Decrypt(config.EncryptedApiKey),
            _ => null
        };

        if (string.IsNullOrEmpty(credential))
        {
            _logger.LogWarning("No valid credential found for OpenAI provider");
            return null;
        }

        var httpClient = _httpClientFactory.CreateClient("OpenAI");

        var options = Providers.OpenAI.OpenAIOptions.CreateDefault(credential) with
        {
            DefaultModel = settings?.DefaultModel ?? "gpt-5.4"
        };

        return new Providers.OpenAI.OpenAIProvider(
            httpClient,
            Options.Create(options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Providers.OpenAI.OpenAIProvider>.Instance);
    }

    private IModelProvider? CreateOllamaProvider(UserProviderConfig config, UserProviderSettings? settings)
    {
        var endpoint = settings?.BaseUrl;
        if (string.IsNullOrEmpty(endpoint))
        {
            _logger.LogWarning("Ollama provider requires a BaseUrl in settings");
            return null;
        }

        var httpClient = _httpClientFactory.CreateClient("Ollama");

        var options = Providers.Ollama.OllamaOptions.CreateRemote(endpoint) with
        {
            ProviderId = "ollama",
            DefaultModel = settings?.DefaultModel ?? "qwen3.5-35b-a3b-instruct"
        };

        return new Providers.Ollama.OllamaProvider(
            httpClient,
            Options.Create(options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Providers.Ollama.OllamaProvider>.Instance);
    }

    private IModelProvider? CreateClaudeCliProvider(UserProviderConfig config, UserProviderSettings? settings, string? githubToken)
    {
        string? apiKey = null;
        string? credentialsJson = null;

        if (string.Equals(config.AuthType, "session_json", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(config.EncryptedAccessToken))
            {
                _logger.LogWarning("No session credential found for Claude CLI provider");
                return null;
            }

            credentialsJson = _encryption.Decrypt(config.EncryptedAccessToken);
            if (string.IsNullOrWhiteSpace(credentialsJson))
            {
                _logger.LogWarning("Claude CLI provider session payload was empty after decryption");
                return null;
            }
        }
        else
        {
            if (string.IsNullOrEmpty(config.EncryptedApiKey))
            {
                _logger.LogWarning("No API key found for Claude CLI provider");
                return null;
            }

            apiKey = _encryption.Decrypt(config.EncryptedApiKey);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("Claude CLI provider API key was empty after decryption");
                return null;
            }
        }

        return new ClaudeCliModelProvider(
            config.UserId,
            settings?.DefaultModel ?? "claude-sonnet-4-6",
            apiKey,
            credentialsJson,
            githubToken,
            mcpToken: settings?.McpToken,
            mcpServerUrl: _workspaceMcpServerUrl,
            _workspaceManager,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ClaudeCliModelProvider>.Instance);
    }

    private IModelProvider? CreateCodexProvider(UserProviderConfig config, UserProviderSettings? settings, string? githubToken)
    {
        if (string.IsNullOrEmpty(config.EncryptedAccessToken))
        {
            _logger.LogWarning("No session credential found for OpenAI Codex provider");
            return null;
        }

        var sessionJson = _encryption.Decrypt(config.EncryptedAccessToken);
        if (string.IsNullOrWhiteSpace(sessionJson))
        {
            _logger.LogWarning("OpenAI Codex provider session payload was empty after decryption");
            return null;
        }

        return new CodexCliModelProvider(
            config.UserId,
            settings?.DefaultModel ?? "gpt-5.4",
            sessionJson,
            githubToken,
            _workspaceManager,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CodexCliModelProvider>.Instance);
    }

    private async Task EnsureClaudeMcpTokenAsync(
        Guid customerId,
        Guid userId,
        UserProviderConfig config,
        CancellationToken cancellationToken)
    {
        // Requires the internal tenant user/customer IDs (users.user_id / customers.customer_id)
        // which satisfy the api_tokens FK constraint. These are set by the API layer via WorkspaceTenantIds.
        var tenantUserId = _tenantIds.UserId;
        var tenantCustomerId = _tenantIds.CustomerId;
        if (string.IsNullOrWhiteSpace(tenantUserId) || string.IsNullOrWhiteSpace(tenantCustomerId))
        {
            _logger.LogDebug("Skipping MCP token mint — tenant IDs not available in this request context");
            return;
        }

        var settings = config.SettingsJson is not null
            ? JsonSerializer.Deserialize<UserProviderSettings>(config.SettingsJson) ?? new UserProviderSettings()
            : new UserProviderSettings();

        // Reuse existing token if it won't expire within 24 hours
        if (!string.IsNullOrWhiteSpace(settings.McpToken)
            && settings.McpTokenExpiry.HasValue
            && settings.McpTokenExpiry.Value > DateTimeOffset.UtcNow.AddHours(24))
        {
            return;
        }

        try
        {
            var generated = PersonalApiToken.Generate();
            await _apiTokenStore.CreateTokenAsync(
                new ApiTokenCreateRequest(
                    UserId: tenantUserId,
                    CustomerId: tenantCustomerId,
                    Name: "workspace-mcp-agent",
                    TokenHash: generated.TokenHash,
                    TokenPrefix: generated.TokenPrefix,
                    Scopes: ["mcp:read", "mcp:write"],
                    ExpiresAt: DateTimeOffset.UtcNow.AddDays(30)),
                cancellationToken);

            settings.McpToken = generated.RawToken;
            settings.McpTokenExpiry = DateTimeOffset.UtcNow.AddDays(30);
            config.SettingsJson = JsonSerializer.Serialize(settings);
            await _configRepository.UpsertAsync(config, cancellationToken);

            _logger.LogDebug("Minted workspace MCP token for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mint workspace MCP token for user {UserId}", userId);
        }
    }

    private string? TryDecryptCredential(UserProviderConfig? config)
    {
        if (config is null)
        {
            return null;
        }

        try
        {
            if (!string.IsNullOrEmpty(config.EncryptedAccessToken))
            {
                return _encryption.Decrypt(config.EncryptedAccessToken);
            }

            if (!string.IsNullOrEmpty(config.EncryptedApiKey))
            {
                return _encryption.Decrypt(config.EncryptedApiKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt credential for provider {ProviderId}", config.ProviderId);
        }

        return null;
    }

    private static string NormalizeProviderId(string providerId) =>
        string.Equals(providerId, "ollama-remote", StringComparison.OrdinalIgnoreCase)
            ? "ollama"
            : providerId;

    // Claude Code's public PKCE OAuth client identifier (no secret required for refresh).
    private const string ClaudeCodeOAuthClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private const string AnthropicTokenEndpoint = "https://platform.claude.com/v1/oauth/token";

    private async Task RefreshClaudeCliSessionJsonIfNeededAsync(UserProviderConfig config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(config.EncryptedAccessToken))
            return;

        string credentialsJson;
        try
        {
            credentialsJson = _encryption.Decrypt(config.EncryptedAccessToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt claude-cli session_json for expiry check");
            return;
        }

        ClaudeCredentialsJson? creds;
        try
        {
            creds = JsonSerializer.Deserialize<ClaudeCredentialsJson>(credentialsJson);
        }
        catch (JsonException)
        {
            return; // Not OAuth JSON format — nothing to refresh
        }

        var oauth = creds?.ClaudeAiOauth;
        if (oauth is null)
            return;

        // Only refresh if within 5 minutes of expiry (or already expired)
        if (oauth.ExpiresAt.HasValue && oauth.ExpiresAt.Value > DateTimeOffset.UtcNow.AddMinutes(5))
            return;

        if (string.IsNullOrWhiteSpace(oauth.RefreshToken))
        {
            _logger.LogWarning("Claude CLI session OAuth token is expired but no refresh token is available for user {UserId}", config.UserId);
            return;
        }

        _logger.LogInformation("Claude CLI session OAuth token near/past expiry ({ExpiresAt}), refreshing for user {UserId}", oauth.ExpiresAt, config.UserId);

        try
        {
            var httpClient = _httpClientFactory.CreateClient("Anthropic");
            var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = oauth.RefreshToken,
                ["client_id"] = ClaudeCodeOAuthClientId
            });

            var response = await httpClient.PostAsync(AnthropicTokenEndpoint, formContent, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Claude CLI credential refresh failed ({StatusCode}) body={Body} for user {UserId}", response.StatusCode, body, config.UserId);
                return;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("access_token", out var accessTokenEl) || string.IsNullOrEmpty(accessTokenEl.GetString()))
            {
                _logger.LogWarning("Claude CLI credential refresh returned no access_token. Body={Body} for user {UserId}", body, config.UserId);
                return;
            }

            oauth.AccessToken = accessTokenEl.GetString();
            if (root.TryGetProperty("refresh_token", out var refreshTokenEl) && !string.IsNullOrEmpty(refreshTokenEl.GetString()))
                oauth.RefreshToken = refreshTokenEl.GetString();
            if (root.TryGetProperty("expires_in", out var expiresInEl) && expiresInEl.TryGetInt32(out var expiresIn))
                oauth.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            config.EncryptedAccessToken = _encryption.Encrypt(JsonSerializer.Serialize(creds));
            await _configRepository.UpsertAsync(config, cancellationToken);

            _logger.LogInformation("Refreshed Claude CLI session OAuth token for user {UserId}, new expiry {ExpiresAt}", config.UserId, oauth.ExpiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception while refreshing Claude CLI session OAuth credentials for user {UserId}", config.UserId);
        }
    }

    private sealed class ClaudeCredentialsJson
    {
        [JsonPropertyName("claudeAiOauth")]
        public ClaudeAiOAuthToken? ClaudeAiOauth { get; set; }
    }

    private sealed class ClaudeAiOAuthToken
    {
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refreshToken")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTimeOffset? ExpiresAt { get; set; }
    }
}
