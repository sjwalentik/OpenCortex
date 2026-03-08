using OpenCortex.Core.Persistence;

namespace OpenCortex.Indexer.Indexing;

public sealed class BrainIngestionPersistenceCoordinator
{
    private readonly IDocumentCatalogStore _documentStore;
    private readonly IChunkStore _chunkStore;
    private readonly ILinkGraphStore _linkGraphStore;
    private readonly IIndexRunStore _indexRunStore;
    private readonly IEmbeddingStore _embeddingStore;

    public BrainIngestionPersistenceCoordinator(
        IDocumentCatalogStore documentStore,
        IChunkStore chunkStore,
        ILinkGraphStore linkGraphStore,
        IIndexRunStore indexRunStore,
        IEmbeddingStore embeddingStore)
    {
        _documentStore = documentStore;
        _chunkStore = chunkStore;
        _linkGraphStore = linkGraphStore;
        _indexRunStore = indexRunStore;
        _embeddingStore = embeddingStore;
    }

    public async Task<IndexRunRecord> PersistAsync(
        BrainIngestionBatch batch,
        string triggerType,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var indexRun = new IndexRunRecord(
            Guid.NewGuid().ToString("n"),
            batch.BrainId,
            triggerType,
            "running",
            startedAt,
            null,
            batch.Documents.Count,
            0,
            0,
            null);

        await _indexRunStore.StartIndexRunAsync(indexRun, cancellationToken);

        try
        {
            await _documentStore.UpsertDocumentsAsync(batch.Documents, cancellationToken);
            await _chunkStore.UpsertChunksAsync(batch.Chunks, cancellationToken);
            await _linkGraphStore.UpsertEdgesAsync(batch.LinkEdges, cancellationToken);
            await _embeddingStore.UpsertEmbeddingsAsync(batch.Embeddings, cancellationToken);

            var completedRun = indexRun with
            {
                Status = "completed",
                CompletedAt = DateTimeOffset.UtcNow,
                DocumentsIndexed = batch.Documents.Count,
            };

            await _indexRunStore.CompleteIndexRunAsync(completedRun, cancellationToken);
            return completedRun;
        }
        catch (Exception ex)
        {
            var failedRun = indexRun with
            {
                Status = "failed",
                CompletedAt = DateTimeOffset.UtcNow,
                DocumentsFailed = batch.Documents.Count,
                ErrorSummary = ex.Message,
            };

            await _indexRunStore.CompleteIndexRunAsync(failedRun, cancellationToken);
            throw;
        }
    }
}
