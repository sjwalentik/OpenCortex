using System.ComponentModel;
using ModelContextProtocol.Server;
using OpenCortex.Core.Persistence;
using OpenCortex.Persistence.Postgres;
using OpenCortex.Retrieval.Execution;

namespace OpenCortex.McpServer;

/// <summary>
/// MCP tool implementations exposed to agents via the Model Context Protocol.
/// All tools are brain-scoped and return structured, agent-ready responses.
/// </summary>
[McpServerToolType]
public sealed class OpenCortexTools(
    IBrainCatalogStore brainCatalogStore,
    OqlQueryExecutor queryExecutor)
{
    // -----------------------------------------------------------------------
    // list_brains
    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "List all active brains registered in OpenCortex. " +
        "Returns brain IDs, names, modes, and source root counts. " +
        "Use this to discover available brains before querying them.")]
    public async Task<ListBrainsResult> list_brains(CancellationToken cancellationToken)
    {
        var brains = await brainCatalogStore.ListBrainsAsync(cancellationToken);
        var active = brains.Where(b => b.Status != "retired").ToList();
        return new ListBrainsResult(
            active.Count,
            active.Select(b => new BrainItem(b.BrainId, b.Name, b.Mode, b.Status, b.SourceRootCount)).ToList());
    }

    // -----------------------------------------------------------------------
    // query_brain
    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Execute an OQL query against a specific brain. " +
        "oql must include a FROM clause targeting the exact brain ID (e.g. FROM brain(\"my-brain\")). " +
        "Returns ranked results with titles, snippets, scores, and score breakdowns. " +
        "Rank modes: keyword, semantic, hybrid. " +
        "Example: FROM brain(\"my-brain\") SEARCH \"retention strategy\" RANK hybrid LIMIT 10")]
    public async Task<QueryBrainResult> query_brain(
        [Description("Full OQL query string. Must include FROM brain(\"...\") with a valid brain ID.")] string oql,
        CancellationToken cancellationToken)
    {
        // Validate that the OQL targets at least one brain before executing
        if (!oql.Contains("FROM brain(", StringComparison.OrdinalIgnoreCase))
        {
            return QueryBrainResult.Failure(
                oql,
                "OQL must include a FROM brain(...) clause. Example: FROM brain(\"my-brain\") SEARCH \"term\" RANK hybrid LIMIT 10");
        }

        OqlQueryExecutionResult result;
        try
        {
            result = await queryExecutor.ExecuteAsync(oql, cancellationToken);
        }
        catch (Exception ex)
        {
            return QueryBrainResult.Failure(oql, $"Query execution failed: {ex.Message}");
        }

        var items = result.Results.Select(r => new QueryResultItem(
            r.DocumentId,
            r.BrainId,
            r.CanonicalPath,
            r.Title,
            r.Snippet,
            Math.Round(r.Score, 4),
            r.Reason,
            new ScoreBreakdownItem(
                Math.Round(r.Breakdown.KeywordScore, 4),
                Math.Round(r.Breakdown.SemanticScore, 4),
                Math.Round(r.Breakdown.GraphScore, 4)))).ToList();

        var summary = result.Summary;

        return new QueryBrainResult(
            Oql: oql,
            Error: null,
            TotalResults: summary.TotalResults,
            MaxScore: Math.Round(summary.MaxScore, 4),
            MinScore: Math.Round(summary.MinScore, 4),
            ResultsWithKeywordSignal: summary.ResultsWithKeywordSignal,
            ResultsWithSemanticSignal: summary.ResultsWithSemanticSignal,
            ResultsWithGraphSignal: summary.ResultsWithGraphSignal,
            Results: items);
    }

    // -----------------------------------------------------------------------
    // get_document
    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Retrieve the full detail for a specific brain by ID. " +
        "Returns the brain's name, mode, status, description, and all configured source roots. " +
        "Use this to understand what a brain indexes before querying it.")]
    public async Task<GetBrainResult> get_brain(
        [Description("The brain ID to look up (e.g. \"my-brain\").")] string brain_id,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(brain_id))
            return new GetBrainResult(null, "brain_id is required.");

        var brain = await brainCatalogStore.GetBrainAsync(brain_id, cancellationToken);
        if (brain is null)
            return new GetBrainResult(null, $"Brain '{brain_id}' not found.");

        return new GetBrainResult(
            new BrainDetailItem(
                brain.BrainId,
                brain.Name,
                brain.Slug,
                brain.Mode,
                brain.Status,
                brain.Description,
                brain.SourceRoots.Select(sr => new SourceRootItem(
                    sr.SourceRootId,
                    sr.Path,
                    sr.PathType,
                    sr.IsWritable,
                    sr.IncludePatterns,
                    sr.ExcludePatterns)).ToList()),
            null);
    }
}

// ---------------------------------------------------------------------------
// Result shapes
// ---------------------------------------------------------------------------

public sealed record ListBrainsResult(int Count, IReadOnlyList<BrainItem> Brains);

public sealed record BrainItem(string BrainId, string Name, string Mode, string Status, int SourceRootCount);

public sealed record QueryBrainResult(
    string Oql,
    string? Error,
    int TotalResults,
    double MaxScore,
    double MinScore,
    int ResultsWithKeywordSignal,
    int ResultsWithSemanticSignal,
    int ResultsWithGraphSignal,
    IReadOnlyList<QueryResultItem> Results)
{
    public static QueryBrainResult Failure(string oql, string message) =>
        new(oql, message, 0, 0, 0, 0, 0, 0, []);
}

public sealed record QueryResultItem(
    string DocumentId,
    string BrainId,
    string CanonicalPath,
    string Title,
    string? Snippet,
    double Score,
    string Reason,
    ScoreBreakdownItem Breakdown);

public sealed record ScoreBreakdownItem(double Keyword, double Semantic, double Graph);

public sealed record GetBrainResult(BrainDetailItem? Brain, string? Error);

public sealed record BrainDetailItem(
    string BrainId,
    string Name,
    string Slug,
    string Mode,
    string Status,
    string? Description,
    IReadOnlyList<SourceRootItem> SourceRoots);

public sealed record SourceRootItem(
    string SourceRootId,
    string Path,
    string PathType,
    bool IsWritable,
    IReadOnlyList<string> IncludePatterns,
    IReadOnlyList<string> ExcludePatterns);
