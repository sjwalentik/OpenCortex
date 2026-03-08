namespace OpenCortex.Core.Persistence;

public interface ILinkGraphStore
{
    Task UpsertEdgesAsync(IReadOnlyList<LinkEdgeRecord> edges, CancellationToken cancellationToken = default);

    Task DeleteStaleEdgesAsync(
        string brainId,
        IReadOnlyList<string> activeEdgeIds,
        IReadOnlyList<string> activeDocumentIds,
        CancellationToken cancellationToken = default);
}
