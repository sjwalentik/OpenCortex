using OpenCortex.Core.Query;

namespace OpenCortex.McpServer.Tests;

public sealed class OqlParserTests
{
    [Fact]
    public void Parse_RequiresBrainClause()
    {
        var parser = new OqlParser();

        var error = Assert.Throws<InvalidOperationException>(() => parser.Parse("SEARCH \"hello\""));

        Assert.Contains("FROM brain", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ParsesCompoundWhereFilters()
    {
        const string oql = """
            FROM brain("sample-team")
            SEARCH "plan"
            WHERE tag = "roadmap" AND type = "plan" AND path = "docs/plan.md"
            LIMIT 5
            """;

        var query = new OqlParser().Parse(oql);

        Assert.Equal(3, query.Filters.Count);
        Assert.Collection(
            query.Filters,
            filter =>
            {
                Assert.Equal("tag", filter.Field);
                Assert.Equal("=", filter.Operator);
                Assert.Equal("roadmap", filter.Value);
            },
            filter =>
            {
                Assert.Equal("type", filter.Field);
                Assert.Equal("=", filter.Operator);
                Assert.Equal("plan", filter.Value);
            },
            filter =>
            {
                Assert.Equal("path", filter.Field);
                Assert.Equal("=", filter.Operator);
                Assert.Equal("docs/plan.md", filter.Value);
            });
    }
}
