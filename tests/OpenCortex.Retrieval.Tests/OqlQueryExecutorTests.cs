using OpenCortex.Core.Persistence;
using OpenCortex.Core.Query;
using OpenCortex.Retrieval.Execution;

namespace OpenCortex.Retrieval.Tests;

public sealed class OqlQueryExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsPlanAndResults()
    {
        var store = new FakeDocumentQueryStore();
        var executor = new OqlQueryExecutor(store);

        const string oql = """
            FROM brain("sample-team")
            SEARCH "retention"
            LIMIT 3
            """;

        var result = await executor.ExecuteAsync(oql);

        Assert.Equal("sample-team", result.Plan.BrainId);
        Assert.Single(result.Results);
        Assert.Equal("doc-1", result.Results[0].DocumentId);
        Assert.Equal("sample-team", store.LastQuery?.BrainId);
        Assert.Equal("retention", store.LastQuery?.SearchText);
    }

    [Fact]
    public async Task ExecuteAsync_PassesStructuredFiltersToStore()
    {
        var store = new FakeDocumentQueryStore();
        var executor = new OqlQueryExecutor(store);

        const string oql = """
            FROM brain("sample-team")
            WHERE tag = "roadmap" AND type = "plan"
            LIMIT 3
            """;

        await executor.ExecuteAsync(oql);

        Assert.Equal(2, store.LastQuery?.Filters.Count);
        Assert.Contains(store.LastQuery!.Filters, filter => filter.Field == "tag" && filter.Value == "roadmap");
        Assert.Contains(store.LastQuery!.Filters, filter => filter.Field == "type" && filter.Value == "plan");
    }

    [Fact]
    public async Task ExecuteAsync_ResultsIncludeScoreBreakdown()
    {
        var store = new FakeDocumentQueryStore();
        var executor = new OqlQueryExecutor(store);

        const string oql = """
            FROM brain("sample-team")
            SEARCH "retention"
            RANK hybrid
            LIMIT 3
            """;

        var result = await executor.ExecuteAsync(oql);

        Assert.Single(result.Results);
        var first = result.Results[0];

        // Breakdown values should be present and non-negative.
        Assert.NotNull(first.Breakdown);
        Assert.True(first.Breakdown.KeywordScore >= 0, "Keyword score should be non-negative.");
        Assert.True(first.Breakdown.SemanticScore >= 0, "Semantic score should be non-negative.");
        Assert.True(first.Breakdown.GraphScore >= 0, "Graph score should be non-negative.");
    }

    [Fact]
    public async Task ExecuteAsync_ReasonStringReflectsSignals()
    {
        // A result with a title match keyword score should surface that in the reason.
        var store = new FakeDocumentQueryStore(
            keywordScore: 2.0,
            semanticScore: 0.85,
            graphScore: 0.15,
            reason: "title match (2.00) + semantic similarity (0.85) + graph boost ×1 (0.15)");

        var executor = new OqlQueryExecutor(store);

        const string oql = """
            FROM brain("sample-team")
            SEARCH "retention"
            RANK hybrid
            LIMIT 3
            """;

        var result = await executor.ExecuteAsync(oql);

        var first = result.Results[0];
        Assert.Contains("title match", first.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("semantic", first.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("graph", first.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ParsesSingleLineOql()
    {
        // Agents send OQL as a single line; the parser must handle this correctly
        // or SearchText/RankMode are lost and semantic scoring is silently skipped.
        var store = new FakeDocumentQueryStore();
        var executor = new OqlQueryExecutor(store);

        const string oql = "FROM brain(\"sample-team\") SEARCH \"Pixel\" RANK hybrid LIMIT 10";

        var result = await executor.ExecuteAsync(oql);

        Assert.Equal("sample-team", store.LastQuery?.BrainId);
        Assert.Equal("Pixel", store.LastQuery?.SearchText);
        Assert.Equal("hybrid", store.LastQuery?.RankMode);
        Assert.Equal(10, store.LastQuery?.Limit);
    }

    private sealed class FakeDocumentQueryStore : IDocumentQueryStore
    {
        private readonly double _keywordScore;
        private readonly double _semanticScore;
        private readonly double _graphScore;
        private readonly string _reason;

        public OqlQuery? LastQuery { get; private set; }

        public FakeDocumentQueryStore(
            double keywordScore = 2.0,
            double semanticScore = 0.0,
            double graphScore = 0.0,
            string reason = "title match (2.00)")
        {
            _keywordScore = keywordScore;
            _semanticScore = semanticScore;
            _graphScore = graphScore;
            _reason = reason;
        }

        public Task<IReadOnlyList<RetrievalResultRecord>> SearchAsync(OqlQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            var breakdown = new ScoreBreakdown(_keywordScore, _semanticScore, _graphScore);
            return Task.FromResult<IReadOnlyList<RetrievalResultRecord>>(
            [
                new RetrievalResultRecord(
                    "doc-1",
                    query.BrainId,
                    "docs/plan.md",
                    "Plan",
                    "chunk-1",
                    "snippet",
                    _keywordScore + _semanticScore + _graphScore,
                    _reason,
                    breakdown),
            ]);
        }
    }
}
