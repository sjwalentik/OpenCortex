using System.Security.Claims;
using System.Text.Json;
using OpenCortex.Core;
using OpenCortex.Core.OAuth;

namespace OpenCortex.Api;

/// <summary>
/// API endpoints for users to configure their own LLM providers.
/// </summary>
public static class ProviderConfigEndpoints
{
    public static void MapProviderConfigEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api/providers/config")
            .RequireAuthorization()
            .RequireRateLimiting("tenant-api");

        // List user's configured providers
        routes.MapGet("/", async (
            ClaimsPrincipal user,
            IUserProviderConfigRepository repository,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (userId is null) return Results.Unauthorized();

            var configs = await repository.ListByUserAsync(userId.Value, cancellationToken);

            return Results.Ok(new
            {
                count = configs.Count,
                providers = configs.Select(c => new
                {
                    providerId = c.ProviderId,
                    authType = c.AuthType,
                    isEnabled = c.IsEnabled,
                    hasCredentials = !string.IsNullOrEmpty(c.EncryptedApiKey) || !string.IsNullOrEmpty(c.EncryptedAccessToken),
                    settings = c.SettingsJson is not null ? JsonSerializer.Deserialize<UserProviderSettings>(c.SettingsJson) : null,
                    createdAt = c.CreatedAt,
                    updatedAt = c.UpdatedAt
                })
            });
        });

        // Get specific provider config
        routes.MapGet("/{providerId}", async (
            string providerId,
            ClaimsPrincipal user,
            IUserProviderConfigRepository repository,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (userId is null) return Results.Unauthorized();

            var config = await repository.GetAsync(userId.Value, providerId, cancellationToken);
            if (config is null)
            {
                return Results.NotFound(new { message = $"No configuration found for provider '{providerId}'." });
            }

            return Results.Ok(new
            {
                providerId = config.ProviderId,
                authType = config.AuthType,
                isEnabled = config.IsEnabled,
                hasCredentials = !string.IsNullOrEmpty(config.EncryptedApiKey) || !string.IsNullOrEmpty(config.EncryptedAccessToken),
                settings = config.SettingsJson is not null ? JsonSerializer.Deserialize<UserProviderSettings>(config.SettingsJson) : null,
                createdAt = config.CreatedAt,
                updatedAt = config.UpdatedAt
            });
        });

        // Configure a provider (upsert)
        routes.MapPut("/{providerId}", async (
            string providerId,
            ProviderConfigRequest request,
            ClaimsPrincipal user,
            IUserProviderConfigRepository repository,
            ICredentialEncryption encryption,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            var customerId = GetCustomerId(user);
            if (userId is null || customerId is null) return Results.Unauthorized();

            var config = new UserProviderConfig
            {
                CustomerId = customerId.Value,
                UserId = userId.Value,
                ProviderId = providerId.ToLowerInvariant(),
                AuthType = request.AuthType ?? "api_key",
                IsEnabled = request.IsEnabled ?? true
            };

            // Encrypt credentials
            if (!string.IsNullOrEmpty(request.ApiKey))
            {
                config.EncryptedApiKey = encryption.Encrypt(request.ApiKey);
            }

            if (!string.IsNullOrEmpty(request.AccessToken))
            {
                config.EncryptedAccessToken = encryption.Encrypt(request.AccessToken);
            }

            if (!string.IsNullOrEmpty(request.RefreshToken))
            {
                config.EncryptedRefreshToken = encryption.Encrypt(request.RefreshToken);
            }

            config.TokenExpiresAt = request.TokenExpiresAt;

            if (request.Settings is not null)
            {
                config.SettingsJson = JsonSerializer.Serialize(request.Settings);
            }

            var saved = await repository.UpsertAsync(config, cancellationToken);

            return Results.Ok(new
            {
                message = $"Provider '{providerId}' configured successfully.",
                providerId = saved.ProviderId,
                isEnabled = saved.IsEnabled,
                createdAt = saved.CreatedAt,
                updatedAt = saved.UpdatedAt
            });
        });

        // Delete provider config
        routes.MapDelete("/{providerId}", async (
            string providerId,
            ClaimsPrincipal user,
            IUserProviderConfigRepository repository,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (userId is null) return Results.Unauthorized();

            await repository.DeleteAsync(userId.Value, providerId, cancellationToken);

            return Results.Ok(new { message = $"Provider '{providerId}' configuration deleted." });
        });

