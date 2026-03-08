using OpenCortex.Core.Persistence;
using OpenCortex.Indexer.Indexing;

namespace OpenCortex.Indexer.Tests;

public sealed class BrainIngestionPersistenceCoordinatorTests
{
    [Fact]
    public async Task PersistAsync_WritesDocumentsChunksEdgesAndCompletesRun()
    {
        var documentStore = new FakeDocumentStore();
        var chunkStore = new FakeChunkStore();
        var edgeStore = new FakeLinkGraphStore();
        var indexRunStore = new FakeIndexRunStore();
        var embeddingStore = new FakeEmbeddingStore();
        var coordinator = new BrainIngestionPersistenceCoordinator(documentStore, chunkStore, edgeStore, indexRunStore, embeddingStore);

        var batch = new BrainIngestionBatch(
            "brain-a",
            ["root-1"],
            [new DocumentRecord("doc-1", "brain-a", "root-1", "path.md", "Path", null, new Dictionary<string, string>(), "hash", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false)],
            [new ChunkRecord("chunk-1", "brain-a", "doc-1", 0, "hello", null, 1)],
            [new LinkEdgeRecord("edge-1", "brain-a", "doc-1", null, "Target", null, "wiki")],
            [new EmbeddingRecord("embedding-1", "brain-a", "chunk-1", "model", 3, [0.1f, 0.2f, 0.3f])]);

        var result = await coordinator.PersistAsync(batch, "test");

        Assert.Equal("completed", result.Status);
        Assert.Single(documentStore.Documents);
        Assert.Single(documentStore.Reconciliations);
        Assert.Single(chunkStore.Chunks);
        Assert.Single(chunkStore.CleanupCalls);
        Assert.Single(edgeStore.Edges);
        Assert.Single(edgeStore.CleanupCalls);
        Assert.Single(embeddingStore.Embeddings);
        Assert.Single(embeddingStore.CleanupCalls);
        Assert.Equal(2, indexRunStore.IndexRuns.Count);
        Assert.Equal("running", indexRunStore.IndexRuns[0].Status);
        Assert.Equal("completed", indexRunStore.IndexRuns[1].Status);
    }

