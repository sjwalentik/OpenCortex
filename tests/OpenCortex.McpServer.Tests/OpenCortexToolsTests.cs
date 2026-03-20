using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCortex.Core.Authoring;
using OpenCortex.Core.Brains;
using OpenCortex.Core.Configuration;
using OpenCortex.Core.Persistence;
using OpenCortex.Indexer.Indexing;
using OpenCortex.McpServer;
using OpenCortex.Orchestration.Memory;
using OpenCortex.Retrieval.Execution;
using OpenCortex.Tools.Memory.Handlers;

namespace OpenCortex.McpServer.Tests;

public sealed class OpenCortexToolsTests
{
    private static OpenCortexTools BuildTools(
        IBrainCatalogStore? catalog = null,
        OqlQueryExecutor? executor = null,
        ISubscriptionStore? subscriptionStore = null,
        IUsageCounterStore? usageCounterStore = null,
        IManagedDocumentStore? managedDocumentStore = null,
        IManagedContentBrainIndexingService? indexingService = null,
        IUserMemoryPreferenceStore? memoryPreferenceStore = null,
        IHttpContextAccessor? httpContextAccessor = null,
        OpenCortexOptions? options = null)
    {
        catalog ??= new StubBrainCatalogStore();
        executor ??= new OqlQueryExecutor(new StubDocumentQueryStore());
        subscriptionStore ??= new StubSubscriptionStore();
        usageCounterStore ??= new StubUsageCounterStore();
        managedDocumentStore ??= new StubManagedDocumentStore();
        indexingService ??= new StubManagedContentBrainIndexingService();
        memoryPreferenceStore ??= new StubUserMemoryPreferenceStore();
        httpContextAccessor ??= BuildHttpContextAccessor();
        options ??= new OpenCortexOptions { Billing = new BillingOptions() };
        var memoryBrainResolver = new MemoryBrainResolver(catalog, memoryPreferenceStore);
        var saveMemoryHandler = new SaveMemoryHandler(managedDocumentStore, memoryBrainResolver, indexingService, subscriptionStore, options);
        var recallMemoriesHandler = new RecallMemoriesHandler(executor, managedDocumentStore, memoryBrainResolver, NullLogger<RecallMemoriesHandler>.Instance);
        var forgetMemoryHandler = new ForgetMemoryHandler(managedDocumentStore, memoryBrainResolver, indexingService);

        return new OpenCortexTools(
            catalog,
            executor,
            subscriptionStore,
            usageCounterStore,
            managedDocumentStore,
            indexingService,
            saveMemoryHandler,
            recallMemoriesHandler,
            forgetMemoryHandler,
            httpContextAccessor,
            options);
    }

    [Fact]
    public async Task ListBrains_ReturnsBrains_ExcludingRetired()
    {
        var catalog = new StubBrainCatalogStore(new Dictionary<string, IReadOnlyList<BrainSummary>>
        {
            ["cus_test"] =
            [
                new BrainSummary("active-brain", "Active Brain", "active-brain", "Filesystem", "active", 2),
                new BrainSummary("retired-brain", "Retired Brain", "retired-brain", "Filesystem", "retired", 1),
            ],
        });

        var result = await BuildTools(catalog: catalog).list_brains(CancellationToken.None);

        Assert.Equal(1, result.Count);
        Assert.Single(result.Brains);
        Assert.Equal("active-brain", result.Brains[0].BrainId);
    }