        // Toggle provider enabled/disabled
        routes.MapPost("/{providerId}/toggle", async (
            string providerId,
            ClaimsPrincipal user,
            IUserProviderConfigRepository repository,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            var customerId = GetCustomerId(user);
            if (userId is null || customerId is null) return Results.Unauthorized();

            var existing = await repository.GetAsync(userId.Value, providerId, cancellationToken);
            if (existing is null)
            {
                return Results.NotFound(new { message = $"No configuration found for provider '{providerId}'." });
            }

            existing.IsEnabled = !existing.IsEnabled;
            await repository.UpsertAsync(existing, cancellationToken);

            return Results.Ok(new
            {
                providerId,
                isEnabled = existing.IsEnabled,
                message = existing.IsEnabled ? "Provider enabled." : "Provider disabled."
            });
        });

        // List available providers (that can be configured)
        routes.MapGet("/available", (IProviderOAuthService oauthService) =>
        {
            var available = new[]
            {
                new AvailableProvider(
                    "anthropic",
                    "Anthropic (Claude)",
                    oauthService.IsOAuthConfigured("anthropic")
                        ? new[] { "oauth", "api_key" }
                        : new[] { "api_key" },
                    "claude-sonnet-4-6-20260313",
                    "https://console.anthropic.com/settings/keys",
                    oauthService.IsOAuthConfigured("anthropic")
                ),
                new AvailableProvider(
                    "openai",
                    "OpenAI (GPT)",
                    oauthService.IsOAuthConfigured("openai")
                        ? new[] { "oauth", "api_key" }
                        : new[] { "api_key" },
                    "gpt-5.4",
                    "https://platform.openai.com/api-keys",
                    oauthService.IsOAuthConfigured("openai")
                ),
                new AvailableProvider(
                    "ollama",
                    "Ollama (Local/Self-hosted)",
                    new[] { "none" },
                    "qwen3.5-35b-a3b-instruct",
                    null,
                    false
                )
            };

            return Results.Ok(new { providers = available });
        });

        // --- OAuth Flow Endpoints ---

        // Start OAuth flow - returns authorization URL
        routes.MapGet("/{providerId}/oauth/authorize", (
            string providerId,
            ClaimsPrincipal user,
            IProviderOAuthService oauthService,
            string? returnUrl = null) =>
        {
            var userId = GetUserId(user);
            if (userId is null) return Results.Unauthorized();

            if (!oauthService.IsOAuthConfigured(providerId))
            {
                return Results.BadRequest(new { message = $"OAuth is not configured for provider '{providerId}'." });
            }

            var authUrl = oauthService.GetAuthorizationUrl(providerId, userId.Value, returnUrl);
            return Results.Ok(new { authorizationUrl = authUrl });
        });

        // OAuth callback - exchange code for tokens
        routes.MapGet("/{providerId}/oauth/callback", async (
            string providerId,
            string code,
            string state,
            ClaimsPrincipal user,
            IProviderOAuthService oauthService,
            IUserProviderConfigRepository repository,
            ICredentialEncryption encryption,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            var customerId = GetCustomerId(user);
            if (userId is null || customerId is null) return Results.Unauthorized();

            // Verify state contains the user ID
            var stateParts = state.Split(':');
            if (stateParts.Length < 1 || !Guid.TryParse(stateParts[0], out var stateUserId) || stateUserId != userId.Value)
            {
                return Results.BadRequest(new { message = "Invalid OAuth state." });
            }

            var result = await oauthService.ExchangeCodeAsync(providerId, code, cancellationToken);
            if (!result.Success)
            {
                return Results.BadRequest(new
                {
                    message = "OAuth token exchange failed.",
                    error = result.Error,
                    errorDescription = result.ErrorDescription
                });
            }

            // Save the OAuth tokens
            var config = new UserProviderConfig
            {
                CustomerId = customerId.Value,
                UserId = userId.Value,
                ProviderId = providerId.ToLowerInvariant(),
                AuthType = "oauth",
                EncryptedAccessToken = encryption.Encrypt(result.AccessToken!),
                EncryptedRefreshToken = !string.IsNullOrEmpty(result.RefreshToken)
                    ? encryption.Encrypt(result.RefreshToken)
                    : null,
                TokenExpiresAt = result.ExpiresAt,
                IsEnabled = true
            };

            await repository.UpsertAsync(config, cancellationToken);

            // Return URL from state if provided
            var returnUrl = stateParts.Length > 1 ? stateParts[1] : null;

            return Results.Ok(new
            {
                message = $"Successfully connected to {providerId}.",
                providerId,
                returnUrl
            });
        });

