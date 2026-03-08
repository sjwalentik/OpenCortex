namespace OpenCortex.Core.Persistence;

public interface IDocumentCatalogStore
{
    Task UpsertDocumentsAsync(IReadOnlyList<DocumentRecord> documents, CancellationToken cancellationToken = default);
}
