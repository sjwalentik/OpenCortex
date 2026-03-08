using OpenCortex.Indexer.Indexing;

namespace OpenCortex.Indexer.Tests;

public sealed class MarkdownDocumentParserTests
{
    [Fact]
    public void Parse_ExtractsFrontmatterLinksAndChunks()
    {
        const string markdown = """
            ---
            title: Build Plan
            type: plan
            ---
            # Build Plan
            Review the [[Architecture]] and [[Roadmap|product roadmap]].

            ## Next Steps
            Ship the first indexer slice.
            """;

        var parsed = new MarkdownDocumentParser().Parse(markdown);

        Assert.Equal("Build Plan", parsed.Frontmatter["title"]);
        Assert.Equal("plan", parsed.Frontmatter["type"]);
        Assert.Equal(new[] { "Architecture", "Roadmap" }, parsed.WikiLinks);
        Assert.Equal(2, parsed.Chunks.Count);
        Assert.Equal("Build Plan", parsed.Chunks[0].HeadingPath);
        Assert.Equal("Next Steps", parsed.Chunks[1].HeadingPath);
    }
}