        // Disconnect OAuth (revoke and delete)
        routes.MapPost("/{providerId}/oauth/disconnect", async (
            string providerId,
            ClaimsPrincipal user,
            IProviderOAuthService oauthService,
            IUserProviderConfigRepository repository,
            ICredentialEncryption encryption,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (userId is null) return Results.Unauthorized();

            var config = await repository.GetAsync(userId.Value, providerId, cancellationToken);
            if (config is null || config.AuthType != "oauth")
            {
                return Results.NotFound(new { message = $"No OAuth connection found for provider '{providerId}'." });
            }

            // Revoke token if possible
            if (!string.IsNullOrEmpty(config.EncryptedAccessToken))
            {
                var accessToken = encryption.Decrypt(config.EncryptedAccessToken);
                await oauthService.RevokeTokenAsync(providerId, accessToken, cancellationToken);
            }

            // Delete the config
            await repository.DeleteAsync(userId.Value, providerId, cancellationToken);

            return Results.Ok(new { message = $"Disconnected from {providerId}." });
        });

        // Refresh OAuth token manually
        routes.MapPost("/{providerId}/oauth/refresh", async (
            string providerId,
            ClaimsPrincipal user,
            IProviderOAuthService oauthService,
            IUserProviderConfigRepository repository,
            ICredentialEncryption encryption,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            var customerId = GetCustomerId(user);
            if (userId is null || customerId is null) return Results.Unauthorized();

            var config = await repository.GetAsync(userId.Value, providerId, cancellationToken);
            if (config is null || config.AuthType != "oauth" || string.IsNullOrEmpty(config.EncryptedRefreshToken))
            {
                return Results.BadRequest(new { message = "No refresh token available." });
            }

            var refreshToken = encryption.Decrypt(config.EncryptedRefreshToken);
            var result = await oauthService.RefreshTokenAsync(providerId, refreshToken, cancellationToken);

            if (!result.Success)
            {
                return Results.BadRequest(new
                {
                    message = "Token refresh failed.",
                    error = result.Error
                });
            }

            // Update stored tokens
            config.EncryptedAccessToken = encryption.Encrypt(result.AccessToken!);
            if (!string.IsNullOrEmpty(result.RefreshToken))
            {
                config.EncryptedRefreshToken = encryption.Encrypt(result.RefreshToken);
            }
            config.TokenExpiresAt = result.ExpiresAt;

            await repository.UpsertAsync(config, cancellationToken);

            return Results.Ok(new
            {
                message = "Token refreshed successfully.",
                expiresAt = result.ExpiresAt
            });
        });
    }

    private static Guid? GetUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (sub is not null && Guid.TryParse(sub, out var userId))
        {
            return userId;
        }
        // For Firebase, the sub is a string ID - hash it to a GUID
        if (sub is not null)
        {
            return GuidFromString(sub);
        }
        return null;
    }

    private static Guid? GetCustomerId(ClaimsPrincipal user)
    {
        // Customer ID could come from a custom claim or default to user ID for individual users
        var customerId = user.FindFirstValue("customer_id");
        if (customerId is not null && Guid.TryParse(customerId, out var cid))
        {
            return cid;
        }
        return GetUserId(user);
    }

    private static Guid GuidFromString(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }
}

// Request DTOs

internal sealed record ProviderConfigRequest(
    string? AuthType = "api_key",
    string? ApiKey = null,
    string? AccessToken = null,
    string? RefreshToken = null,
    DateTime? TokenExpiresAt = null,
    UserProviderSettings? Settings = null,
    bool? IsEnabled = true
);

internal sealed record AvailableProvider(
    string ProviderId,
    string Name,
    string[] AuthTypes,
    string DefaultModel,
    string? ConfigUrl,
    bool OAuthConfigured
);
