namespace OpenCortex.Core.Persistence;

public interface IDocumentCatalogStore
{
    Task UpsertDocumentsAsync(IReadOnlyList<DocumentRecord> documents, CancellationToken cancellationToken = default);

    Task MarkMissingDocumentsDeletedAsync(
        string brainId,
        string sourceRootId,
        IReadOnlyList<string> activeCanonicalPaths,
        DateTimeOffset indexedAt,
        CancellationToken cancellationToken = default);

    Task MarkMissingManagedDocumentsDeletedAsync(
        string brainId,
        IReadOnlyList<string> activeCanonicalPaths,
        DateTimeOffset indexedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all non-deleted documents for the given brain, ordered by canonical path.
    /// Supports optional filtering by source root ID and path prefix.
    /// </summary>
    Task<IReadOnlyList<DocumentListItem>> ListDocumentsAsync(
        string brainId,
        string? sourceRootId = null,
        string? pathPrefix = null,
        int limit = 200,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Lightweight projection of a document suitable for listing and browsing.
/// Does not include full content or chunk data.
/// </summary>
public sealed record DocumentListItem(
    string DocumentId,
    string BrainId,
    string? SourceRootId,
    string CanonicalPath,
    string Title,
    string? DocumentType,
    DateTimeOffset? SourceUpdatedAt,
    DateTimeOffset IndexedAt);
