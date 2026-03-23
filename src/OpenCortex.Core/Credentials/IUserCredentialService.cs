namespace OpenCortex.Core.Credentials;

/// <summary>
/// Service for retrieving decrypted user credentials for tool execution.
/// </summary>
public interface IUserCredentialService
{
    /// <summary>
    /// Get all decrypted credentials for a user, keyed by provider ID.
    /// Only returns credentials for enabled providers.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="customerId">Customer ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of provider ID to decrypted credential (API key or access token).</returns>
    Task<IReadOnlyDictionary<string, string>> GetDecryptedCredentialsAsync(
        Guid customerId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get decrypted credential for a specific provider.
    /// </summary>
    /// <param name="customerId">Customer ID.</param>
    /// <param name="userId">User ID.</param>
    /// <param name="providerId">Provider ID (e.g., "github", "anthropic").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Decrypted credential, or null if not configured.</returns>
    Task<string?> GetDecryptedCredentialAsync(
        Guid customerId,
        Guid userId,
        string providerId,
        CancellationToken cancellationToken = default);
}
