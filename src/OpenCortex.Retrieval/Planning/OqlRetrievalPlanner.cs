using OpenCortex.Core.Query;

namespace OpenCortex.Retrieval.Planning;

public sealed class OqlRetrievalPlanner
{
    private readonly OqlParser _parser = new();

    public RetrievalPlan BuildPlan(string oql)
    {
        var query = _parser.Parse(oql);
        var steps = new List<string>
        {
            $"Scope retrieval to brain '{query.BrainId}'.",
            string.IsNullOrWhiteSpace(query.SearchText)
                ? "Apply metadata-only retrieval plan."
                : $"Run keyword and semantic retrieval for '{query.SearchText}'.",
        };

        foreach (var filter in query.Filters)
        {
            steps.Add($"Apply metadata filter '{filter.Field} {filter.Operator} \"{filter.Value}\"'.");
        }

        steps.Add($"Rank results using '{query.RankMode}' mode.");
        steps.Add($"Trim to {query.Limit} result(s).");

        return new RetrievalPlan(
            query.BrainId,
            query.SearchText,
            query.Filters.Select(filter => $"{filter.Field} {filter.Operator} \"{filter.Value}\"").ToArray(),
            query.RankMode,
            query.Limit,
            steps);
    }
}
