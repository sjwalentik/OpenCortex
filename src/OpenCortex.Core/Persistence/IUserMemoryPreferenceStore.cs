namespace OpenCortex.Core.Persistence;

public interface IUserMemoryPreferenceStore
{
    Task<string?> GetMemoryBrainIdAsync(string customerId, string userId, CancellationToken cancellationToken = default);

    Task SetMemoryBrainIdAsync(string customerId, string userId, string? memoryBrainId, CancellationToken cancellationToken = default);
}
