namespace OpenCortex.Core.Persistence;

public interface IEmbeddingStore
{
    Task UpsertEmbeddingsAsync(IReadOnlyList<EmbeddingRecord> embeddings, CancellationToken cancellationToken = default);
}
