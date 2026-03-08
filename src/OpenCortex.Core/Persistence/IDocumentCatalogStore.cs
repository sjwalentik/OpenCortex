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
}
