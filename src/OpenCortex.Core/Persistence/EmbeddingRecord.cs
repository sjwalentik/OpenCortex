namespace OpenCortex.Core.Persistence;

public sealed record EmbeddingRecord(
    string EmbeddingId,
    string BrainId,
    string ChunkId,
    string Model,
    int Dimensions,
    IReadOnlyList<float> Vector);
