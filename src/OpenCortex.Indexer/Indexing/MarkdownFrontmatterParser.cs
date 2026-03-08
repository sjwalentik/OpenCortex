namespace OpenCortex.Indexer.Indexing;

public sealed class MarkdownFrontmatterParser
{
    public (IReadOnlyDictionary<string, string> Frontmatter, string Body) Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), markdown);
        }

        var closingIndex = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);

        if (closingIndex < 0)
        {
            return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), markdown);
        }

        var frontmatterBlock = normalized[4..closingIndex];
        var body = normalized[(closingIndex + 5)..];
        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in frontmatterBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = line.IndexOf(':');

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (key.Length > 0)
            {
                frontmatter[key] = value;
            }
        }

        return (frontmatter, body);
    }
}
