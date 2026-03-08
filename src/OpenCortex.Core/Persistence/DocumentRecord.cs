namespace OpenCortex.Core.Persistence;

public sealed record DocumentRecord(
    string DocumentId,
    string BrainId,
    string? SourceRootId,
    string CanonicalPath,
    string Title,
    string? DocumentType,
    IReadOnlyDictionary<string, string> Frontmatter,
    string ContentHash,
    DateTimeOffset? SourceUpdatedAt,
    DateTimeOffset IndexedAt,
    bool IsDeleted);
