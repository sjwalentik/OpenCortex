using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenCortex.Core.Authoring;

public static partial class ManagedDocumentText
{
    public static string NormalizeSlug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "document";
        }

        var normalizedPath = value
            .Trim()
            .Replace('\\', '/');

        if (normalizedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath[..^3];
        }

        var segments = normalizedPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeSlugSegment)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        return segments.Length == 0
            ? "document"
            : string.Join('/', segments);
    }

    public static string BuildCanonicalPath(string slug) => $"{NormalizeSlug(slug)}.md";

    public static int CountWords(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        return WordRegex().Matches(content).Count;
    }

    public static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeSlugSegment(string value) =>
        NonSlugCharactersRegex().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex NonSlugCharactersRegex();

    [GeneratedRegex(@"\S+", RegexOptions.Compiled)]
    private static partial Regex WordRegex();
}
