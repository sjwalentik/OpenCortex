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

    private sealed class FakeDocumentQueryStore : IDocumentQueryStore
    {
        public OqlQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<RetrievalResultRecord>> SearchAsync(OqlQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<RetrievalResultRecord>>(
            [
                new RetrievalResultRecord("doc-1", query.BrainId, "docs/plan.md", "Plan", "chunk-1", "snippet", 1.0, "title match"),
            ]);
        }
    }
}
