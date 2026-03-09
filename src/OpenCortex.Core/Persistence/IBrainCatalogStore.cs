using OpenCortex.Core.Brains;

namespace OpenCortex.Core.Persistence;

public interface IBrainCatalogStore
{
    Task<IReadOnlyList<BrainSummary>> ListBrainsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BrainSummary>> ListBrainsByCustomerAsync(string customerId, CancellationToken cancellationToken = default);

    Task<BrainDetail?> GetBrainAsync(string brainId, CancellationToken cancellationToken = default);

    Task<BrainDetail?> GetBrainByCustomerAsync(string customerId, string brainId, CancellationToken cancellationToken = default);

    Task<BrainDetail> CreateBrainAsync(BrainDefinition brain, CancellationToken cancellationToken = default);

    Task<BrainDetail?> UpdateBrainAsync(string brainId, string name, string slug, string mode, string status, string? description, CancellationToken cancellationToken = default);

    Task<bool> RetireBrainAsync(string brainId, CancellationToken cancellationToken = default);

    Task UpsertBrainsAsync(IReadOnlyList<BrainDefinition> brains, CancellationToken cancellationToken = default);

    Task<SourceRootSummary> AddSourceRootAsync(string brainId, SourceRootDefinition sourceRoot, CancellationToken cancellationToken = default);

    Task<SourceRootSummary?> UpdateSourceRootAsync(string brainId, string sourceRootId, string path, string pathType, bool isWritable, string[] includePatterns, string[] excludePatterns, string watchMode, CancellationToken cancellationToken = default);

    Task<bool> RemoveSourceRootAsync(string brainId, string sourceRootId, CancellationToken cancellationToken = default);
}
