namespace OpenCortex.Core.Embeddings;

public interface IEmbeddingProvider
{
    Task<EmbeddingResponse> GenerateAsync(string text, CancellationToken cancellationToken = default);
}
