using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCortex.Core.Configuration;
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
            indexingService,
            new StubSubscriptionStore(planId: "pro"),
            new OpenCortexOptions());

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
    public async Task SaveMemoryHandler_ReturnsFailure_WhenWorkspaceIsAtDocumentLimit()
    {
        var documentStore = new StubManagedDocumentStore();
        for (var index = 0; index < 10; index++)
        {
            await documentStore.CreateManagedDocumentAsync(
                new ManagedDocumentCreateRequest(
                    BrainId: "brain-memory",
                    CustomerId: "cust-test",
                    Title: $"Existing {index}",
                    Slug: $"existing/{index}",
                    Content: "existing content",
                    Frontmatter: new Dictionary<string, string>(),
                    Status: "published",
                    UserId: "user-test"),
                CancellationToken.None);
        }

        var handler = new SaveMemoryHandler(
            documentStore,
            new StubMemoryBrainResolver(new MemoryBrainResult(true, "brain-memory", null, false)),
            new StubManagedContentBrainIndexingService(),
            new StubSubscriptionStore(planId: "free"),
            new OpenCortexOptions());

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

        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("Document limit reached", json.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("memory_quota_reached", json.RootElement.GetProperty("error_code").GetString());
        Assert.True(json.RootElement.GetProperty("quota_exceeded").GetBoolean());
        Assert.Contains("forget_memory", json.RootElement.GetProperty("suggestion").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, documentStore.Documents.Count);
    }

    [Fact]
    public async Task SaveMemoryHandler_ReturnsFailure_WhenTenantScopeMissing()
    {
        var handler = new SaveMemoryHandler(
            new StubManagedDocumentStore(),
            new StubMemoryBrainResolver(new MemoryBrainResult(true, "brain-memory", null, false)),
            new StubManagedContentBrainIndexingService(),
            new StubSubscriptionStore(planId: "pro"),
            new OpenCortexOptions());

        var response = await handler.ExecuteAsync(
            JsonDocument.Parse("""
            {
              "content": "The user prefers concise architecture summaries.",
              "category": "preference"
            }
            """).RootElement,
            new ToolExecutionContext
            {
                UserId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                ConversationId = "conv-test",
                BrainId = "brain-memory"
            },
            CancellationToken.None);

        using var json = JsonDocument.Parse(response);

        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("hosted tenant context", json.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveMemoryHandler_ReturnsFailure_WhenCategoryIsInvalid()
    {
        var handler = new SaveMemoryHandler(
            new StubManagedDocumentStore(),
            new StubMemoryBrainResolver(new MemoryBrainResult(true, "brain-memory", null, false)),
            new StubManagedContentBrainIndexingService(),
            new StubSubscriptionStore(planId: "pro"),
            new OpenCortexOptions());

        var response = await handler.ExecuteAsync(
            JsonDocument.Parse("""
            {
              "content": "The user prefers concise architecture summaries.",
              "category": "secret"
            }
            """).RootElement,
            CreateContext(),
            CancellationToken.None);

        using var json = JsonDocument.Parse(response);

        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("Unsupported memory category", json.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveMemoryHandler_ReturnsExistingMemory_WhenDuplicateFound()
    {
        var documentStore = new StubManagedDocumentStore();
        var indexingService = new StubManagedContentBrainIndexingService();
        var existing = await documentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest(
                BrainId: "brain-memory",
                CustomerId: "cust-test",
                Title: "[preference] Concise summaries",
                Slug: "memories/preference/existing",
                Content: "The user prefers concise architecture summaries.",
                Frontmatter: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["category"] = "preference",
                    ["confidence"] = "high",
                    ["tags"] = "user,style"
                },
                Status: "published",
                UserId: "user-test"),
            CancellationToken.None);

        var handler = new SaveMemoryHandler(
            documentStore,
            new StubMemoryBrainResolver(new MemoryBrainResult(true, "brain-memory", null, false)),
            indexingService,
            new StubSubscriptionStore(planId: "pro"),
            new OpenCortexOptions());

        var response = await handler.ExecuteAsync(
            JsonDocument.Parse("""
            {
              "content": "The user prefers concise architecture summaries",
              "category": "preference",
              "confidence": "medium"
            }
            """).RootElement,
            CreateContext(),
            CancellationToken.None);

        using var json = JsonDocument.Parse(response);

        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.True(json.RootElement.GetProperty("duplicate").GetBoolean());
        Assert.Equal(existing.CanonicalPath, json.RootElement.GetProperty("memory_path").GetString());
        Assert.Single(documentStore.Documents);
        Assert.Equal(1, documentStore.ListManagedDocumentsCalls);
        Assert.Equal("memories/preference/", documentStore.LastListPathPrefix);
        Assert.Equal(0, documentStore.ListForIndexingCalls);
        Assert.Empty(indexingService.Calls);
    }

    [Fact]
    public async Task RecallMemoriesHandler_EscapesOqlSearchLiteral()
    {
        var queryStore = new RecordingDocumentQueryStore([]);
        var handler = new RecallMemoriesHandler(
            new OqlQueryExecutor(queryStore),
            new StubManagedDocumentStore(),
            new StubMemoryBrainResolver(new MemoryBrainResult(true, "brain-memory", null, false)),
            NullLogger<RecallMemoriesHandler>.Instance);
        var injectedQuery = "decision\" WHERE path_prefix = \"docs/";
        var payload = JsonSerializer.Serialize(new
        {
            query = injectedQuery,
            limit = 3
        });

        var response = await handler.ExecuteAsync(
            JsonDocument.Parse(payload).RootElement,
            CreateContext(),
            CancellationToken.None);

        using var json = JsonDocument.Parse(response);

        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.NotNull(queryStore.LastQuery);
        Assert.DoesNotContain("path_prefix", queryStore.LastQuery!.SearchText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("hybrid", queryStore.LastQuery.RankMode);
        Assert.Contains(queryStore.LastQuery.Filters, filter => filter.Field == "path_prefix" && filter.Value == "memories/");
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
            new StubMemoryBrainResolver(new MemoryBrainResult(true, "brain-memory", null, false)),
            NullLogger<RecallMemoriesHandler>.Instance);

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
        Assert.Equal(0.91, memory.GetProperty("base_score").GetDouble(), precision: 6);
        Assert.True(memory.GetProperty("score").GetDouble() > memory.GetProperty("base_score").GetDouble());
    }

    [Fact]
    public async Task RecallMemoriesHandler_FiltersWeakMatchesAfterConfidenceAdjustment()
    {
        var queryStore = new RecordingDocumentQueryStore(
        [
            new RetrievalResultRecord(
                "memory-doc-1",
                "brain-memory",
                "memories/fact/weak.md",
                "[fact] Weak match",
                null,
                "Weak match.",
                0.01,
                "semantic",
                new ScoreBreakdown(0.0, 0.01, 0.0))
        ]);
        var documentStore = new StubManagedDocumentStore();
        await documentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest(
                BrainId: "brain-memory",
                CustomerId: "cust-test",
                Title: "[fact] Weak match",
                Slug: "memories/fact/weak",
                Content: "Weak match.",
                Frontmatter: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["category"] = "fact",
                    ["confidence"] = "low"
                },
                Status: "published",
                UserId: "user-test"),
            CancellationToken.None);

        var handler = new RecallMemoriesHandler(
            new OqlQueryExecutor(queryStore),
            documentStore,
            new StubMemoryBrainResolver(new MemoryBrainResult(true, "brain-memory", null, false)),
            NullLogger<RecallMemoriesHandler>.Instance);

        var response = await handler.ExecuteAsync(
            JsonDocument.Parse("""
            {
              "query": "unrelated",
              "minimum_score": 0.05
            }
            """).RootElement,
            CreateContext(),
            CancellationToken.None);

        using var json = JsonDocument.Parse(response);

        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task ForgetMemoryHandler_ReturnsFailure_WhenPathContainsTraversal()
    {
        var handler = new ForgetMemoryHandler(
            new StubManagedDocumentStore(),
            new StubMemoryBrainResolver(new MemoryBrainResult(true, "brain-memory", null, false)),
            new StubManagedContentBrainIndexingService());

        var response = await handler.ExecuteAsync(
            JsonDocument.Parse("""
            {
              "memory_path": "memories/../docs/secret.md"
            }
            """).RootElement,
            CreateContext(),
            CancellationToken.None);

        using var json = JsonDocument.Parse(response);

        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("Invalid memory path", json.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
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

    private sealed class StubSubscriptionStore(string planId = "free", string status = "active") : ISubscriptionStore
    {
        public Task<SubscriptionRecord> EnsureFreeSubscriptionAsync(string customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(BuildSubscription(customerId, "free", "active"));

        public Task<SubscriptionRecord?> GetSubscriptionAsync(string customerId, CancellationToken cancellationToken = default)
            => Task.FromResult<SubscriptionRecord?>(BuildSubscription(customerId, planId, status));

        public Task<CustomerBillingProfile?> GetCustomerBillingProfileAsync(string customerId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<string?> FindCustomerIdByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task LinkStripeCustomerAsync(string customerId, string stripeCustomerId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SubscriptionRecord> UpsertSubscriptionAsync(SubscriptionUpsertRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> TryRecordSubscriptionEventAsync(SubscriptionEventRecord record, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task MarkSubscriptionEventProcessedAsync(string stripeEventId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        private static SubscriptionRecord BuildSubscription(string customerId, string planId, string status) =>
            new(
                "sub_test",
                customerId,
                planId,
                status,
                null,
                null,
                1,
                null,
                DateTimeOffset.UtcNow.AddDays(30),
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
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

        public int ListManagedDocumentsCalls { get; private set; }

        public int ListForIndexingCalls { get; private set; }

        public string? LastListPathPrefix { get; private set; }

        public Task<IReadOnlyList<ManagedDocumentSummary>> ListManagedDocumentsAsync(
            string customerId,
            string brainId,
            string? pathPrefix = null,
            string? excludePathPrefix = null,
            int limit = 200,
            CancellationToken cancellationToken = default)
        {
            ListManagedDocumentsCalls++;
            LastListPathPrefix = pathPrefix;
            return Task.FromResult<IReadOnlyList<ManagedDocumentSummary>>(_documents.Values
                .Where(document => string.Equals(document.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(document.BrainId, brainId, StringComparison.OrdinalIgnoreCase)
                    && MatchesPathPrefix(document.CanonicalPath, pathPrefix)
                    && !HasExcludedPathPrefix(document.CanonicalPath, excludePathPrefix)
                    && !document.IsDeleted)
                .Take(limit)
                .Select(document => new ManagedDocumentSummary(
                    document.ManagedDocumentId,
                    document.BrainId,
                    document.CustomerId,
                    document.Title,
                    document.Slug,
                    document.CanonicalPath,
                    document.Status,
                    document.WordCount,
                    document.CreatedAt,
                    document.UpdatedAt))
                .ToList());
        }

        public Task<int> CountActiveManagedDocumentsAsync(string customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(_documents.Values.Count(document =>
                string.Equals(document.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
                && !document.IsDeleted));

        public Task<IReadOnlyList<ManagedDocumentDetail>> ListManagedDocumentsForIndexingAsync(string customerId, string brainId, CancellationToken cancellationToken = default)
        {
            ListForIndexingCalls++;
            return Task.FromResult<IReadOnlyList<ManagedDocumentDetail>>(_documents.Values
                .Where(document => string.Equals(document.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(document.BrainId, brainId, StringComparison.OrdinalIgnoreCase)
                    && !document.IsDeleted)
                .ToList());
        }

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
            var activeDocuments = _documents.Values.Count(document =>
                string.Equals(document.CustomerId, request.CustomerId, StringComparison.OrdinalIgnoreCase)
                && !document.IsDeleted);
            if (request.MaxActiveDocuments is int maxActiveDocuments && activeDocuments >= maxActiveDocuments)
            {
                throw new ManagedDocumentQuotaExceededException(
                    request.QuotaExceededMessage
                    ?? $"Document limit reached for customer '{request.CustomerId}'.");
            }

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

        private static bool MatchesPathPrefix(string canonicalPath, string? pathPrefix)
        {
            if (string.IsNullOrWhiteSpace(pathPrefix))
            {
                return true;
            }

            var normalizedPrefix = pathPrefix.Trim().Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(normalizedPrefix))
            {
                return true;
            }

            return canonicalPath.StartsWith(normalizedPrefix + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasExcludedPathPrefix(string canonicalPath, string? pathPrefix)
            => !string.IsNullOrWhiteSpace(pathPrefix) && MatchesPathPrefix(canonicalPath, pathPrefix);
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
