namespace OpenCortex.Core.Persistence;

public interface IChunkStore
{
    Task UpsertChunksAsync(IReadOnlyList<ChunkRecord> chunks, CancellationToken cancellationToken = default);

    Task DeleteStaleChunksAsync(
        string brainId,
        IReadOnlyList<string> activeChunkIds,
        IReadOnlyList<string> activeDocumentIds,
        CancellationToken cancellationToken = default);
}
