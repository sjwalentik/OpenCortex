using System.Text.Json;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Query;
using OpenCortex.Indexer.Indexing;
using OpenCortex.Orchestration.Memory;
using OpenCortex.Retrieval.Execution;
using OpenCortex.Tools;
using OpenCortex.Tools.Memory.Handlers;

namespace OpenCortex.Api.Tests;

public sealed class MemoryToolHandlersTests
{
    [Fact]
    public async Task SaveMemoryHandler_CreatesManagedDocumentUnderMemoriesPath()
    {
        var documentStore = new StubManagedDocumentStore();
        var indexingService = new StubManagedContentBrainIndexingService();
        var handler = new SaveMemoryHandler(
            documentStore,
            new StubMemoryBrainResolver(new MemoryBrainResult(true, "brain-memory", null, false)),
            indexingService);

        var response = await handler.ExecuteAsync(
            JsonDocument.Parse("""
            {
              "content": "The user prefers concise architecture summaries.",
              "category": "preference",
              "confidence": "high",
              "tags": ["user", "style"]
            }
            """).RootElement,
            CreateContext(),
            CancellationToken.None);

        using var json = JsonDocument.Parse(response);

        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Single(documentStore.Documents);

        var saved = documentStore.Documents[0];
        Assert.StartsWith("memories/preference/", saved.CanonicalPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("published", saved.Status);
        Assert.Equal("preference", saved.Frontmatter["category"]);
        Assert.Equal("high", saved.Frontmatter["confidence"]);
        Assert.Equal("user,style", saved.Frontmatter["tags"]);
        Assert.Single(indexingService.Calls);
        Assert.Equal("memory-save", indexingService.Calls[0].TriggerType);
    }

    [Fact]
    public async Task RecallMemoriesHandler_UsesPathPrefixScopedOqlAndReturnsMemoryMetadata()
    {
        var queryStore = new RecordingDocumentQueryStore(
        [
            new RetrievalResultRecord(
                "memory-doc-1",
                "brain-memory",
                "memories/decision/abc123.md",
                "[decision] Use OQL path prefix on day one",
                null,
                "Use OQL path prefix on day one for memory recall.",
                0.91,
                "semantic",
                new ScoreBreakdown(0.0, 0.91, 0.0))
        ]);
        var documentStore = new StubManagedDocumentStore();
        await documentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest(
                BrainId: "brain-memory",
                CustomerId: "cust-test",
                Title: "[decision] Use OQL path prefix on day one",
                Slug: "memories/decision/abc123",
                Content: "Use OQL path prefix on day one for memory recall.",
                Frontmatter: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["category"] = "decision",
                    ["confidence"] = "high",
                    ["tags"] = "roadmap,p1"
                },
                Status: "published",
                UserId: "user-test"),
            CancellationToken.None);

        var handler = new RecallMemoriesHandler(
            new OqlQueryExecutor(queryStore),
            documentStore,
            new StubMemoryBrainResolver(new MemoryBrainResult(true, "brain-memory", null, false)));

        var response = await handler.ExecuteAsync(
            JsonDocument.Parse("""
            {
              "query": "day one memory recall",
              "category": "decision",
              "limit": 3
            }
            """).RootElement,
            CreateContext(),
            CancellationToken.None);

        using var json = JsonDocument.Parse(response);

