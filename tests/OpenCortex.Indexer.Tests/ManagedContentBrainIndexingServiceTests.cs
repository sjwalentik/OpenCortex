using OpenCortex.Core.Embeddings;
using OpenCortex.Core.Persistence;
using OpenCortex.Indexer.Indexing;

namespace OpenCortex.Indexer.Tests;

public sealed class ManagedContentBrainIndexingServiceTests
{
    [Fact]
    public async Task ReindexAsync_BuildsDocumentsChunksEdgesAndRun_ForManagedDocuments()
    {
        var managedStore = new FakeManagedDocumentStore(
            new ManagedDocumentDetail(
                "mdoc-a",
                "brain-a",
                "cust-a",
                "Alpha",
                "alpha",
                "alpha.md",
                "# Alpha\nLinks to [[Beta]].",
                new Dictionary<string, string> { ["type"] = "note" },
                "hash-a",
                "draft",
                4,
                "user-a",
                "user-a",
                DateTimeOffset.UtcNow.AddMinutes(-10),
                DateTimeOffset.UtcNow.AddMinutes(-5),
                false),
            new ManagedDocumentDetail(
                "mdoc-b",
                "brain-a",
                "cust-a",
                "Beta",
                "beta",
                "beta.md",
                "# Beta\nTarget doc.",
                new Dictionary<string, string>(),
                "hash-b",
                "draft",
                3,
                "user-a",
                "user-a",
                DateTimeOffset.UtcNow.AddMinutes(-10),
                DateTimeOffset.UtcNow.AddMinutes(-3),
                false));

        var documentStore = new FakeDocumentStore();
        var chunkStore = new FakeChunkStore();
        var linkStore = new FakeLinkGraphStore();
        var runStore = new FakeIndexRunStore();
        var embeddingStore = new FakeEmbeddingStore();
        var service = new ManagedContentBrainIndexingService(
            managedStore,
            documentStore,
            chunkStore,
            linkStore,
            runStore,
            embeddingStore,
            new DeterministicEmbeddingProvider(new EmbeddingOptions { Model = "deterministic", Dimensions = 8 }));

        var result = await service.ReindexAsync("cust-a", "brain-a", "managed-document-save");

        Assert.Equal("completed", result.Status);
        Assert.Equal(2, documentStore.Documents.Count);
        Assert.Equal(2, documentStore.LastManagedPaths.Count);
        Assert.NotEmpty(chunkStore.Chunks);
        Assert.NotEmpty(embeddingStore.Embeddings);
        Assert.Single(linkStore.Edges.Where(edge => edge.ToDocumentId == "mdoc-b"));
        Assert.Equal(2, runStore.IndexRuns.Count);
        Assert.Equal("running", runStore.IndexRuns[0].Status);
        Assert.Equal("completed", runStore.IndexRuns[1].Status);
    }

    private sealed class FakeManagedDocumentStore : IManagedDocumentStore
    {
        private readonly IReadOnlyList<ManagedDocumentDetail> _documents;

        public FakeManagedDocumentStore(params ManagedDocumentDetail[] documents)
        {
            _documents = documents;
        }

        public Task<IReadOnlyList<ManagedDocumentSummary>> ListManagedDocumentsAsync(string customerId, string brainId, int limit = 200, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ManagedDocumentSummary>>([]);

        public Task<int> CountActiveManagedDocumentsAsync(string customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(_documents.Count(document => !document.IsDeleted));

        public Task<IReadOnlyList<ManagedDocumentDetail>> ListManagedDocumentsForIndexingAsync(string customerId, string brainId, CancellationToken cancellationToken = default)
            => Task.FromResult(_documents);

        public Task<ManagedDocumentDetail?> GetManagedDocumentAsync(string customerId, string brainId, string managedDocumentId, CancellationToken cancellationToken = default)
            => Task.FromResult(_documents.FirstOrDefault(document => document.ManagedDocumentId == managedDocumentId));

        public Task<ManagedDocumentDetail> CreateManagedDocumentAsync(ManagedDocumentCreateRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ManagedDocumentDetail?> UpdateManagedDocumentAsync(ManagedDocumentUpdateRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> SoftDeleteManagedDocumentAsync(string customerId, string brainId, string managedDocumentId, string userId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeDocumentStore : IDocumentCatalogStore
    {
        public List<DocumentRecord> Documents { get; } = [];
        public IReadOnlyList<string> LastManagedPaths { get; private set; } = [];

        public Task UpsertDocumentsAsync(IReadOnlyList<DocumentRecord> documents, CancellationToken cancellationToken = default)
        {
            Documents.AddRange(documents);
            return Task.CompletedTask;
        }

        public Task MarkMissingDocumentsDeletedAsync(string brainId, string sourceRootId, IReadOnlyList<string> activeCanonicalPaths, DateTimeOffset indexedAt, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkMissingManagedDocumentsDeletedAsync(string brainId, IReadOnlyList<string> activeCanonicalPaths, DateTimeOffset indexedAt, CancellationToken cancellationToken = default)
        {
            LastManagedPaths = activeCanonicalPaths;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DocumentListItem>> ListDocumentsAsync(string brainId, string? sourceRootId = null, string? pathPrefix = null, int limit = 200, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DocumentListItem>>([]);
    }

    private sealed class FakeChunkStore : IChunkStore
    {
        public List<ChunkRecord> Chunks { get; } = [];

        public Task UpsertChunksAsync(IReadOnlyList<ChunkRecord> chunks, CancellationToken cancellationToken = default)
        {
            Chunks.AddRange(chunks);
            return Task.CompletedTask;
        }

        public Task DeleteStaleChunksAsync(string brainId, IReadOnlyList<string> activeChunkIds, IReadOnlyList<string> activeDocumentIds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeLinkGraphStore : ILinkGraphStore
    {
        public List<LinkEdgeRecord> Edges { get; } = [];

        public Task UpsertEdgesAsync(IReadOnlyList<LinkEdgeRecord> edges, CancellationToken cancellationToken = default)
        {
            Edges.AddRange(edges);
            return Task.CompletedTask;
        }

        public Task DeleteStaleEdgesAsync(string brainId, IReadOnlyList<string> activeEdgeIds, IReadOnlyList<string> activeDocumentIds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeEmbeddingStore : IEmbeddingStore
    {
        public List<EmbeddingRecord> Embeddings { get; } = [];

        public Task UpsertEmbeddingsAsync(IReadOnlyList<EmbeddingRecord> embeddings, CancellationToken cancellationToken = default)
        {
            Embeddings.AddRange(embeddings);
            return Task.CompletedTask;
        }

        public Task DeleteStaleEmbeddingsAsync(string brainId, IReadOnlyList<string> activeEmbeddingIds, IReadOnlyList<string> activeChunkIds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
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
            => Task.FromResult<IReadOnlyList<IndexRunRecord>>(IndexRuns);

        public Task<IndexRunRecord?> GetIndexRunAsync(string indexRunId, CancellationToken cancellationToken = default)
            => Task.FromResult(IndexRuns.LastOrDefault(run => run.IndexRunId == indexRunId));

        public Task AddIndexRunErrorAsync(IndexRunErrorRecord error, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<IndexRunErrorRecord>> ListIndexRunErrorsAsync(string indexRunId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<IndexRunErrorRecord>>([]);
    }
}
