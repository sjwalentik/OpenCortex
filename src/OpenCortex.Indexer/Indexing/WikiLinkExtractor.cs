using System.Text.RegularExpressions;

namespace OpenCortex.Indexer.Indexing;

public sealed partial class WikiLinkExtractor
{
    public IReadOnlyList<string> Extract(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        return WikiLinkRegex()
            .Matches(markdown)
            .Select(match => NormalizeTarget(match.Groups[1].Value))
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeTarget(string rawTarget)
    {
        var pipeIndex = rawTarget.IndexOf('|');
        var hashIndex = rawTarget.IndexOf('#');
        var stopIndex = new[] { pipeIndex, hashIndex }
            .Where(index => index >= 0)
            .DefaultIfEmpty(rawTarget.Length)
            .Min();

        return rawTarget[..stopIndex].Trim();
    }

    [GeneratedRegex("\\[\\[([^\\]]+)\\]\\]")]
    private static partial Regex WikiLinkRegex();
}
