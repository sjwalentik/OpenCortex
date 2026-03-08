using System.Text.RegularExpressions;

namespace OpenCortex.Core.Query;

public sealed partial class OqlParser
{
    public OqlQuery Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("OQL cannot be empty.", nameof(text));
        }

        var query = new OqlQuery();

        // Support both multiline OQL (one clause per line) and single-line OQL
        // (clauses separated by spaces). Split on newlines first; if the result is a
        // single token that contains multiple clauses, re-split on keyword boundaries.
        var rawLines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawLines.Length == 1)
        {
            rawLines = ClauseKeywordRegex().Split(rawLines[0].Trim())
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();
        }

        foreach (var rawLine in rawLines)
        {
            if (rawLine.StartsWith("FROM brain(", StringComparison.OrdinalIgnoreCase))
            {
                query = query with
                {
                    BrainId = ExtractQuotedValue(rawLine),
                };

                continue;
            }

            if (rawLine.StartsWith("SEARCH ", StringComparison.OrdinalIgnoreCase))
            {
                query = query with
                {
                    SearchText = ExtractQuotedValue(rawLine),
                };

                continue;
            }

            if (rawLine.StartsWith("WHERE ", StringComparison.OrdinalIgnoreCase))
            {
                query = query with
                {
                    Filters = ParseFilters(rawLine[6..].Trim()),
                };

                continue;
            }

            if (rawLine.StartsWith("RANK ", StringComparison.OrdinalIgnoreCase))
            {
                query = query with
                {
                    RankMode = rawLine[5..].Trim(),
                };

                continue;
            }

            if (rawLine.StartsWith("LIMIT ", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(rawLine[6..].Trim(), out var limit))
            {
                query = query with
                {
                    Limit = limit,
                };
            }
        }

        if (string.IsNullOrWhiteSpace(query.BrainId))
        {
            throw new InvalidOperationException("OQL must include FROM brain(\"...\").");
        }

        return query;
    }

    private static string ExtractQuotedValue(string line)
    {
        var match = QuotedValueRegex().Match(line);

        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not parse quoted value from '{line}'.");
        }

        return match.Groups[1].Value;
    }

    private static IReadOnlyList<OqlFilter> ParseFilters(string whereClause)
    {
        if (string.IsNullOrWhiteSpace(whereClause))
        {
            return [];
        }

        var parts = whereClause.Split(" AND ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var filters = new List<OqlFilter>();

        foreach (var part in parts)
        {
            var separatorIndex = part.IndexOf('=');

            if (separatorIndex <= 0)
            {
                throw new InvalidOperationException($"Could not parse WHERE clause segment '{part}'.");
            }

            var field = part[..separatorIndex].Trim();
            var value = part[(separatorIndex + 1)..].Trim();

            if (!value.StartsWith('"') || !value.EndsWith('"') || value.Length < 2)
            {
                throw new InvalidOperationException($"WHERE clause value for '{field}' must be quoted.");
            }

            filters.Add(new OqlFilter(field, "=", value[1..^1]));
        }

        return filters;
    }

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex QuotedValueRegex();

    /// <summary>
    /// Splits a single-line OQL string into clause tokens by matching the keyword
    /// boundaries (FROM, SEARCH, WHERE, RANK, LIMIT) that start each clause.
    /// The lookahead keeps the keyword at the start of each resulting token.
    /// </summary>
    [GeneratedRegex(@"(?=\b(?:FROM|SEARCH|WHERE|RANK|LIMIT)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex ClauseKeywordRegex();
}
