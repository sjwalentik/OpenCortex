using OpenCortex.Core.Embeddings;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Query;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresDocumentQueryStore : IDocumentQueryStore
{
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly IEmbeddingProvider _embeddingProvider;

    public PostgresDocumentQueryStore(PostgresConnectionFactory connectionFactory, IEmbeddingProvider embeddingProvider)
    {
        _connectionFactory = connectionFactory;
        _embeddingProvider = embeddingProvider;
    }

    public async Task<IReadOnlyList<RetrievalResultRecord>> SearchAsync(OqlQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var hasSearchText = !string.IsNullOrWhiteSpace(query.SearchText);
        var searchPattern = hasSearchText ? $"%{query.SearchText}%" : "%";
        var usesSemantic = hasSearchText && (
            string.Equals(query.RankMode, "semantic", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(query.RankMode, "hybrid", StringComparison.OrdinalIgnoreCase));
        var usesKeyword = hasSearchText && !string.Equals(query.RankMode, "semantic", StringComparison.OrdinalIgnoreCase);

        var queryEmbedding = usesSemantic
            ? await _embeddingProvider.GenerateAsync(query.SearchText, cancellationToken)
            : null;
        var queryVectorLiteral = queryEmbedding is not null
            ? EmbeddingVector.ToVectorLiteral(queryEmbedding.Vector)
            : null;

        var filters = BuildFilters(query, command);

        // Each signal is returned as a separate column so the caller can build
        // an accurate per-signal breakdown rather than inferring it from text.
        var keywordScoreExpr = BuildKeywordScoreExpr(hasSearchText, usesKeyword);
        var semanticScoreExpr = BuildSemanticScoreExpr(usesSemantic);
        const string graphScoreExpr = "LEAST(COALESCE(graph.related_edge_count, 0), 5) * 0.15";

        var totalScoreExpr = query.RankMode.ToLowerInvariant() switch
        {
            "semantic" => $"(({semanticScoreExpr}) + ({graphScoreExpr}))",
            "hybrid"   => $"(({keywordScoreExpr}) + ({semanticScoreExpr}) + ({graphScoreExpr}))",
            _          => $"(({keywordScoreExpr}) + ({graphScoreExpr}))",
        };

        // Search predicate: keyword modes require at least a keyword or embedding hit.
        var searchPredicate = usesKeyword
            ? "(d.title ILIKE @search_pattern OR c.content ILIKE @search_pattern OR e.embedding_id IS NOT NULL)"
            : "1 = 1";

        command.CommandText = $"""
            SELECT
                d.document_id,
                d.brain_id,
                d.canonical_path,
                d.title,
                c.chunk_id,
                LEFT(c.content, 280)                AS snippet,
                {totalScoreExpr}                    AS score,
                {keywordScoreExpr}                  AS keyword_score,
                {semanticScoreExpr}                 AS semantic_score,
                {graphScoreExpr}                    AS graph_score,
                CASE WHEN d.title ILIKE @search_pattern THEN true ELSE false END AS title_matched,
                CASE WHEN c.content ILIKE @search_pattern THEN true ELSE false END AS content_matched,
                COALESCE(graph.related_edge_count, 0) AS related_edge_count
            FROM {_connectionFactory.Schema}.documents d
            LEFT JOIN {_connectionFactory.Schema}.chunks c ON c.document_id = d.document_id
            LEFT JOIN {_connectionFactory.Schema}.embeddings e ON e.chunk_id = c.chunk_id
            LEFT JOIN LATERAL (
                SELECT COUNT(*)::int AS related_edge_count
                FROM {_connectionFactory.Schema}.link_edges le
                WHERE le.brain_id = d.brain_id
                  AND (
                    le.from_document_id = d.document_id
                    OR le.to_document_id = d.document_id
                    OR le.target_ref = d.title
                    OR le.target_ref = d.canonical_path
                  )
            ) graph ON TRUE
            WHERE d.brain_id = @brain_id
              AND d.is_deleted = false
              AND {searchPredicate}
              {filters}
            ORDER BY score DESC, d.title ASC
            LIMIT @limit;
            """;

        command.Parameters.AddWithValue("brain_id", query.BrainId);
        command.Parameters.AddWithValue("search_pattern", searchPattern);
        command.Parameters.AddWithValue("limit", query.Limit);

        if (queryVectorLiteral is not null)
        {
            command.Parameters.AddWithValue("query_vector", queryVectorLiteral);
        }

        var results = new List<RetrievalResultRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var keywordScore  = reader.GetDouble(7);
            var semanticScore = reader.GetDouble(8);
            var graphScore    = reader.GetDouble(9);
            var titleMatched  = reader.GetBoolean(10);
            var contentMatched = reader.GetBoolean(11);
            var edgeCount     = reader.GetInt32(12);

            var breakdown = new ScoreBreakdown(keywordScore, semanticScore, graphScore);
            var reason = BuildReason(query.RankMode, keywordScore, semanticScore, graphScore,
                titleMatched, contentMatched, edgeCount, hasSearchText, usesSemantic);

            results.Add(new RetrievalResultRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetDouble(6),
                reason,
                breakdown));
        }

        return results;
    }

    private static string BuildKeywordScoreExpr(bool hasSearchText, bool usesKeyword)
    {
        if (!hasSearchText || !usesKeyword)
        {
            return hasSearchText ? "0.0" : "0.25";
        }

        return "CASE WHEN d.title ILIKE @search_pattern THEN 2.0 WHEN c.content ILIKE @search_pattern THEN 1.0 ELSE 0.0 END";
    }

    private static string BuildSemanticScoreExpr(bool usesSemantic)
    {
        return usesSemantic
            ? "CASE WHEN e.embedding_id IS NOT NULL THEN (1.0 - (e.vector <=> CAST(@query_vector AS vector))) ELSE 0.0 END"
            : "0.0";
    }

    /// <summary>
    /// Builds a structured, human-readable reason string from the actual
    /// per-signal score values rather than re-inferring from SQL CASE expressions.
    /// </summary>
    private static string BuildReason(
        string rankMode,
        double keywordScore,
        double semanticScore,
        double graphScore,
        bool titleMatched,
        bool contentMatched,
        int edgeCount,
        bool hasSearchText,
        bool usesSemantic)
    {
        var signals = new List<string>();

        if (titleMatched && keywordScore > 0)
        {
            signals.Add($"title match ({keywordScore:F2})");
        }
        else if (contentMatched && keywordScore > 0)
        {
            signals.Add($"content match ({keywordScore:F2})");
        }
        else if (!hasSearchText && keywordScore > 0)
        {
            signals.Add($"metadata filter ({keywordScore:F2})");
        }

        if (usesSemantic && semanticScore > 0)
        {
            signals.Add($"semantic similarity ({semanticScore:F2})");
        }

        if (graphScore > 0)
        {
            signals.Add($"graph boost ×{edgeCount} ({graphScore:F2})");
        }

        if (signals.Count == 0)
        {
            return rankMode.ToLowerInvariant() switch
            {
                "semantic" => "no embedding available",
                _          => "metadata filter match",
            };
        }

        return string.Join(" + ", signals);
    }

    private static string BuildFilters(OqlQuery query, Npgsql.NpgsqlCommand command)
    {
        if (query.Filters.Count == 0)
        {
            return string.Empty;
        }

        var clauses = new List<string>();
        var parameterIndex = 0;

        foreach (var filter in query.Filters)
        {
            if (!string.Equals(filter.Operator, "=", StringComparison.Ordinal))
            {
                continue;
            }

            var parameterName = $"filter_{parameterIndex++}";

            switch (filter.Field.ToLowerInvariant())
            {
                case "tag":
                    command.Parameters.AddWithValue(parameterName, filter.Value);
                    clauses.Add($"COALESCE(d.frontmatter ->> 'tag', '') = @{parameterName}");
                    break;
                case "title":
                    command.Parameters.AddWithValue(parameterName, filter.Value);
                    clauses.Add($"d.title = @{parameterName}");
                    break;
                case "path":
                    command.Parameters.AddWithValue(parameterName, filter.Value);
                    clauses.Add($"d.canonical_path = @{parameterName}");
                    break;
                case "type":
                    command.Parameters.AddWithValue(parameterName, filter.Value);
                    clauses.Add($"COALESCE(d.document_type, '') = @{parameterName}");
                    break;
            }
        }

        if (clauses.Count == 0)
        {
            return string.Empty;
        }

        return "AND " + string.Join(" AND ", clauses);
    }
}
