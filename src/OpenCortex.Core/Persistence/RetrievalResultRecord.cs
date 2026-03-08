namespace OpenCortex.Core.Persistence;

public sealed record RetrievalResultRecord(
    string DocumentId,
    string BrainId,
    string CanonicalPath,
    string Title,
    string? ChunkId,
    string? Snippet,
    double Score,
    string Reason);
