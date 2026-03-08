using OpenCortex.Core.Persistence;

namespace OpenCortex.Indexer.Indexing;

public sealed record BrainIngestionBatch(
    string BrainId,
    IReadOnlyList<DocumentRecord> Documents,
    IReadOnlyList<ChunkRecord> Chunks,
    IReadOnlyList<LinkEdgeRecord> LinkEdges,
    IReadOnlyList<EmbeddingRecord> Embeddings);