    [Fact]
    public async Task PersistAsync_OnFailureRecordsIndexRunError()
    {
        var indexRunStore = new FakeIndexRunStore();
        var coordinator = new BrainIngestionPersistenceCoordinator(
            new ThrowingDocumentStore(),
            new FakeChunkStore(),
            new FakeLinkGraphStore(),
            indexRunStore,
            new FakeEmbeddingStore());

        var batch = new BrainIngestionBatch(
            "brain-a",
            ["root-1"],
            [new DocumentRecord("doc-1", "brain-a", "root-1", "path.md", "Path", null, new Dictionary<string, string>(), "hash", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false)],
            [],
            [],
            []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PersistAsync(batch, "test"));

        Assert.Single(indexRunStore.Errors);
        Assert.Equal("InvalidOperationException", indexRunStore.Errors[0].ErrorCode);
        Assert.Contains("boom", indexRunStore.Errors[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeDocumentStore : IDocumentCatalogStore
    {
        public List<DocumentRecord> Documents { get; } = [];
        public List<(string BrainId, string SourceRootId, IReadOnlyList<string> ActivePaths)> Reconciliations { get; } = [];

        public Task UpsertDocumentsAsync(IReadOnlyList<DocumentRecord> documents, CancellationToken cancellationToken = default)
        {
            Documents.AddRange(documents);
            return Task.CompletedTask;
        }

        public Task MarkMissingDocumentsDeletedAsync(string brainId, string sourceRootId, IReadOnlyList<string> activeCanonicalPaths, DateTimeOffset indexedAt, CancellationToken cancellationToken = default)
        {
            Reconciliations.Add((brainId, sourceRootId, activeCanonicalPaths));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DocumentListItem>> ListDocumentsAsync(string brainId, string? sourceRootId = null, string? pathPrefix = null, int limit = 200, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DocumentListItem>>([]);
    }

    private sealed class ThrowingDocumentStore : IDocumentCatalogStore
    {
        public Task UpsertDocumentsAsync(IReadOnlyList<DocumentRecord> documents, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("boom");
        }

        public Task MarkMissingDocumentsDeletedAsync(string brainId, string sourceRootId, IReadOnlyList<string> activeCanonicalPaths, DateTimeOffset indexedAt, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DocumentListItem>> ListDocumentsAsync(string brainId, string? sourceRootId = null, string? pathPrefix = null, int limit = 200, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DocumentListItem>>([]);
    }

    private sealed class FakeChunkStore : IChunkStore
    {
        public List<ChunkRecord> Chunks { get; } = [];
        public List<(string BrainId, IReadOnlyList<string> ChunkIds, IReadOnlyList<string> DocumentIds)> CleanupCalls { get; } = [];

        public Task UpsertChunksAsync(IReadOnlyList<ChunkRecord> chunks, CancellationToken cancellationToken = default)
        {
            Chunks.AddRange(chunks);
            return Task.CompletedTask;
        }

        public Task DeleteStaleChunksAsync(string brainId, IReadOnlyList<string> activeChunkIds, IReadOnlyList<string> activeDocumentIds, CancellationToken cancellationToken = default)
        {
            CleanupCalls.Add((brainId, activeChunkIds, activeDocumentIds));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLinkGraphStore : ILinkGraphStore
    {
        public List<LinkEdgeRecord> Edges { get; } = [];
        public List<(string BrainId, IReadOnlyList<string> EdgeIds, IReadOnlyList<string> DocumentIds)> CleanupCalls { get; } = [];

        public Task UpsertEdgesAsync(IReadOnlyList<LinkEdgeRecord> edges, CancellationToken cancellationToken = default)
        {
            Edges.AddRange(edges);
            return Task.CompletedTask;
        }

        public Task DeleteStaleEdgesAsync(string brainId, IReadOnlyList<string> activeEdgeIds, IReadOnlyList<string> activeDocumentIds, CancellationToken cancellationToken = default)
        {
            CleanupCalls.Add((brainId, activeEdgeIds, activeDocumentIds));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIndexRunStore : IIndexRunStore
    {
        public List<IndexRunRecord> IndexRuns { get; } = [];
        public List<IndexRunErrorRecord> Errors { get; } = [];

        public Task StartIndexRunAsync(IndexRunRecord indexRun, CancellationToken cancellationToken = default)
        {
            IndexRuns.Add(indexRun);
            return Task.CompletedTask;
        }

        public Task CompleteIndexRunAsync(IndexRunRecord indexRun, CancellationToken cancellationToken = default)
        {
            IndexRuns.Add(indexRun);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IndexRunRecord>> ListIndexRunsAsync(string? brainId = null, int limit = 20, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<IndexRunRecord> result = string.IsNullOrWhiteSpace(brainId)
                ? IndexRuns.Take(limit).ToArray()
                : IndexRuns.Where(run => run.BrainId == brainId).Take(limit).ToArray();

            return Task.FromResult(result);
        }

        public Task<IndexRunRecord?> GetIndexRunAsync(string indexRunId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IndexRuns.LastOrDefault(run => run.IndexRunId == indexRunId));
        }

        public Task AddIndexRunErrorAsync(IndexRunErrorRecord error, CancellationToken cancellationToken = default)
        {
            Errors.Add(error);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IndexRunErrorRecord>> ListIndexRunErrorsAsync(string indexRunId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<IndexRunErrorRecord> result = Errors.Where(error => error.IndexRunId == indexRunId).ToArray();
            return Task.FromResult(result);
        }
    }

    private sealed class FakeEmbeddingStore : IEmbeddingStore
    {
        public List<EmbeddingRecord> Embeddings { get; } = [];
        public List<(string BrainId, IReadOnlyList<string> EmbeddingIds, IReadOnlyList<string> ChunkIds)> CleanupCalls { get; } = [];

        public Task UpsertEmbeddingsAsync(IReadOnlyList<EmbeddingRecord> embeddings, CancellationToken cancellationToken = default)
        {
            Embeddings.AddRange(embeddings);
            return Task.CompletedTask;
        }

        public Task DeleteStaleEmbeddingsAsync(string brainId, IReadOnlyList<string> activeEmbeddingIds, IReadOnlyList<string> activeChunkIds, CancellationToken cancellationToken = default)
        {
            CleanupCalls.Add((brainId, activeEmbeddingIds, activeChunkIds));
            return Task.CompletedTask;
        }
    }
}
