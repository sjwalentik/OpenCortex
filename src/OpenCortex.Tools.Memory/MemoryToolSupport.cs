using System.Text.Json;

namespace OpenCortex.Tools.Memory;

internal static class MemoryToolSupport
{
    private static readonly HashSet<string> ValidCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "fact",
        "decision",
        "preference",
        "learning"
    };

    private static readonly HashSet<string> ValidConfidenceLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "high",
        "medium",
        "low"
    };

    public static bool TryResolveTenantScope(
        ToolExecutionContext context,
        out string customerId,
        out string userId,
        out string error)
    {
        customerId = context.TenantCustomerId ?? string.Empty;
        userId = context.TenantUserId ?? string.Empty;
        error = string.Empty;

        if (!string.IsNullOrWhiteSpace(customerId) && !string.IsNullOrWhiteSpace(userId))
        {
            return true;
        }

        error = "Memory tools require hosted tenant context. Tenant user or customer identity was not available.";
        return false;
    }

    public static bool IsValidCategory(string? category)
        => !string.IsNullOrWhiteSpace(category) && ValidCategories.Contains(category);

    public static bool IsValidConfidence(string? confidence)
        => !string.IsNullOrWhiteSpace(confidence) && ValidConfidenceLevels.Contains(confidence);

    public static string NormalizeCategory(string category) => category.Trim().ToLowerInvariant();

    public static string NormalizeConfidence(string? confidence)
        => IsValidConfidence(confidence) ? confidence!.Trim().ToLowerInvariant() : "medium";

    public static IReadOnlyList<string> ReadTags(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("tags", out var tagsElement) || tagsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return tagsElement.EnumerateArray()
            .Select(element => element.GetString()?.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string CreateMemorySlug(string category)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        return $"memories/{NormalizeCategory(category)}/{suffix}";
    }

    public static string BuildTitle(string category, string content)
    {
        var singleLine = content.Replace('\r', ' ').Replace('\n', ' ').Trim();
        var preview = singleLine.Length > 60 ? singleLine[..60].TrimEnd() + "..." : singleLine;
        return $"[{NormalizeCategory(category)}] {preview}";
    }

    public static string EscapeOqlLiteral(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public static string BuildPathPrefix(string? category)
        => string.IsNullOrWhiteSpace(category)
            ? "memories/"
            : $"memories/{NormalizeCategory(category!)}/";

    public static bool TryNormalizeMemoryPath(string? memoryPath, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(memoryPath))
        {
            return false;
        }

        var segments = memoryPath
            .Trim()
            .Replace('\\', '/')
            .TrimStart('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
        {
            return false;
        }

        var normalizedSegments = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (string.Equals(segment, ".", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return false;
            }

            normalizedSegments.Add(segment);
        }

        if (normalizedSegments.Count == 0)
        {
            return false;
        }

        normalizedPath = string.Join('/', normalizedSegments);
        return true;
    }

    public static string InferCategoryFromPath(string canonicalPath)
    {
        var segments = canonicalPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length >= 2 ? segments[1] : "memory";
    }

    public static string CreateSnippet(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalized = content.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length > 200 ? normalized[..200].TrimEnd() + "..." : normalized;
    }

    public static double GetConfidenceScoreMultiplier(string? confidence)
        => NormalizeConfidence(confidence) switch
        {
            "high" => 1.15,
            "low" => 0.75,
            _ => 1.0
        };

    public static double CalculateContentSimilarity(string left, string right)
    {
        var normalizedLeft = NormalizeComparableText(left);
        var normalizedRight = NormalizeComparableText(right);

        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
        {
            return 1.0;
        }

        var leftTokens = normalizedLeft.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = normalizedRight.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0.0;
        }

        var intersection = leftTokens.Count(token => rightTokens.Contains(token));
        var union = leftTokens.Count + rightTokens.Count - intersection;
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static string NormalizeComparableText(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();

        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
