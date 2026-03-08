namespace OpenCortex.Indexer.Indexing;

public sealed record ParsedMarkdownDocument(
    IReadOnlyDictionary<string, string> Frontmatter,
    string Body,
    IReadOnlyList<string> WikiLinks,
    IReadOnlyList<MarkdownChunk> Chunks);
