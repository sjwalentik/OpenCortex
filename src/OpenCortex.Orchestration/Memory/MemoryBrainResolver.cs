using OpenCortex.Core.Persistence;

namespace OpenCortex.Orchestration.Memory;

public sealed class MemoryBrainResolver : IMemoryBrainResolver
{
    private readonly IBrainCatalogStore _brainStore;
    private readonly IUserMemoryPreferenceStore _memoryPreferenceStore;

    public MemoryBrainResolver(
        IBrainCatalogStore brainStore,
        IUserMemoryPreferenceStore memoryPreferenceStore)
    {
        _brainStore = brainStore;
        _memoryPreferenceStore = memoryPreferenceStore;
    }

    public async Task<MemoryBrainResult> ResolveAsync(
        string customerId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var activeManagedContentBrains = (await _brainStore.ListBrainsByCustomerAsync(customerId, cancellationToken))
            .Where(brain =>
                string.Equals(brain.Status, "active", StringComparison.OrdinalIgnoreCase)
                && string.Equals(brain.Mode, "managed-content", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (activeManagedContentBrains.Count == 0)
        {
            return new MemoryBrainResult(
                Success: false,
                BrainId: null,
                Error: "No active managed-content brains found.",
                NeedsConfiguration: false);
        }

        if (activeManagedContentBrains.Count == 1)
        {
            return new MemoryBrainResult(
                Success: true,
                BrainId: activeManagedContentBrains[0].BrainId,
                Error: null,
                NeedsConfiguration: false);
        }

        var memoryBrainId = await _memoryPreferenceStore.GetMemoryBrainIdAsync(customerId, userId, cancellationToken);
        if (string.IsNullOrWhiteSpace(memoryBrainId))
        {
            return new MemoryBrainResult(
                Success: false,
                BrainId: null,
                Error: "Multiple active managed-content brains found. Configure a memory brain before using agent memory.",
                NeedsConfiguration: true);
        }

        var configuredBrain = activeManagedContentBrains
            .FirstOrDefault(brain => string.Equals(brain.BrainId, memoryBrainId, StringComparison.OrdinalIgnoreCase));

        if (configuredBrain is null)
        {
            return new MemoryBrainResult(
                Success: false,
                BrainId: null,
                Error: "Configured memory brain was not found or is no longer active.",
                NeedsConfiguration: true);
        }

        return new MemoryBrainResult(
            Success: true,
            BrainId: configuredBrain.BrainId,
            Error: null,
            NeedsConfiguration: false);
    }
}
