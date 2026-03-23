using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Orchestration;

/// <summary>
/// Factory for creating provider instances with user-specific credentials.
/// </summary>
public interface IUserProviderFactory
{
    /// <summary>
    /// Get a provider configured for a specific user.
    /// </summary>
    Task<IModelProvider?> GetProviderForUserAsync(Guid customerId, Guid userId, string providerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all enabled providers for a user.
    /// </summary>
    Task<IReadOnlyList<IModelProvider>> GetProvidersForUserAsync(Guid customerId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a user has any configured providers.
    /// </summary>
    Task<bool> HasConfiguredProvidersAsync(Guid customerId, Guid userId, CancellationToken cancellationToken = default);
}
