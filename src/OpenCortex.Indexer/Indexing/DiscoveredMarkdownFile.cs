namespace OpenCortex.Indexer.Indexing;

public sealed record DiscoveredMarkdownFile(
    string SourceRootId,
    string AbsolutePath,
    string CanonicalPath,
    DateTimeOffset LastModifiedAt);
