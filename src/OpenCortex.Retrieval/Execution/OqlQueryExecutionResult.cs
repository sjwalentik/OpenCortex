using OpenCortex.Core.Persistence;
using OpenCortex.Retrieval.Planning;

namespace OpenCortex.Retrieval.Execution;

public sealed record OqlQueryExecutionResult(
    RetrievalPlan Plan,
    IReadOnlyList<RetrievalResultRecord> Results)
{
    /// <summary>
    /// Summarises which scoring signals contributed across all results.
    /// Useful for surfacing in API responses and admin tooling.
    /// </summary>
    public ExecutionSummary Summary => new(
        TotalResults: Results.Count,
        ResultsWithKeywordSignal: Results.Count(r => SanitizeFiniteScore(r.Breakdown.KeywordScore) > 0),
        ResultsWithSemanticSignal: Results.Count(r => SanitizeFiniteScore(r.Breakdown.SemanticScore) > 0),
        ResultsWithGraphSignal: Results.Count(r => SanitizeFiniteScore(r.Breakdown.GraphScore) > 0),
        MaxScore: Results.Count > 0 ? Results.Max(r => SanitizeFiniteScore(r.Score)) : 0.0,
        MinScore: Results.Count > 0 ? Results.Min(r => SanitizeFiniteScore(r.Score)) : 0.0);

    private static double SanitizeFiniteScore(double value) => double.IsFinite(value) ? value : 0.0;
}

/// <summary>
/// Aggregate signal statistics across all results in a query execution.
/// </summary>
public sealed record ExecutionSummary(
    int TotalResults,
    int ResultsWithKeywordSignal,
    int ResultsWithSemanticSignal,
    int ResultsWithGraphSignal,
    double MaxScore,
    double MinScore);
