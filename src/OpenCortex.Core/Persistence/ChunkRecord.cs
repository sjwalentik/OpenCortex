namespace OpenCortex.Core.Persistence;

public sealed record ChunkRecord(
    string ChunkId,
    string BrainId,
    string DocumentId,
    int ChunkIndex,
    string Content,
    string? HeadingPath,
    int? TokenCount);
