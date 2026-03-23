namespace OpenCortex.Orchestration.Memory;

public interface IMemoryBrainResolver
{
    Task<MemoryBrainResult> ResolveAsync(
        string customerId,
        string userId,
        CancellationToken cancellationToken = default);
}

public sealed record MemoryBrainResult(
    bool Success,
    string? BrainId,
    string? Error,
    bool NeedsConfiguration);
