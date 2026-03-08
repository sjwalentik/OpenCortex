using OpenCortex.Core.Brains;

namespace OpenCortex.Core.Persistence;

public interface IBrainCatalogStore
{
    Task<IReadOnlyList<BrainSummary>> ListBrainsAsync(CancellationToken cancellationToken = default);

    Task UpsertBrainsAsync(IReadOnlyList<BrainDefinition> brains, CancellationToken cancellationToken = default);
}
