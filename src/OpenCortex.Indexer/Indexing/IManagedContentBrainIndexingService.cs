using OpenCortex.Core.Persistence;

namespace OpenCortex.Indexer.Indexing;

public interface IManagedContentBrainIndexingService
{
    Task<IndexRunRecord> ReindexAsync(
        string customerId,
        string brainId,
        string triggerType,
        CancellationToken cancellationToken = default);
}
