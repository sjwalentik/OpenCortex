using Microsoft.Extensions.Logging;

namespace OpenCortex.Core.Credentials;

/// <summary>
/// Default implementation of user credential service.
/// </summary>
public sealed class UserCredentialService : IUserCredentialService
{
    private readonly IUserProviderConfigRepository _configRepository;
    private readonly ICredentialEncryption _encryption;
    private readonly ILogger<UserCredentialService> _logger;

    public UserCredentialService(
        IUserProviderConfigRepository configRepository,
        ICredentialEncryption encryption,
        ILogger<UserCredentialService> logger)
    {
        _configRepository = configRepository;
        _encryption = encryption;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetDecryptedCredentialsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var configs = await _configRepository.ListByUserAsync(userId, cancellationToken);
        var credentials = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var config in configs.Where(c => c.IsEnabled))
        {
            var credential = DecryptCredential(config);
            if (credential is not null)
            {
                credentials[config.ProviderId.ToLowerInvariant()] = credential;
            }
        }

        _logger.LogDebug("Retrieved {Count} credentials for user {UserId}",
            credentials.Count, userId);

        return credentials;
    }

    public async Task<string?> GetDecryptedCredentialAsync(
        Guid userId,
        string providerId,
        CancellationToken cancellationToken = default)
    {
        var config = await _configRepository.GetAsync(userId, providerId, cancellationToken);
        if (config is null || !config.IsEnabled)
        {
            return null;
        }

        return DecryptCredential(config);
    }

    private string? DecryptCredential(UserProviderConfig config)
    {
        try
        {
            // Prefer access token (OAuth) over API key
            if (!string.IsNullOrEmpty(config.EncryptedAccessToken))
            {
                return _encryption.Decrypt(config.EncryptedAccessToken);
            }

            if (!string.IsNullOrEmpty(config.EncryptedApiKey))
            {
                return _encryption.Decrypt(config.EncryptedApiKey);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to decrypt credential for provider {ProviderId}",
                config.ProviderId);
            return null;
        }
    }
}
