using OpenCortex.Core.Persistence;
using OpenCortex.Core.Query;
using OpenCortex.Indexer.Indexing;

namespace OpenCortex.Integration.Tests;

/// <summary>
/// A pure in-memory IDocumentQueryStore that scores against a BrainIngestionBatch
/// using the same keyword / semantic / graph signals as the Postgres implementation.
/// Enables full end-to-end indexing + retrieval tests without a database.
/// </summary>
internal sealed class InMemoryDocumentQueryStore : IDocumentQueryStore
{
    private readonly BrainIngestionBatch _batch;

    public InMemoryDocumentQueryStore(BrainIngestionBatch batch)
    {
        _batch = batch;
    }

    public Task<IReadOnlyList<RetrievalResultRecord>> SearchAsync(OqlQuery query, CancellationToken cancellationToken = default)
    {
        var hasSearch = !string.IsNullOrWhiteSpace(query.SearchText);
        var searchLower = query.SearchText?.ToLowerInvariant() ?? string.Empty;
        var rankMode = query.RankMode.ToLowerInvariant();
        var usesKeyword = hasSearch && rankMode != "semantic";
        var usesSemantic = hasSearch && (rankMode == "semantic" || rankMode == "hybrid");

        // Build a query embedding from the search text using the same token-hash
        // approach as DeterministicEmbeddingProvider, so cosine similarity is meaningful.
        var queryVector = usesSemantic ? BuildQueryVector(searchLower, _batch.Embeddings.FirstOrDefault()?.Dimensions ?? 1536) : null;

        // Index edges by document for graph boost
        var edgeCountByDoc = _batch.LinkEdges
            .SelectMany(e => new[] { e.FromDocumentId, e.ToDocumentId })
            .Where(id => id is not null)
            .GroupBy(id => id!)
            .ToDictionary(g => g.Key, g => g.Count());

        var results = new List<RetrievalResultRecord>();

        foreach (var doc in _batch.Documents)
        {
            if (!string.Equals(doc.BrainId, query.BrainId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (doc.IsDeleted)
                continue;
            if (!PassesFilters(doc, query.Filters))
                continue;

            // Collect chunks belonging to this document
            var docChunks = _batch.Chunks
                .Where(c => c.DocumentId == doc.DocumentId)
                .ToList();

            var titleLower = doc.Title.ToLowerInvariant();
            var contentLower = string.Join(" ", docChunks.Select(c => c.Content)).ToLowerInvariant();

            var titleMatched = hasSearch && titleLower.Contains(searchLower);
            var contentMatched = hasSearch && !titleMatched && contentLower.Contains(searchLower);

            // Keyword score: title hit = 2.0, content hit = 1.0, no search = 0.25 (metadata-only)
            double keywordScore = 0.0;
            if (usesKeyword)
            {
                keywordScore = titleMatched ? 2.0 : contentMatched ? 1.0 : 0.0;
            }
            else if (!hasSearch)
            {
                keywordScore = 0.25;
            }

            // Semantic score: cosine similarity between query vector and best matching chunk embedding
            double semanticScore = 0.0;
            if (usesSemantic && queryVector is not null)
            {
                foreach (var chunk in docChunks)
                {
                    var embedding = _batch.Embeddings.FirstOrDefault(e => e.ChunkId == chunk.ChunkId);
                    if (embedding is not null)
                    {
                        var sim = CosineSimilarity(queryVector, embedding.Vector.ToArray());
                        if (sim > semanticScore)
                            semanticScore = sim;
                    }
                }
            }

            // Graph score: capped at 5 edges × 0.15
            edgeCountByDoc.TryGetValue(doc.DocumentId, out var edgeCount);
            var graphScore = Math.Min(edgeCount, 5) * 0.15;

            double totalScore = rankMode switch
            {
                "semantic" => semanticScore + graphScore,
                "hybrid"   => keywordScore + semanticScore + graphScore,
                _          => keywordScore + graphScore,
            };

            // Skip results with zero score when search text was provided
            if (hasSearch && usesKeyword && totalScore <= 0 && !usesSemantic)
                continue;

            var bestChunk = docChunks.FirstOrDefault();
            var snippet = bestChunk?.Content is { } c ? c[..Math.Min(280, c.Length)] : null;

            var breakdown = new ScoreBreakdown(keywordScore, semanticScore, graphScore);
            var reason = BuildReason(keywordScore, semanticScore, graphScore,
                titleMatched, contentMatched, edgeCount, hasSearch, usesSemantic);

            results.Add(new RetrievalResultRecord(
                doc.DocumentId,
                doc.BrainId,
                doc.CanonicalPath,
                doc.Title,
                bestChunk?.ChunkId,
                snippet,
                totalScore,
                reason,
                breakdown));
        }

        var ordered = results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Title)
            .Take(query.Limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<RetrievalResultRecord>>(ordered);
    }

    private static bool PassesFilters(DocumentRecord doc, IReadOnlyList<OqlFilter> filters)
    {
        foreach (var filter in filters)
        {
            if (!string.Equals(filter.Operator, "=", StringComparison.Ordinal))
                continue;

            var match = filter.Field.ToLowerInvariant() switch
            {
                "tag"   => doc.Frontmatter.TryGetValue("tag", out var tag) && string.Equals(tag, filter.Value, StringComparison.OrdinalIgnoreCase),
                "title" => string.Equals(doc.Title, filter.Value, StringComparison.OrdinalIgnoreCase),
                "path"  => string.Equals(doc.CanonicalPath, filter.Value, StringComparison.OrdinalIgnoreCase),
                "path_prefix" => doc.CanonicalPath.StartsWith(BuildPathPrefixValue(filter.Value), StringComparison.OrdinalIgnoreCase),
                "type"  => string.Equals(doc.DocumentType, filter.Value, StringComparison.OrdinalIgnoreCase),
                _       => true,
            };

            if (!match)
                return false;
        }

        return true;
    }

    private static string BuildPathPrefixValue(string pathPrefix)
    {
        var normalized = pathPrefix.Trim().Replace('\\', '/').Trim('/');
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : normalized + "/";
    }

    private static float[] BuildQueryVector(string text, int dimensions)
    {
        // Mirrors DeterministicEmbeddingProvider token-hash logic so similarity is meaningful.
        var vector = new float[dimensions];
        var tokens = System.Text.RegularExpressions.Regex.Matches(text, "[a-z0-9_/-]+")
            .Select(m => m.Value)
            .ToArray();

        if (tokens.Length == 0)
            return vector;

        foreach (var token in tokens)
        {
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
            var index = BitConverter.ToInt32(hash, 0) & int.MaxValue;
            var sign = (hash[4] & 1) == 0 ? 1f : -1f;
            var weight = 1f + (hash[5] / 255f);
            vector[index % dimensions] += sign * weight;
        }

        double mag = 0;
        foreach (var v in vector) mag += v * v;
        if (mag > 0)
        {
            var scale = 1.0 / Math.Sqrt(mag);
            for (var i = 0; i < vector.Length; i++)
                vector[i] = (float)(vector[i] * scale);
        }

        return vector;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0;

        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot  += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denom <= 0 ? 0 : dot / denom;
    }

    private static string BuildReason(
        double keywordScore, double semanticScore, double graphScore,
        bool titleMatched, bool contentMatched, int edgeCount,
        bool hasSearch, bool usesSemantic)
    {
        var parts = new List<string>();

        if (titleMatched && keywordScore > 0)
            parts.Add($"title match ({keywordScore:F2})");
        else if (contentMatched && keywordScore > 0)
            parts.Add($"content match ({keywordScore:F2})");
        else if (!hasSearch && keywordScore > 0)
            parts.Add($"metadata filter ({keywordScore:F2})");

        if (usesSemantic && semanticScore > 0)
            parts.Add($"semantic similarity ({semanticScore:F2})");

        if (graphScore > 0)
            parts.Add($"graph boost ×{edgeCount} ({graphScore:F2})");

        return parts.Count > 0 ? string.Join(" + ", parts) : "metadata filter match";
    }
}
