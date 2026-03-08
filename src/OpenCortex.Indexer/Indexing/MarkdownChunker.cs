namespace OpenCortex.Indexer.Indexing;

public sealed class MarkdownChunker
{
    public IReadOnlyList<MarkdownChunk> Chunk(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var chunks = new List<MarkdownChunk>();
        var currentLines = new List<string>();
        string? currentHeading = null;
        var chunkIndex = 0;

        foreach (var line in lines)
        {
            if (IsHeading(line) && currentLines.Count > 0)
            {
                chunks.Add(CreateChunk(chunkIndex++, currentHeading, currentLines));
                currentLines.Clear();
            }

            if (IsHeading(line))
            {
                currentHeading = line.TrimStart('#', ' ').Trim();
            }

            currentLines.Add(line);
        }

        if (currentLines.Count > 0)
        {
            chunks.Add(CreateChunk(chunkIndex, currentHeading, currentLines));
        }

        return chunks
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk.Content))
            .ToArray();
    }

    private static bool IsHeading(string line)
    {
        return line.StartsWith('#');
    }

    private static MarkdownChunk CreateChunk(int chunkIndex, string? heading, List<string> lines)
    {
        var content = string.Join("\n", lines).Trim();
        var tokenCount = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return new MarkdownChunk(chunkIndex, heading, content, tokenCount);
    }
}
