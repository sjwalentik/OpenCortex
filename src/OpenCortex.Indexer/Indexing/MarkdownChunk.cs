namespace OpenCortex.Indexer.Indexing;

public sealed record MarkdownChunk(
    int ChunkIndex,
    string? HeadingPath,
    string Content,
    int TokenCount);
