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

        var filters = BuildFilters(query, command);
        var hasSearchText = !string.IsNullOrWhiteSpace(query.SearchText);
        var searchPattern = hasSearchText ? $"%{query.SearchText}%" : "%";
        var usesSemantic = hasSearchText && (string.Equals(query.RankMode, "semantic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(query.RankMode, "hybrid", StringComparison.OrdinalIgnoreCase));
        var queryEmbedding = usesSemantic ? await _embeddingProvider.GenerateAsync(query.SearchText, cancellationToken) : null;
        var queryVectorLiteral = queryEmbedding is not null ? EmbeddingVector.ToVectorLiteral(queryEmbedding.Vector) : null;
        var searchPredicate = hasSearchText && !string.Equals(query.RankMode, "semantic", StringComparison.OrdinalIgnoreCase)
            ? "(d.title ILIKE @search_pattern OR c.content ILIKE @search_pattern OR e.embedding_id IS NOT NULL)"
            : "1 = 1";
        var scoreExpression = BuildScoreExpression(query.RankMode, hasSearchText, usesSemantic);
        var reasonExpression = BuildReasonExpression(query.RankMode, hasSearchText, usesSemantic);

        command.CommandText = $"""
            SELECT
                d.document_id,
                d.brain_id,
                d.canonical_path,
                d.title,
                c.chunk_id,
                LEFT(c.content, 280) AS snippet,
                {scoreExpression} AS score,
                {reasonExpression} AS reason
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
            results.Add(new RetrievalResultRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetDouble(6),
                reader.GetString(7)));
        }

        return results;
    }

    private static string BuildScoreExpression(string rankMode, bool hasSearchText, bool usesSemantic)
    {
        var keywordScore = hasSearchText
            ? "CASE WHEN d.title ILIKE @search_pattern THEN 2.0 WHEN c.content ILIKE @search_pattern THEN 1.0 ELSE 0.0 END"
            : "0.25";

        var semanticScore = usesSemantic
            ? "CASE WHEN e.embedding_id IS NOT NULL THEN (1 - (e.vector <=> CAST(@query_vector AS vector))) ELSE 0.0 END"
            : "0.0";

        const string graphScore = "LEAST(COALESCE(graph.related_edge_count, 0), 5) * 0.15";

        return rankMode.ToLowerInvariant() switch
        {
            "semantic" => $"(({semanticScore}) + ({graphScore}))",
            "hybrid" => $"(({keywordScore}) + ({semanticScore}) + ({graphScore}))",
            _ => $"(({keywordScore}) + ({graphScore}))",
        };
    }

    private static string BuildReasonExpression(string rankMode, bool hasSearchText, bool usesSemantic)
    {
        if (string.Equals(rankMode, "semantic", StringComparison.OrdinalIgnoreCase) && usesSemantic)
        {
            return "CASE WHEN e.embedding_id IS NOT NULL AND COALESCE(graph.related_edge_count, 0) > 0 THEN 'semantic similarity + graph boost' WHEN e.embedding_id IS NOT NULL THEN 'semantic similarity' WHEN COALESCE(graph.related_edge_count, 0) > 0 THEN 'graph boost' ELSE 'metadata filter match' END";
        }

        if (string.Equals(rankMode, "hybrid", StringComparison.OrdinalIgnoreCase) && usesSemantic)
        {
            return "CASE WHEN d.title ILIKE @search_pattern AND COALESCE(graph.related_edge_count, 0) > 0 THEN 'hybrid:title+semantic+graph' WHEN c.content ILIKE @search_pattern AND COALESCE(graph.related_edge_count, 0) > 0 THEN 'hybrid:content+semantic+graph' WHEN d.title ILIKE @search_pattern THEN 'hybrid:title+semantic' WHEN c.content ILIKE @search_pattern THEN 'hybrid:content+semantic' WHEN e.embedding_id IS NOT NULL AND COALESCE(graph.related_edge_count, 0) > 0 THEN 'hybrid:semantic+graph' WHEN e.embedding_id IS NOT NULL THEN 'hybrid:semantic' WHEN COALESCE(graph.related_edge_count, 0) > 0 THEN 'graph boost' ELSE 'metadata filter match' END";
        }

        if (hasSearchText)
        {
            return "CASE WHEN d.title ILIKE @search_pattern AND COALESCE(graph.related_edge_count, 0) > 0 THEN 'title match + graph boost' WHEN c.content ILIKE @search_pattern AND COALESCE(graph.related_edge_count, 0) > 0 THEN 'content match + graph boost' WHEN d.title ILIKE @search_pattern THEN 'title match' WHEN c.content ILIKE @search_pattern THEN 'content match' WHEN COALESCE(graph.related_edge_count, 0) > 0 THEN 'graph boost' ELSE 'metadata filter match' END";
        }

        return "CASE WHEN COALESCE(graph.related_edge_count, 0) > 0 THEN 'graph boost' ELSE 'metadata filter match' END";
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