        Assert.NotNull(queryStore.LastQuery);
        Assert.Equal("brain-memory", queryStore.LastQuery!.BrainId);
        Assert.Equal("day one memory recall", queryStore.LastQuery.SearchText);
        Assert.Contains(queryStore.LastQuery.Filters, filter => filter.Field == "path_prefix" && filter.Value == "memories/decision/");

        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("count").GetInt32());

        var memory = json.RootElement.GetProperty("memories")[0];
        Assert.Equal("memories/decision/abc123.md", memory.GetProperty("path").GetString());
        Assert.Equal("decision", memory.GetProperty("category").GetString());
        Assert.Equal("high", memory.GetProperty("confidence").GetString());
    }

    [Fact]
    public async Task ForgetMemoryHandler_SoftDeletesManagedMemoryDocument()
    {
        var documentStore = new StubManagedDocumentStore();
        var indexingService = new StubManagedContentBrainIndexingService();
        var document = await documentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest(
                BrainId: "brain-memory",
                CustomerId: "cust-test",
                Title: "[fact] Ollama is the default local provider",
                Slug: "memories/fact/ollama-default",
                Content: "Ollama is the default local provider.",
                Frontmatter: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["category"] = "fact"
                },
                Status: "published",
                UserId: "user-test"),
            CancellationToken.None);

        var handler = new ForgetMemoryHandler(
            documentStore,
            new StubMemoryBrainResolver(new MemoryBrainResult(true, "brain-memory", null, false)),
            indexingService);

        var response = await handler.ExecuteAsync(
            JsonDocument.Parse("""
            {
              "memory_path": "memories/fact/ollama-default.md",
              "reason": "Superseded by a newer routing rule."
            }
            """).RootElement,
            CreateContext(),
            CancellationToken.None);

        using var json = JsonDocument.Parse(response);

        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("memories/fact/ollama-default.md", json.RootElement.GetProperty("forgotten").GetString());
        Assert.Contains(documentStore.SoftDeletedManagedDocumentIds, id => id == document.ManagedDocumentId);
        Assert.Single(indexingService.Calls);
        Assert.Equal("memory-forget", indexingService.Calls[0].TriggerType);
    }

    private static ToolExecutionContext CreateContext() => new()
    {
        UserId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        ConversationId = "conv-test",
        TenantUserId = "user-test",
        TenantCustomerId = "cust-test",
        BrainId = "brain-memory"
    };

    private sealed class StubMemoryBrainResolver(MemoryBrainResult result) : IMemoryBrainResolver
    {
        public Task<MemoryBrainResult> ResolveAsync(string customerId, string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class RecordingDocumentQueryStore(IReadOnlyList<RetrievalResultRecord> results) : IDocumentQueryStore
    {
        public OqlQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<RetrievalResultRecord>> SearchAsync(OqlQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(results);
        }
    }

    private sealed class StubManagedDocumentStore : IManagedDocumentStore
    {
        private readonly Dictionary<string, ManagedDocumentDetail> _documents = new(StringComparer.OrdinalIgnoreCase);
        private int _nextId = 1;

        public List<ManagedDocumentDetail> Documents => _documents.Values.ToList();

        public List<string> SoftDeletedManagedDocumentIds { get; } = [];

        public Task<IReadOnlyList<ManagedDocumentSummary>> ListManagedDocumentsAsync(
            string customerId,
            string brainId,
            string? pathPrefix = null,
            string? excludePathPrefix = null,
            int limit = 200,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ManagedDocumentSummary>>([]);

        public Task<int> CountActiveManagedDocumentsAsync(string customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(_documents.Values.Count(document =>
                string.Equals(document.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
                && !document.IsDeleted));

        public Task<IReadOnlyList<ManagedDocumentDetail>> ListManagedDocumentsForIndexingAsync(string customerId, string brainId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ManagedDocumentDetail>>(_documents.Values.ToList());

        public Task<ManagedDocumentDetail?> GetManagedDocumentAsync(string customerId, string brainId, string managedDocumentId, CancellationToken cancellationToken = default)
            => Task.FromResult(_documents.TryGetValue(managedDocumentId, out var document) ? document : null);

        public Task<ManagedDocumentDetail?> GetManagedDocumentByCanonicalPathAsync(string customerId, string brainId, string canonicalPath, CancellationToken cancellationToken = default)
            => Task.FromResult<ManagedDocumentDetail?>(_documents.Values.FirstOrDefault(document =>
                string.Equals(document.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(document.BrainId, brainId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(document.CanonicalPath, canonicalPath, StringComparison.OrdinalIgnoreCase)
                && !document.IsDeleted));

        public Task<IReadOnlyList<ManagedDocumentVersionSummary>> ListManagedDocumentVersionsAsync(string customerId, string brainId, string managedDocumentId, int limit = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ManagedDocumentVersionSummary>>([]);

        public Task<ManagedDocumentVersionDetail?> GetManagedDocumentVersionAsync(string customerId, string brainId, string managedDocumentId, string managedDocumentVersionId, CancellationToken cancellationToken = default)
            => Task.FromResult<ManagedDocumentVersionDetail?>(null);

        public Task<ManagedDocumentDetail> CreateManagedDocumentAsync(ManagedDocumentCreateRequest request, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var managedDocumentId = $"memory-{_nextId++:D4}";
            var slug = request.Slug ?? "memory";
            var document = new ManagedDocumentDetail(
                managedDocumentId,
                request.BrainId,
                request.CustomerId,
                request.Title,
                slug,
                OpenCortex.Core.Authoring.ManagedDocumentText.BuildCanonicalPath(slug),
                request.Content,
                new Dictionary<string, string>(request.Frontmatter, StringComparer.OrdinalIgnoreCase),
                "hash",
                request.Status,
                request.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                request.UserId,
                request.UserId,
                now,
                now,
                false);

            _documents[managedDocumentId] = document;
            return Task.FromResult(document);
        }

        public Task<ManagedDocumentDetail?> UpdateManagedDocumentAsync(ManagedDocumentUpdateRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> SoftDeleteManagedDocumentAsync(string customerId, string brainId, string managedDocumentId, string userId, CancellationToken cancellationToken = default)
        {
            if (!_documents.TryGetValue(managedDocumentId, out var document))
            {
                return Task.FromResult(false);
            }

            _documents[managedDocumentId] = document with
            {
                IsDeleted = true,
                UpdatedBy = userId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            SoftDeletedManagedDocumentIds.Add(managedDocumentId);
            return Task.FromResult(true);
        }

        public Task<ManagedDocumentDetail?> RestoreManagedDocumentVersionAsync(string customerId, string brainId, string managedDocumentId, string managedDocumentVersionId, string userId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubManagedContentBrainIndexingService : IManagedContentBrainIndexingService
    {
        public List<(string CustomerId, string BrainId, string TriggerType)> Calls { get; } = [];

        public Task<IndexRunRecord> ReindexAsync(string customerId, string brainId, string triggerType, CancellationToken cancellationToken = default)
        {
            Calls.Add((customerId, brainId, triggerType));
            return Task.FromResult(new IndexRunRecord(
                $"idx-{Calls.Count:D4}",
                brainId,
                triggerType,
                "completed",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                1,
                1,
                0,
                null));
        }
    }
}
