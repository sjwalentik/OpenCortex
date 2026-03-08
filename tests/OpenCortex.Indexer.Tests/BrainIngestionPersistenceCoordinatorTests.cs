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
            [new DocumentRecord("doc-1", "brain-a", "root-1", "path.md", "Path", null, new Dictionary<string, string>(), "hash", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false)],
            [new ChunkRecord("chunk-1", "brain-a", "doc-1", 0, "hello", null, 1)],
            [new LinkEdgeRecord("edge-1", "brain-a", "doc-1", null, "Target", null, "wiki")],
            [new EmbeddingRecord("embedding-1", "brain-a", "chunk-1", "model", 3, [0.1f, 0.2f, 0.3f])]);

        var result = await coordinator.PersistAsync(batch, "test");

        Assert.Equal("completed", result.Status);
        Assert.Single(documentStore.Documents);
        Assert.Single(chunkStore.Chunks);
        Assert.Single(edgeStore.Edges);
        Assert.Single(embeddingStore.Embeddings);
        Assert.Equal(2, indexRunStore.IndexRuns.Count);
        Assert.Equal("running", indexRunStore.IndexRuns[0].Status);
        Assert.Equal("completed", indexRunStore.IndexRuns[1].Status);
    }

    private sealed class FakeDocumentStore : IDocumentCatalogStore
    {
        public List<DocumentRecord> Documents { get; } = [];

        public Task UpsertDocumentsAsync(IReadOnlyList<DocumentRecord> documents, CancellationToken cancellationToken = default)
        {
            Documents.AddRange(documents);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeChunkStore : IChunkStore
    {
        public List<ChunkRecord> Chunks { get; } = [];

        public Task UpsertChunksAsync(IReadOnlyList<ChunkRecord> chunks, CancellationToken cancellationToken = default)
        {
            Chunks.AddRange(chunks);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLinkGraphStore : ILinkGraphStore
    {
        public List<LinkEdgeRecord> Edges { get; } = [];

        public Task UpsertEdgesAsync(IReadOnlyList<LinkEdgeRecord> edges, CancellationToken cancellationToken = default)
        {
            Edges.AddRange(edges);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIndexRunStore : IIndexRunStore
    {
        public List<IndexRunRecord> IndexRuns { get; } = [];

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
    }

    private sealed class FakeEmbeddingStore : IEmbeddingStore
    {
        public List<EmbeddingRecord> Embeddings { get; } = [];

        public Task UpsertEmbeddingsAsync(IReadOnlyList<EmbeddingRecord> embeddings, CancellationToken cancellationToken = default)
        {
            Embeddings.AddRange(embeddings);
            return Task.CompletedTask;
        }
    }
}
