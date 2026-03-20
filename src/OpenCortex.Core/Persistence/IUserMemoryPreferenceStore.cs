namespace OpenCortex.Core.Persistence;

public interface IUserMemoryPreferenceStore
{
    Task<string?> GetMemoryBrainIdAsync(string userId, CancellationToken cancellationToken = default);

    Task SetMemoryBrainIdAsync(string userId, string? memoryBrainId, CancellationToken cancellationToken = default);
}
