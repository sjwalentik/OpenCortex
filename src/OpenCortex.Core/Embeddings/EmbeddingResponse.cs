namespace OpenCortex.Core.Embeddings;

public sealed record EmbeddingResponse(string Model, int Dimensions, IReadOnlyList<float> Vector);
