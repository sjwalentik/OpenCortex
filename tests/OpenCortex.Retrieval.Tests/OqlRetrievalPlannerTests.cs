using OpenCortex.Retrieval.Planning;

namespace OpenCortex.Retrieval.Tests;

public sealed class OqlRetrievalPlannerTests
{
    [Fact]
    public void BuildPlan_ParsesCoreOqlClauses()
    {
        const string oql = """
            FROM brain("sample-team")
            SEARCH "retention strategy"
            WHERE tag = "roadmap"
            RANK hybrid
            LIMIT 5
            """;

        var plan = new OqlRetrievalPlanner().BuildPlan(oql);

        Assert.Equal("sample-team", plan.BrainId);
        Assert.Equal("retention strategy", plan.SearchText);
        Assert.Single(plan.Filters);
        Assert.Equal("tag = \"roadmap\"", plan.Filters[0]);
        Assert.Equal("hybrid", plan.RankMode);
        Assert.Equal(5, plan.Limit);
    }

    [Fact]
    public void BuildPlan_ParsesMultipleFilters()
    {
        const string oql = """
            FROM brain("sample-team")
            WHERE tag = "roadmap" AND type = "plan"
            LIMIT 5
            """;

        var plan = new OqlRetrievalPlanner().BuildPlan(oql);

        Assert.Equal(2, plan.Filters.Count);
        Assert.Contains("tag = \"roadmap\"", plan.Filters);
        Assert.Contains("type = \"plan\"", plan.Filters);
    }

    [Fact]
    public void BuildPlan_PreservesSemanticRankMode()
    {
        const string oql = """
            FROM brain("sample-team")
            SEARCH "architecture"
            RANK semantic
            LIMIT 5
            """;

        var plan = new OqlRetrievalPlanner().BuildPlan(oql);

        Assert.Equal("semantic", plan.RankMode);
        Assert.Contains(plan.Steps, step => step.Contains("semantic retrieval", StringComparison.OrdinalIgnoreCase));
    }
}
