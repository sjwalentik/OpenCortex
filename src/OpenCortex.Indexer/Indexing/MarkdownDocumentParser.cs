namespace OpenCortex.Indexer.Indexing;

public sealed class MarkdownDocumentParser
{
    private readonly MarkdownFrontmatterParser _frontmatterParser = new();
    private readonly WikiLinkExtractor _wikiLinkExtractor = new();
    private readonly MarkdownChunker _chunker = new();

    public ParsedMarkdownDocument Parse(string markdown)
    {
        var (frontmatter, body) = _frontmatterParser.Parse(markdown);
        var wikiLinks = _wikiLinkExtractor.Extract(body);
        var chunks = _chunker.Chunk(body);

        return new ParsedMarkdownDocument(frontmatter, body, wikiLinks, chunks);
    }
}
