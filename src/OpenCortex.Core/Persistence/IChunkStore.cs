namespace OpenCortex.Core.Persistence;

public interface IChunkStore
{
    Task UpsertChunksAsync(IReadOnlyList<ChunkRecord> chunks, CancellationToken cancellationToken = default);
}
