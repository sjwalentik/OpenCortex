namespace OpenCortex.Core.Persistence;

public interface ILinkGraphStore
{
    Task UpsertEdgesAsync(IReadOnlyList<LinkEdgeRecord> edges, CancellationToken cancellationToken = default);
}
