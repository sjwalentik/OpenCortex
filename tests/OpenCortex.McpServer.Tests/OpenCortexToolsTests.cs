using OpenCortex.Core.Brains;
using OpenCortex.Core.Persistence;
using OpenCortex.McpServer;
using OpenCortex.Retrieval.Execution;

namespace OpenCortex.McpServer.Tests;

/// <summary>
/// Tests for the MCP tool contracts exposed via <see cref="OpenCortexTools"/>.
/// Uses in-memory stubs to avoid Postgres or embedding dependencies.
/// </summary>
public sealed class OpenCortexToolsTests
{
    // -----------------------------------------------------------------------
    // Helpers / stubs
    // -----------------------------------------------------------------------

    private static OpenCortexTools BuildTools(
        IBrainCatalogStore? catalog = null,
        OqlQueryExecutor? executor = null)
    {
        catalog ??= new StubBrainCatalogStore();
        executor ??= new OqlQueryExecutor(new StubDocumentQueryStore());
        return new OpenCortexTools(catalog, executor);
    }

    // -----------------------------------------------------------------------
    // list_brains
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListBrains_ReturnsBrains_ExcludingRetired()
    {
        var catalog = new StubBrainCatalogStore(
            new BrainSummary("active-brain", "Active Brain", "active-brain", "Filesystem", "active", 2),
            new BrainSummary("retired-brain", "Retired Brain", "retired-brain", "Filesystem", "retired", 1));

        var tools = BuildTools(catalog: catalog);
        var result = await tools.list_brains(CancellationToken.None);

        Assert.Equal(1, result.Count);
        Assert.Single(result.Brains);
        Assert.Equal("active-brain", result.Brains[0].BrainId);
    }

    [Fact]
    public async Task ListBrains_ReturnsEmpty_WhenNoActiveBrains()
    {
        var tools = BuildTools(catalog: new StubBrainCatalogStore());
        var result = await tools.list_brains(CancellationToken.None);

        Assert.Equal(0, result.Count);
        Assert.Empty(result.Brains);
    }

    // -----------------------------------------------------------------------
    // query_brain
    // -----------------------------------------------------------------------

    [Fact]
    public async Task QueryBrain_ReturnsFailure_WhenOqlMissingFromClause()
    {
        var tools = BuildTools();
        var result = await tools.query_brain("SEARCH \"test\"", CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("FROM brain", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.TotalResults);
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task QueryBrain_ReturnsResults_ForValidOql()
    {
        var store = new StubDocumentQueryStore(
            new RetrievalResultRecord(
                "doc-1", "my-brain", "docs/test.md", "Test Doc",
                null, "snippet text", 1.5, "title match (1.50)",
                new ScoreBreakdown(1.5, 0, 0)));

        var executor = new OqlQueryExecutor(store);
        var tools = BuildTools(executor: executor);

        var result = await tools.query_brain(
            """FROM brain("my-brain") SEARCH "test" RANK keyword LIMIT 5""",
            CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(1, result.TotalResults);
        Assert.Single(result.Results);
        Assert.Equal("doc-1", result.Results[0].DocumentId);
        Assert.Equal("Test Doc", result.Results[0].Title);
        Assert.Equal(1.5, result.Results[0].Score);
        Assert.Equal(1.5, result.Results[0].Breakdown.Keyword);
        Assert.Equal(0, result.Results[0].Breakdown.Semantic);
    }

    [Fact]
    public async Task QueryBrain_ReturnsFailure_WhenExecutorThrows()
    {
        var store = new ThrowingDocumentQueryStore();
        var executor = new OqlQueryExecutor(store);
        var tools = BuildTools(executor: executor);

        var result = await tools.query_brain(
            "FROM brain(\"my-brain\") SEARCH \"test\"",
            CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("Query execution failed", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, result.TotalResults);
    }

    [Fact]
    public async Task QueryBrain_ScoresRoundedToFourDecimalPlaces()
    {
        var store = new StubDocumentQueryStore(
            new RetrievalResultRecord(
                "doc-1", "brain", "x.md", "X", null, null,
                1.123456789, "kw",
                new ScoreBreakdown(0.987654321, 0.111111111, 0.024691358)));

        var executor = new OqlQueryExecutor(store);
        var tools = BuildTools(executor: executor);

        var result = await tools.query_brain(
            "FROM brain(\"brain\") SEARCH \"x\"", CancellationToken.None);

        Assert.Null(result.Error);
        var item = result.Results[0];
        Assert.Equal(4, CountDecimalPlaces(item.Score));
        Assert.Equal(4, CountDecimalPlaces(item.Breakdown.Keyword));
    }

    // -----------------------------------------------------------------------
    // get_brain
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetBrain_ReturnsNotFound_WhenBrainDoesNotExist()
    {
        var tools = BuildTools(catalog: new StubBrainCatalogStore());
        var result = await tools.get_brain("missing-brain", CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Null(result.Brain);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetBrain_ReturnsBrainDetail_WhenFound()
    {
        var detail = new BrainDetail(
            "my-brain", "My Brain", "my-brain", "Filesystem", "active",
            "A test brain", null,
            [new SourceRootSummary("root-1", "my-brain", "knowledge/canonical", "local", true, ["**/*.md"], [], "scheduled", true)]);

        var catalog = new StubBrainCatalogStore(detail: detail);
        var tools = BuildTools(catalog: catalog);
        var result = await tools.get_brain("my-brain", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Brain);
        Assert.Equal("my-brain", result.Brain!.BrainId);
        Assert.Equal("My Brain", result.Brain.Name);
        Assert.Single(result.Brain.SourceRoots);
        Assert.Equal("knowledge/canonical", result.Brain.SourceRoots[0].Path);
    }

    [Fact]
    public async Task GetBrain_ReturnsError_WhenBrainIdEmpty()
    {
        var tools = BuildTools();
        var result = await tools.get_brain("", CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Null(result.Brain);
    }

    // -----------------------------------------------------------------------
    // Utility
    // -----------------------------------------------------------------------

    private static int CountDecimalPlaces(double value)
    {
        var s = value.ToString("G");
        var dot = s.IndexOf('.');
        return dot < 0 ? 0 : s.Length - dot - 1;
    }
}

// ---------------------------------------------------------------------------
// Stubs
// ---------------------------------------------------------------------------

internal sealed class StubBrainCatalogStore : IBrainCatalogStore
{
    private readonly IReadOnlyList<BrainSummary> _summaries;
    private readonly BrainDetail? _detail;

    public StubBrainCatalogStore(params BrainSummary[] summaries)
    {
        _summaries = summaries;
        _detail = null;
    }

    public StubBrainCatalogStore(BrainDetail? detail = null)
    {
        _summaries = [];
        _detail = detail;
    }

    public Task<IReadOnlyList<BrainSummary>> ListBrainsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_summaries);

    public Task<BrainDetail?> GetBrainAsync(string brainId, CancellationToken cancellationToken = default)
        => Task.FromResult(_detail?.BrainId == brainId ? _detail : null);

    // Remaining interface methods — not needed for these tests

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

internal sealed class ThrowingDocumentQueryStore : IDocumentQueryStore
{
    public Task<IReadOnlyList<RetrievalResultRecord>> SearchAsync(
        OpenCortex.Core.Query.OqlQuery query,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated store failure");
}
