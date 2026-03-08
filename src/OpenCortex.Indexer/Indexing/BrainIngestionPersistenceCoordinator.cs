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
            var activeDocumentIds = batch.Documents.Select(document => document.DocumentId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var activeChunkIds = batch.Chunks.Select(chunk => chunk.ChunkId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var activeEdgeIds = batch.LinkEdges.Select(edge => edge.LinkEdgeId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var activeEmbeddingIds = batch.Embeddings.Select(embedding => embedding.EmbeddingId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            await _documentStore.UpsertDocumentsAsync(batch.Documents, cancellationToken);

            foreach (var sourceRootId in batch.SourceRootIds)
            {
                var activeCanonicalPaths = batch.Documents
                    .Where(document => string.Equals(document.SourceRootId, sourceRootId, StringComparison.OrdinalIgnoreCase))
                    .Select(document => document.CanonicalPath)
                    .ToArray();

                await _documentStore.MarkMissingDocumentsDeletedAsync(
                    batch.BrainId,
                    sourceRootId,
                    activeCanonicalPaths,
                    startedAt,
                    cancellationToken);
            }

            await _chunkStore.UpsertChunksAsync(batch.Chunks, cancellationToken);
            await _chunkStore.DeleteStaleChunksAsync(batch.BrainId, activeChunkIds, activeDocumentIds, cancellationToken);
            await _linkGraphStore.UpsertEdgesAsync(batch.LinkEdges, cancellationToken);
            await _linkGraphStore.DeleteStaleEdgesAsync(batch.BrainId, activeEdgeIds, activeDocumentIds, cancellationToken);
            await _embeddingStore.UpsertEmbeddingsAsync(batch.Embeddings, cancellationToken);
            await _embeddingStore.DeleteStaleEmbeddingsAsync(batch.BrainId, activeEmbeddingIds, activeChunkIds, cancellationToken);

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
            await _indexRunStore.AddIndexRunErrorAsync(
                new IndexRunErrorRecord(
                    Guid.NewGuid().ToString("n"),
                    indexRun.IndexRunId,
                    null,
                    null,
                    ex.GetType().Name,
                    ex.Message,
                    DateTimeOffset.UtcNow),
                cancellationToken);

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