    [Fact]
    public void ToolManifest_IncludesDocumentReadAndWriteHelpers()
    {
        var manifest = OpenCortexToolManifest.Build();
        var toolNames = manifest.Select(item => item.Name).ToList();

        var getDocument = manifest.Single(item => string.Equals(item.Name, "get_document", StringComparison.Ordinal));

        Assert.Contains(getDocument.Parameters, parameter => string.Equals(parameter.Name, "brain_id", StringComparison.Ordinal));
        Assert.Contains(getDocument.Parameters, parameter => string.Equals(parameter.Name, "document_id", StringComparison.Ordinal));
        Assert.Contains(getDocument.Parameters, parameter => string.Equals(parameter.Name, "canonical_path", StringComparison.Ordinal));
        Assert.DoesNotContain(getDocument.Parameters, parameter => string.Equals(parameter.Name, "cancellationToken", StringComparison.OrdinalIgnoreCase));

        var saveDocument = manifest.Single(item => string.Equals(item.Name, "save_document", StringComparison.Ordinal));

        Assert.Contains(saveDocument.Parameters, parameter => string.Equals(parameter.Name, "brain_id", StringComparison.Ordinal) && parameter.Optional);
        Assert.Contains(saveDocument.Parameters, parameter => string.Equals(parameter.Name, "canonical_path", StringComparison.Ordinal) && !parameter.Optional);
        Assert.Contains(saveDocument.Parameters, parameter => string.Equals(parameter.Name, "content", StringComparison.Ordinal) && !parameter.Optional);

        var deleteDocument = manifest.Single(item => string.Equals(item.Name, "delete_document", StringComparison.Ordinal));

        Assert.Contains(deleteDocument.Parameters, parameter => string.Equals(parameter.Name, "brain_id", StringComparison.Ordinal) && parameter.Optional);
        Assert.Contains(deleteDocument.Parameters, parameter => string.Equals(parameter.Name, "managed_document_id", StringComparison.Ordinal) && parameter.Optional);
        Assert.Contains(deleteDocument.Parameters, parameter => string.Equals(parameter.Name, "canonical_path", StringComparison.Ordinal) && parameter.Optional);
        Assert.True(toolNames.IndexOf("save_document") < toolNames.IndexOf("create_document"));
        Assert.True(toolNames.IndexOf("save_document") < toolNames.IndexOf("update_document"));
        Assert.Contains("Preferred write tool", saveDocument.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prefer save_document", manifest.Single(item => string.Equals(item.Name, "create_document", StringComparison.Ordinal)).Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prefer save_document", manifest.Single(item => string.Equals(item.Name, "update_document", StringComparison.Ordinal)).Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolManifest_IncludesMemoryHelpers()
    {
        var manifest = OpenCortexToolManifest.Build();

        var saveMemory = manifest.Single(item => string.Equals(item.Name, "save_memory", StringComparison.Ordinal));
        var recallMemories = manifest.Single(item => string.Equals(item.Name, "recall_memories", StringComparison.Ordinal));
        var forgetMemory = manifest.Single(item => string.Equals(item.Name, "forget_memory", StringComparison.Ordinal));

        Assert.Contains(saveMemory.Parameters, parameter => string.Equals(parameter.Name, "content", StringComparison.Ordinal));
        Assert.Contains(saveMemory.Parameters, parameter => string.Equals(parameter.Name, "category", StringComparison.Ordinal));
        Assert.Contains(recallMemories.Parameters, parameter => string.Equals(parameter.Name, "query", StringComparison.Ordinal));
        Assert.Contains(forgetMemory.Parameters, parameter => string.Equals(parameter.Name, "memory_path", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryBrain_ReturnsFailure_WhenOqlMissingFromClause()
    {
        var result = await BuildTools().query_brain("SEARCH \"test\"", CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("FROM brain", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QueryBrain_ReturnsFailure_WhenMonthlyQuotaExceeded()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var counterKey = $"mcp.queries.{nowUtc:yyyy-MM}";
        var catalog = new StubBrainCatalogStore(new Dictionary<string, IReadOnlyList<BrainSummary>>
        {
            ["cus_test"] =
            [
                new BrainSummary("my-brain", "My Brain", "my-brain", "Filesystem", "active", 0),
            ],
        });
        var usageCounterStore = new StubUsageCounterStore(new Dictionary<string, long>
        {
            [$"cus_test::{counterKey}"] = 100,
        });

        var result = await BuildTools(catalog: catalog, usageCounterStore: usageCounterStore)
            .query_brain("FROM brain(\"my-brain\") SEARCH \"quota\"", CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("Monthly MCP query limit reached", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QueryBrain_SanitizesNonFiniteScoresBeforeSerialization()
    {
        var catalog = new StubBrainCatalogStore(new Dictionary<string, IReadOnlyList<BrainSummary>>
        {
            ["cus_test"] =
            [
                new BrainSummary("my-brain", "My Brain", "my-brain", "Filesystem", "active", 0),
            ],
        });
        var executor = new OqlQueryExecutor(new StubDocumentQueryStore(
            new RetrievalResultRecord(
                "doc-1",
                "my-brain",
                "docs/plan.md",
                "Plan",
                "chunk-1",
                "snippet",
                double.PositiveInfinity,
                "semantic similarity",
                new ScoreBreakdown(2.0, double.PositiveInfinity, double.NegativeInfinity))));

        var result = await BuildTools(
                catalog: catalog,
                executor: executor,
                subscriptionStore: new StubSubscriptionStore(planId: "pro"))
            .query_brain("FROM brain(\"my-brain\") SEARCH \"quota\"", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(0.0, result.MaxScore);
        Assert.Equal(0.0, result.MinScore);
        Assert.Single(result.Results);
        Assert.Equal(0.0, result.Results[0].Score);
        Assert.Equal(2.0, result.Results[0].Breakdown.Keyword);
        Assert.Equal(0.0, result.Results[0].Breakdown.Semantic);
        Assert.Equal(0.0, result.Results[0].Breakdown.Graph);
        Assert.Null(Record.Exception(() => JsonSerializer.Serialize(result)));
    }

    [Fact]
    public async Task GetBrain_ReturnsBrainDetail_WhenFound()
    {
        var detail = new BrainDetail(
            "my-brain", "My Brain", "my-brain", "Filesystem", "active",
            "A test brain", "cus_test",
            [new SourceRootSummary("root-1", "my-brain", "knowledge/canonical", "local", true, ["**/*.md"], [], "scheduled", true)]);
        var catalog = new StubBrainCatalogStore(
            new Dictionary<string, IReadOnlyList<BrainSummary>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, BrainDetail>
            {
                ["my-brain"] = detail,
            });

        var result = await BuildTools(catalog: catalog).get_brain("my-brain", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Brain);
        Assert.Equal("knowledge/canonical", result.Brain!.SourceRoots[0].Path);
    }

    [Fact]
    public async Task GetDocument_ReturnsManagedDocumentByDocumentId_WhenFound()
    {
        var managedDocumentStore = new StubManagedDocumentStore();
        var created = await managedDocumentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest(
                "brain-write",
                "cus_test",
                "Pixel",
                "identity/pixel",
                "# Pixel\n\nFull profile.",
                new Dictionary<string, string> { ["type"] = "identity" },
                "published",
                "user_test"),
            CancellationToken.None);

        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            managedDocumentStore: managedDocumentStore)
            .get_document("brain-write", created.ManagedDocumentId, null, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Document);
        Assert.Equal(created.ManagedDocumentId, result.Document!.ManagedDocumentId);
        Assert.Equal("# Pixel\n\nFull profile.", result.Document.Content);
        Assert.Equal("identity/pixel.md", result.Document.CanonicalPath);
    }

    [Fact]
    public async Task GetDocument_ReturnsManagedDocumentByCanonicalPath_WhenFound()
    {
        var managedDocumentStore = new StubManagedDocumentStore();
        await managedDocumentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest(
                "brain-write",
                "cus_test",
                "Pixel",
                "identity/pixel",
                "# Pixel\n\nFull profile.",
                new Dictionary<string, string> { ["type"] = "identity" },
                "published",
                "user_test"),
            CancellationToken.None);

        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            managedDocumentStore: managedDocumentStore)
            .get_document("brain-write", null, "identity/pixel.md", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Document);
        Assert.Equal("identity/pixel.md", result.Document!.CanonicalPath);
        Assert.Equal("# Pixel\n\nFull profile.", result.Document.Content);
    }

    [Fact]
    public async Task GetDocument_ReturnsFailure_WhenIdentifierIsMissing()
    {
        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"))
            .get_document("brain-write", null, null, CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("document_id or canonical_path", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDocument_ReturnsStructuredFailure_WhenStoreThrows()
    {
        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            managedDocumentStore: new ThrowingManagedDocumentStore())
            .get_document("brain-write", "mdoc_broken", null, CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("Document retrieval failed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("boom", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDocument_ReturnsFailure_WhenBrainIsNotManagedContent()
    {
        var catalog = new StubBrainCatalogStore(new Dictionary<string, IReadOnlyList<BrainSummary>>
        {
            ["cus_test"] =
            [
                new BrainSummary("brain-read", "Read Brain", "brain-read", "Filesystem", "active", 0),
            ],
        });

        var result = await BuildTools(
            catalog: catalog,
            subscriptionStore: new StubSubscriptionStore(planId: "pro"))
            .get_document("brain-read", "doc-1", null, CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("managed-content", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateDocument_ReturnsFailure_WhenTokenLacksWriteScope()
    {
        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            httpContextAccessor: BuildHttpContextAccessor(scopes: "mcp:read"))
            .create_document("brain-write", "Draft", "Hello world", null, null, "draft", CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("mcp:write", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateDocument_ReturnsFailure_WhenPlanDisallowsWrite()
    {
        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "free"),
            httpContextAccessor: BuildHttpContextAccessor("cus_test", "mcp:read", "mcp:write"))
            .create_document("brain-write", "Draft", "Hello world", null, null, "draft", CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("does not allow MCP write tools", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateDocument_CreatesManagedDocumentAndReturnsIndexRun()
    {
        var managedDocumentStore = new StubManagedDocumentStore();
        var usageCounterStore = new StubUsageCounterStore();
        var indexingService = new StubManagedContentBrainIndexingService();

        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            usageCounterStore: usageCounterStore,
            managedDocumentStore: managedDocumentStore,
            indexingService: indexingService,
            httpContextAccessor: BuildHttpContextAccessor("cus_test", "mcp:read", "mcp:write"))
            .create_document(
                "brain-write",
                "Launch Notes",
                "# Launch\nReady.",
                "launch-notes",
                new Dictionary<string, string> { ["category"] = "ops" },
                "published",
                CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Document);
        Assert.NotNull(result.IndexRun);
        Assert.Equal("published", result.Document!.Status);
        Assert.Equal("mcp-document-create", result.IndexRun!.TriggerType);
        Assert.Equal(1, managedDocumentStore.CountActive("cus_test"));
        Assert.Equal(1, usageCounterStore.GetValue("cus_test", "documents.active"));
        Assert.Single(indexingService.Calls);
    }

    [Fact]
    public async Task SaveDocument_CreatesManagedDocumentByCanonicalPath_AndInfersSingleBrain()
    {
        var managedDocumentStore = new StubManagedDocumentStore();
        var indexingService = new StubManagedContentBrainIndexingService();
        var usageCounterStore = new StubUsageCounterStore();

        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            usageCounterStore: usageCounterStore,
            managedDocumentStore: managedDocumentStore,
            indexingService: indexingService,
            httpContextAccessor: BuildHttpContextAccessor("cus_test", "mcp:read", "mcp:write"))
            .save_document(
                "projects/OpenCortex/frontend-portal-direction.md",
                "# Direction\n\nReact + TypeScript",
                null,
                null,
                new Dictionary<string, string> { ["project"] = "OpenCortex" },
                "published",
                CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("created", result.Operation);
        Assert.NotNull(result.Document);
        Assert.Equal("projects/opencortex/frontend-portal-direction.md", result.Document!.CanonicalPath);
        Assert.Equal("Frontend Portal Direction", result.Document.Title);
        Assert.Equal("published", result.Document.Status);
        Assert.Equal("mcp-document-create", result.IndexRun!.TriggerType);
        Assert.Equal(1, usageCounterStore.GetValue("cus_test", "documents.active"));
        Assert.Single(indexingService.Calls);
    }

    [Fact]
    public async Task SaveDocument_UpdatesManagedDocumentByCanonicalPath_WhenDocumentAlreadyExists()
    {
        var managedDocumentStore = new StubManagedDocumentStore();
        var indexingService = new StubManagedContentBrainIndexingService();
        var existing = await managedDocumentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest(
                "brain-write",
                "cus_test",
                "OpenCortex Frontend Direction",
                "projects/opencortex/frontend-portal-direction",
                "old content",
                new Dictionary<string, string>(),
                "draft",
                "user_test"),
            CancellationToken.None);

        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            managedDocumentStore: managedDocumentStore,
            indexingService: indexingService,
            httpContextAccessor: BuildHttpContextAccessor("cus_test", "mcp:read", "mcp:write"))
            .save_document(
                "projects/OpenCortex/frontend-portal-direction.md",
                "new content",
                "brain-write",
                "OpenCortex Frontend Direction",
                null,
                "published",
                CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("updated", result.Operation);
        Assert.NotNull(result.Document);
        Assert.Equal(existing.ManagedDocumentId, result.Document!.ManagedDocumentId);
        Assert.Equal("new content", result.Document.Content);
        Assert.Equal("published", result.Document.Status);
        Assert.Equal("mcp-document-update", result.IndexRun!.TriggerType);
        Assert.Single(indexingService.Calls);
    }

    [Fact]
    public async Task SaveDocument_ReturnsFailure_WhenBrainMustBeDisambiguated()
    {
        var catalog = new StubBrainCatalogStore(new Dictionary<string, IReadOnlyList<BrainSummary>>
        {
            ["cus_test"] =
            [
                new BrainSummary("brain-one", "Brain One", "brain-one", "managed-content", "active", 0),
                new BrainSummary("brain-two", "Brain Two", "brain-two", "managed-content", "active", 0),
            ],
        });

        var result = await BuildTools(
            catalog: catalog,
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            httpContextAccessor: BuildHttpContextAccessor("cus_test", "mcp:read", "mcp:write"))
            .save_document(
                "projects/OpenCortex/frontend-portal-direction.md",
                "content",
                null,
                null,
                null,
                "draft",
                CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("brain_id is required", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteDocument_DeletesManagedDocumentAndReturnsIndexRun()
    {
        var managedDocumentStore = new StubManagedDocumentStore();
        var indexingService = new StubManagedContentBrainIndexingService();
        var usageCounterStore = new StubUsageCounterStore();
        var created = await managedDocumentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest(
                "brain-write",
                "cus_test",
                "Doc To Delete",
                "doc-to-delete",
                "body",
                new Dictionary<string, string>(),
                "draft",
                "user_test"),
            CancellationToken.None);

        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            usageCounterStore: usageCounterStore,
            managedDocumentStore: managedDocumentStore,
            indexingService: indexingService,
            httpContextAccessor: BuildHttpContextAccessor("cus_test", "mcp:read", "mcp:write"))
            .delete_document("brain-write", created.ManagedDocumentId, null, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(created.ManagedDocumentId, result.ManagedDocumentId);
        Assert.Equal("mcp-document-delete", result.IndexRun!.TriggerType);
        Assert.Equal(0, managedDocumentStore.CountActive("cus_test"));
        Assert.Equal(0, usageCounterStore.GetValue("cus_test", "documents.active"));
    }

    [Fact]
    public async Task DeleteDocument_DeletesManagedDocumentByCanonicalPath_AndInfersSingleBrain()
    {
        var managedDocumentStore = new StubManagedDocumentStore();
        var indexingService = new StubManagedContentBrainIndexingService();
        var usageCounterStore = new StubUsageCounterStore();
        var created = await managedDocumentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest(
                "brain-write",
                "cus_test",
                "Frontend Portal Direction",
                "projects/opencortex/frontend-portal-direction",
                "body",
                new Dictionary<string, string>(),
                "published",
                "user_test"),
            CancellationToken.None);

        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            usageCounterStore: usageCounterStore,
            managedDocumentStore: managedDocumentStore,
            indexingService: indexingService,
            httpContextAccessor: BuildHttpContextAccessor("cus_test", "mcp:read", "mcp:write"))
            .delete_document(null, null, "projects/OpenCortex/frontend-portal-direction.md", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(created.ManagedDocumentId, result.ManagedDocumentId);
        Assert.Equal("mcp-document-delete", result.IndexRun!.TriggerType);
        Assert.Equal(0, managedDocumentStore.CountActive("cus_test"));
        Assert.Equal(0, usageCounterStore.GetValue("cus_test", "documents.active"));
    }

    [Fact]
    public async Task DeleteDocument_ReturnsFailure_WhenIdentifierIsMissing()
    {
        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            httpContextAccessor: BuildHttpContextAccessor("cus_test", "mcp:read", "mcp:write"))
            .delete_document("brain-write", null, null, CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("managed_document_id or canonical_path", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteDocument_ReturnsFailure_WhenBrainMustBeDisambiguated()
    {
        var catalog = new StubBrainCatalogStore(new Dictionary<string, IReadOnlyList<BrainSummary>>
        {
            ["cus_test"] =
            [
                new BrainSummary("brain-one", "Brain One", "brain-one", "managed-content", "active", 0),
                new BrainSummary("brain-two", "Brain Two", "brain-two", "managed-content", "active", 0),
            ],
        });

        var result = await BuildTools(
            catalog: catalog,
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            httpContextAccessor: BuildHttpContextAccessor("cus_test", "mcp:read", "mcp:write"))
            .delete_document(null, null, "projects/OpenCortex/frontend-portal-direction.md", CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("brain_id is required", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReindexBrain_ReturnsFailure_WhenBrainNotManagedContent()
    {
        var catalog = new StubBrainCatalogStore(new Dictionary<string, IReadOnlyList<BrainSummary>>
        {
            ["cus_test"] =
            [
                new BrainSummary("brain-read", "Read Brain", "brain-read", "Filesystem", "active", 0),
            ],
        });

        var result = await BuildTools(
            catalog: catalog,
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            httpContextAccessor: BuildHttpContextAccessor("cus_test", "mcp:read", "mcp:write"))
            .reindex_brain("brain-read", CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("managed-content", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveMemory_CreatesManagedMemoryDocumentAndReindexes()
    {
        var managedDocumentStore = new StubManagedDocumentStore();
        var indexingService = new StubManagedContentBrainIndexingService();

        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            managedDocumentStore: managedDocumentStore,
            indexingService: indexingService,
            httpContextAccessor: BuildHttpContextAccessor("cus_test", "mcp:read", "mcp:write"))
            .save_memory(
                "The user prefers concise architecture summaries.",
                "preference",
                "high",
                ["user", "style"],
                CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.MemoryPath);
        Assert.StartsWith("memories/preference/", result.MemoryPath!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("brain-write", result.BrainId);
        Assert.Equal("preference", result.Category);
        Assert.Equal("high", result.Confidence);
        Assert.Contains("style", result.Tags);
        Assert.Single(indexingService.Calls);
        Assert.Equal("memory-save", indexingService.Calls[0].TriggerType);
        var saved = Assert.Single(managedDocumentStore.Documents.Where(document =>
            string.Equals(document.CanonicalPath, result.MemoryPath, StringComparison.OrdinalIgnoreCase)));
        Assert.False(saved.Frontmatter.ContainsKey("source_conversation"));
    }

    [Fact]
    public async Task SaveMemory_ReturnsFailure_WhenTokenLacksWriteScope()
    {
        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            httpContextAccessor: BuildHttpContextAccessor(scopes: "mcp:read"))
            .save_memory(
                "The user prefers concise architecture summaries.",
                "preference",
                "high",
                ["user", "style"],
                CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("mcp:write", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveMemory_ReturnsFailure_WhenWorkspaceIsAtDocumentLimit()
    {
        var managedDocumentStore = new StubManagedDocumentStore();
        for (var index = 0; index < 10; index++)
        {
            await managedDocumentStore.CreateManagedDocumentAsync(
                new ManagedDocumentCreateRequest(
                    "brain-write",
                    "cus_test",
                    $"Existing {index}",
                    $"existing/{index}",
                    "existing content",
                    new Dictionary<string, string>(),
                    "published",
                    "user_test"),
                CancellationToken.None);
        }

        var options = new OpenCortexOptions
        {
            Billing = new BillingOptions
            {
                Plans = new Dictionary<string, PlanEntitlements>(StringComparer.OrdinalIgnoreCase)
                {
                    ["free"] = new() { MaxDocuments = 10, MaxBrains = 1, McpQueriesPerMonth = 100, McpWrite = false },
                    ["pro"] = new() { MaxDocuments = 10, MaxBrains = 3, McpQueriesPerMonth = -1, McpWrite = true },
                    ["teams"] = new() { MaxDocuments = 2000, MaxBrains = 10, McpQueriesPerMonth = -1, McpWrite = true },
                }
            }
        };

        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            managedDocumentStore: managedDocumentStore,
            httpContextAccessor: BuildHttpContextAccessor("cus_test", "mcp:read", "mcp:write"),
            options: options)
            .save_memory(
                "The user prefers concise architecture summaries.",
                "preference",
                "high",
                ["user", "style"],
                CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Document limit reached", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.QuotaExceeded);
        Assert.NotNull(result.Suggestion);
        Assert.Contains("Memories page", result.Suggestion, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, managedDocumentStore.Documents.Count);
    }

    [Fact]
    public async Task RecallMemories_ReturnsScopedMemories()
    {
        var queryStore = new StubDocumentQueryStore(
            new RetrievalResultRecord(
                "memory-doc-1",
                "brain-write",
                "memories/decision/abc123.md",
                "[decision] Use OQL path prefix on day one",
                null,
                "Use OQL path prefix on day one for memory recall.",
                0.91,
                "semantic",
                new ScoreBreakdown(0.0, 0.91, 0.0)));
        var managedDocumentStore = new StubManagedDocumentStore();
        await managedDocumentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest(
                "brain-write",
                "cus_test",
                "[decision] Use OQL path prefix on day one",
                "memories/decision/abc123",
                "Use OQL path prefix on day one for memory recall.",
                new Dictionary<string, string> { ["category"] = "decision", ["confidence"] = "high", ["tags"] = "roadmap,p1" },
                "published",
                "user_test"),
            CancellationToken.None);

        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            executor: new OqlQueryExecutor(queryStore),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            managedDocumentStore: managedDocumentStore)
            .recall_memories("day one memory recall", "decision", 3, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(1, result.Count);
        Assert.Single(result.Memories);
        Assert.Equal("memories/decision/abc123.md", result.Memories[0].Path);
        Assert.Equal("decision", result.Memories[0].Category);
        Assert.Equal("high", result.Memories[0].Confidence);
    }

    [Fact]
    public async Task ForgetMemory_DeletesManagedMemoryDocumentAndReindexes()
    {
        var managedDocumentStore = new StubManagedDocumentStore();
        var indexingService = new StubManagedContentBrainIndexingService();
        var existing = await managedDocumentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest(
                "brain-write",
                "cus_test",
                "[fact] Ollama is the default local provider",
                "memories/fact/ollama-default",
                "Ollama is the default local provider.",
                new Dictionary<string, string> { ["category"] = "fact" },
                "published",
                "user_test"),
            CancellationToken.None);

        var result = await BuildTools(
            catalog: BuildManagedContentCatalog(),
            subscriptionStore: new StubSubscriptionStore(planId: "pro"),
            managedDocumentStore: managedDocumentStore,
            indexingService: indexingService,
            httpContextAccessor: BuildHttpContextAccessor("cus_test", "mcp:read", "mcp:write"))
            .forget_memory(
                "memories/fact/ollama-default.md",
                "Superseded by a newer routing rule.",
                CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal("memories/fact/ollama-default.md", result.Forgotten);
        Assert.Contains(managedDocumentStore.SoftDeletedManagedDocumentIds, id => id == existing.ManagedDocumentId);
        Assert.Single(indexingService.Calls);
        Assert.Equal("memory-forget", indexingService.Calls[0].TriggerType);
    }

    private static StubBrainCatalogStore BuildManagedContentCatalog() =>
        new(new Dictionary<string, IReadOnlyList<BrainSummary>>
        {
            ["cus_test"] =
            [
                new BrainSummary("brain-write", "Managed Brain", "brain-write", "managed-content", "active", 0),
            ],
        });

    private static IHttpContextAccessor BuildHttpContextAccessor(string customerId = "cus_test", params string[] scopes)
    {
        if (scopes.Length == 0)
        {
            scopes = ["mcp:read"];
        }

        var context = new DefaultHttpContext();
        context.SetMcpTokenContext(new McpTokenContext("tok_test", "user_test", customerId, scopes, "oct_test"));
        return new HttpContextAccessor { HttpContext = context };
    }
}

internal sealed class StubBrainCatalogStore : IBrainCatalogStore
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<BrainSummary>> _summariesByCustomer;
    private readonly Dictionary<string, BrainDetail> _detailsByBrainId = new(StringComparer.OrdinalIgnoreCase);

    public StubBrainCatalogStore()
        : this(new Dictionary<string, IReadOnlyList<BrainSummary>>(StringComparer.OrdinalIgnoreCase))
    {
    }

    public StubBrainCatalogStore(IReadOnlyDictionary<string, IReadOnlyList<BrainSummary>> summariesByCustomer, IReadOnlyDictionary<string, BrainDetail>? details = null)
    {
        _summariesByCustomer = summariesByCustomer;

        foreach (var (customerId, summaries) in summariesByCustomer)
        {
            foreach (var summary in summaries)
            {
                _detailsByBrainId[summary.BrainId] = new BrainDetail(
                    summary.BrainId,
                    summary.Name,
                    summary.Slug,
                    summary.Mode,
                    summary.Status,
                    null,
                    customerId,
                    []);
            }
        }

        if (details is not null)
        {
            foreach (var item in details)
            {
                _detailsByBrainId[item.Key] = item.Value;
            }
        }
    }

    public Task<IReadOnlyList<BrainSummary>> ListBrainsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<BrainSummary>>(_summariesByCustomer.Values.SelectMany(items => items).ToList());

    public Task<IReadOnlyList<BrainSummary>> ListBrainsByCustomerAsync(string customerId, CancellationToken cancellationToken = default)
        => Task.FromResult(_summariesByCustomer.TryGetValue(customerId, out var summaries) ? summaries : Array.Empty<BrainSummary>());

    public Task<BrainDetail?> GetBrainAsync(string brainId, CancellationToken cancellationToken = default)
        => Task.FromResult(_detailsByBrainId.TryGetValue(brainId, out var detail) ? detail : null);

    public Task<BrainDetail?> GetBrainByCustomerAsync(string customerId, string brainId, CancellationToken cancellationToken = default)
    {
        if (_detailsByBrainId.TryGetValue(brainId, out var detail)
            && string.Equals(detail.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BrainDetail?>(detail);
        }

        return Task.FromResult<BrainDetail?>(null);
    }

    public Task<BrainDetail> CreateBrainAsync(BrainDefinition brain, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<BrainDetail?> UpdateBrainAsync(string brainId, string name, string slug, string mode, string status, string? description, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<bool> RetireBrainAsync(string brainId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task UpsertBrainsAsync(IReadOnlyList<BrainDefinition> brains, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<SourceRootSummary> AddSourceRootAsync(string brainId, SourceRootDefinition sourceRoot, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<SourceRootSummary?> UpdateSourceRootAsync(string brainId, string sourceRootId, string path, string pathType, bool isWritable, string[] includePatterns, string[] excludePatterns, string watchMode, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<bool> RemoveSourceRootAsync(string brainId, string sourceRootId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

internal sealed class StubDocumentQueryStore : IDocumentQueryStore
{
    private readonly IReadOnlyList<RetrievalResultRecord> _results;

    public StubDocumentQueryStore(params RetrievalResultRecord[] results)
    {
        _results = results;
    }

    public Task<IReadOnlyList<RetrievalResultRecord>> SearchAsync(
        OpenCortex.Core.Query.OqlQuery query,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_results);
}

internal sealed class StubSubscriptionStore : ISubscriptionStore
{
    private readonly string _planId;
    private readonly string _status;

    public StubSubscriptionStore(string planId = "free", string status = "active")
    {
        _planId = planId;
        _status = status;
    }

    public Task<SubscriptionRecord> EnsureFreeSubscriptionAsync(string customerId, CancellationToken cancellationToken = default)
        => Task.FromResult(BuildSubscription(customerId, "free", "active"));

    public Task<SubscriptionRecord?> GetSubscriptionAsync(string customerId, CancellationToken cancellationToken = default)
        => Task.FromResult<SubscriptionRecord?>(BuildSubscription(customerId, _planId, _status));

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

internal sealed class StubUsageCounterStore : IUsageCounterStore
{
    private readonly Dictionary<string, long> _values;

    public StubUsageCounterStore(Dictionary<string, long>? values = null)
    {
        _values = values ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    }

    public Task<UsageCounterRecord?> GetCounterAsync(string customerId, string counterKey, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_values.TryGetValue(BuildKey(customerId, counterKey), out var value)
            ? new UsageCounterRecord(customerId, counterKey, value, DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow)
            : null);
    }

    public Task<UsageCounterRecord> IncrementCounterAsync(UsageCounterIncrementRequest request, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(request.CustomerId, request.CounterKey);
        _values.TryGetValue(key, out var current);
        current += request.Delta;
        _values[key] = current;
        return Task.FromResult(new UsageCounterRecord(request.CustomerId, request.CounterKey, current, request.ResetAt, DateTimeOffset.UtcNow));
    }

    public Task<UsageCounterRecord> SetCounterAsync(UsageCounterSetRequest request, CancellationToken cancellationToken = default)
    {
        _values[BuildKey(request.CustomerId, request.CounterKey)] = request.Value;
        return Task.FromResult(new UsageCounterRecord(request.CustomerId, request.CounterKey, request.Value, request.ResetAt, DateTimeOffset.UtcNow));
    }

    public long GetValue(string customerId, string counterKey)
        => _values.TryGetValue(BuildKey(customerId, counterKey), out var value) ? value : 0;

    private static string BuildKey(string customerId, string counterKey) => $"{customerId}::{counterKey}";
}

internal sealed class StubManagedDocumentStore : IManagedDocumentStore
{
    private readonly Dictionary<string, ManagedDocumentDetail> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ManagedDocumentVersionDetail> _versions = [];
    private int _nextId = 1;

    public IReadOnlyList<ManagedDocumentDetail> Documents => _documents.Values.ToList();

    public List<string> SoftDeletedManagedDocumentIds { get; } = [];

    public Task<IReadOnlyList<ManagedDocumentSummary>> ListManagedDocumentsAsync(
        string customerId,
        string brainId,
        string? pathPrefix = null,
        string? excludePathPrefix = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ManagedDocumentSummary>>(_documents.Values
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

    public Task<int> CountActiveManagedDocumentsAsync(string customerId, CancellationToken cancellationToken = default)
        => Task.FromResult(CountActive(customerId));

    public int CountActive(string customerId)
        => _documents.Values.Count(document => string.Equals(document.CustomerId, customerId, StringComparison.OrdinalIgnoreCase) && !document.IsDeleted);

    public Task<IReadOnlyList<ManagedDocumentDetail>> ListManagedDocumentsForIndexingAsync(string customerId, string brainId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ManagedDocumentDetail>>(_documents.Values
            .Where(document => string.Equals(document.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(document.BrainId, brainId, StringComparison.OrdinalIgnoreCase)
                && !document.IsDeleted)
            .ToList());

    public Task<ManagedDocumentDetail?> GetManagedDocumentAsync(string customerId, string brainId, string managedDocumentId, CancellationToken cancellationToken = default)
    {
        if (_documents.TryGetValue(managedDocumentId, out var document)
            && string.Equals(document.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(document.BrainId, brainId, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<ManagedDocumentDetail?>(document);
        }

        return Task.FromResult<ManagedDocumentDetail?>(null);
    }

    public Task<ManagedDocumentDetail?> GetManagedDocumentByCanonicalPathAsync(string customerId, string brainId, string canonicalPath, CancellationToken cancellationToken = default)
        => Task.FromResult<ManagedDocumentDetail?>(_documents.Values.FirstOrDefault(document =>
            string.Equals(document.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(document.BrainId, brainId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(document.CanonicalPath, canonicalPath, StringComparison.OrdinalIgnoreCase)
            && !document.IsDeleted));

    public Task<IReadOnlyList<ManagedDocumentVersionSummary>> ListManagedDocumentVersionsAsync(string customerId, string brainId, string managedDocumentId, int limit = 50, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ManagedDocumentVersionSummary>>(_versions
            .Where(version => string.Equals(version.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(version.BrainId, brainId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(version.ManagedDocumentId, managedDocumentId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(version => version.CreatedAt)
            .Take(limit)
            .Select(version => new ManagedDocumentVersionSummary(
                version.ManagedDocumentVersionId,
                version.ManagedDocumentId,
                version.BrainId,
                version.CustomerId,
                version.Title,
                version.Slug,
                version.CanonicalPath,
                version.Status,
                version.ContentHash,
                version.WordCount,
                version.SnapshotKind,
                version.SnapshotBy,
                version.CreatedAt))
            .ToList());

    public Task<ManagedDocumentVersionDetail?> GetManagedDocumentVersionAsync(string customerId, string brainId, string managedDocumentId, string managedDocumentVersionId, CancellationToken cancellationToken = default)
        => Task.FromResult<ManagedDocumentVersionDetail?>(_versions.FirstOrDefault(version =>
            string.Equals(version.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(version.BrainId, brainId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(version.ManagedDocumentId, managedDocumentId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(version.ManagedDocumentVersionId, managedDocumentVersionId, StringComparison.OrdinalIgnoreCase)));

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
        var managedDocumentId = $"md-{_nextId++:D4}";
        var slug = string.IsNullOrWhiteSpace(request.Slug) ? Slugify(request.Title) : request.Slug!;
        var content = request.Content ?? string.Empty;
        var document = new ManagedDocumentDetail(
            managedDocumentId,
            request.BrainId,
            request.CustomerId,
            request.Title,
            slug,
            ManagedDocumentText.BuildCanonicalPath(slug),
            content,
            new Dictionary<string, string>(request.Frontmatter, StringComparer.OrdinalIgnoreCase),
            $"hash-{managedDocumentId}",
            request.Status,
            CountWords(content),
            request.UserId,
            request.UserId,
            now,
            now,
            false);

        _documents[managedDocumentId] = document;
        AddVersion(document, "created", request.UserId);
        return Task.FromResult(document);
    }

    public Task<ManagedDocumentDetail?> UpdateManagedDocumentAsync(ManagedDocumentUpdateRequest request, CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(request.ManagedDocumentId, out var existing)
            || !string.Equals(existing.CustomerId, request.CustomerId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.BrainId, request.BrainId, StringComparison.OrdinalIgnoreCase)
            || existing.IsDeleted)
        {
            return Task.FromResult<ManagedDocumentDetail?>(null);
        }

        var slug = string.IsNullOrWhiteSpace(request.Slug) ? Slugify(request.Title) : request.Slug!;
        var content = request.Content ?? string.Empty;
        var updated = existing with
        {
            Title = request.Title,
            Slug = slug,
            CanonicalPath = ManagedDocumentText.BuildCanonicalPath(slug),
            Content = content,
            Frontmatter = new Dictionary<string, string>(request.Frontmatter, StringComparer.OrdinalIgnoreCase),
            Status = request.Status,
            WordCount = CountWords(content),
            UpdatedBy = request.UserId,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        _documents[request.ManagedDocumentId] = updated;
        AddVersion(updated, "updated", request.UserId);
        return Task.FromResult<ManagedDocumentDetail?>(updated);
    }

    public Task<bool> SoftDeleteManagedDocumentAsync(string customerId, string brainId, string managedDocumentId, string userId, CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(managedDocumentId, out var existing)
            || !string.Equals(existing.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.BrainId, brainId, StringComparison.OrdinalIgnoreCase)
            || existing.IsDeleted)
        {
            return Task.FromResult(false);
        }

        AddVersion(existing, "deleted", userId);

        _documents[managedDocumentId] = existing with
        {
            IsDeleted = true,
            UpdatedBy = userId,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        SoftDeletedManagedDocumentIds.Add(managedDocumentId);

        return Task.FromResult(true);
    }

    public Task<ManagedDocumentDetail?> RestoreManagedDocumentVersionAsync(string customerId, string brainId, string managedDocumentId, string managedDocumentVersionId, string userId, CancellationToken cancellationToken = default)
    {
        var version = _versions.FirstOrDefault(snapshot =>
            string.Equals(snapshot.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(snapshot.BrainId, brainId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(snapshot.ManagedDocumentId, managedDocumentId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(snapshot.ManagedDocumentVersionId, managedDocumentVersionId, StringComparison.OrdinalIgnoreCase));

        if (version is null)
        {
            return Task.FromResult<ManagedDocumentDetail?>(null);
        }

        var now = DateTimeOffset.UtcNow;
        if (!_documents.TryGetValue(managedDocumentId, out var existing))
        {
            existing = new ManagedDocumentDetail(
                managedDocumentId,
                brainId,
                customerId,
                version.Title,
                version.Slug,
                version.CanonicalPath,
                version.Content,
                new Dictionary<string, string>(version.Frontmatter, StringComparer.OrdinalIgnoreCase),
                version.ContentHash,
                version.Status,
                version.WordCount,
                userId,
                userId,
                now,
                now,
                false);
        }

        var restored = existing with
        {
            Title = version.Title,
            Slug = version.Slug,
            CanonicalPath = version.CanonicalPath,
            Content = version.Content,
            Frontmatter = new Dictionary<string, string>(version.Frontmatter, StringComparer.OrdinalIgnoreCase),
            ContentHash = version.ContentHash,
            Status = version.Status,
            WordCount = version.WordCount,
            UpdatedBy = userId,
            UpdatedAt = now,
            IsDeleted = false,
        };

        _documents[managedDocumentId] = restored;
        AddVersion(restored, "restored", userId);
        return Task.FromResult<ManagedDocumentDetail?>(restored);
    }

    private void AddVersion(ManagedDocumentDetail document, string snapshotKind, string snapshotBy)
    {
        var timestamp = DateTimeOffset.UtcNow;
        _versions.Add(new ManagedDocumentVersionDetail(
            $"mdver-{_versions.Count + 1:D4}",
            document.ManagedDocumentId,
            document.BrainId,
            document.CustomerId,
            document.Title,
            document.Slug,
            document.CanonicalPath,
            document.Content,
            new Dictionary<string, string>(document.Frontmatter, StringComparer.OrdinalIgnoreCase),
            document.ContentHash,
            document.Status,
            document.WordCount,
            snapshotKind,
            snapshotBy,
            timestamp));
    }

    private static int CountWords(string content)
        => string.IsNullOrWhiteSpace(content)
            ? 0
            : content.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string Slugify(string title)
    {
        var chars = title.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        return string.Join('-', new string(chars).Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries));
    }

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

internal sealed class StubUserMemoryPreferenceStore(string? memoryBrainId = null) : IUserMemoryPreferenceStore
{
    public string? MemoryBrainId { get; private set; } = memoryBrainId;

    public Task<string?> GetMemoryBrainIdAsync(string customerId, string userId, CancellationToken cancellationToken = default)
        => Task.FromResult(MemoryBrainId);

    public Task SetMemoryBrainIdAsync(string customerId, string userId, string? memoryBrainId, CancellationToken cancellationToken = default)
    {
        MemoryBrainId = memoryBrainId;
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingManagedDocumentStore : IManagedDocumentStore
{
    public Task<IReadOnlyList<ManagedDocumentSummary>> ListManagedDocumentsAsync(
        string customerId,
        string brainId,
        string? pathPrefix = null,
        string? excludePathPrefix = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<int> CountActiveManagedDocumentsAsync(string customerId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<ManagedDocumentDetail>> ListManagedDocumentsForIndexingAsync(string customerId, string brainId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ManagedDocumentDetail?> GetManagedDocumentAsync(string customerId, string brainId, string managedDocumentId, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("boom");

    public Task<ManagedDocumentDetail?> GetManagedDocumentByCanonicalPathAsync(string customerId, string brainId, string canonicalPath, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("boom");

    public Task<IReadOnlyList<ManagedDocumentVersionSummary>> ListManagedDocumentVersionsAsync(string customerId, string brainId, string managedDocumentId, int limit = 50, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ManagedDocumentVersionDetail?> GetManagedDocumentVersionAsync(string customerId, string brainId, string managedDocumentId, string managedDocumentVersionId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ManagedDocumentDetail> CreateManagedDocumentAsync(ManagedDocumentCreateRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ManagedDocumentDetail?> UpdateManagedDocumentAsync(ManagedDocumentUpdateRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<bool> SoftDeleteManagedDocumentAsync(string customerId, string brainId, string managedDocumentId, string userId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ManagedDocumentDetail?> RestoreManagedDocumentVersionAsync(string customerId, string brainId, string managedDocumentId, string managedDocumentVersionId, string userId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

internal sealed class StubManagedContentBrainIndexingService : IManagedContentBrainIndexingService
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
