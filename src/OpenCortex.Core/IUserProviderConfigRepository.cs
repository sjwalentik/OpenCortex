namespace OpenCortex.Core;

/// <summary>
/// Repository for user-specific provider configurations.
/// </summary>
public interface IUserProviderConfigRepository
{
    Task<UserProviderConfig?> GetAsync(Guid userId, string providerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserProviderConfig>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserProviderConfig> UpsertAsync(UserProviderConfig config, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid userId, string providerId, CancellationToken cancellationToken = default);
    Task<bool> HasAnyConfiguredAsync(Guid userId, CancellationToken cancellationToken = default);
}
