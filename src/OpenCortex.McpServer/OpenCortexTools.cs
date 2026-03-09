using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using OpenCortex.Core.Configuration;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Tenancy;
using OpenCortex.Indexer.Indexing;
using OpenCortex.Retrieval.Execution;

namespace OpenCortex.McpServer;

/// <summary>
/// MCP tool implementations exposed to agents via the Model Context Protocol.
/// All tools are brain-scoped and return structured, agent-ready responses.
/// </summary>
[McpServerToolType]
public sealed class OpenCortexTools(
    IBrainCatalogStore brainCatalogStore,
    OqlQueryExecutor queryExecutor,
    ISubscriptionStore subscriptionStore,
    IUsageCounterStore usageCounterStore,
    IManagedDocumentStore managedDocumentStore,
    IManagedContentBrainIndexingService managedContentBrainIndexingService,
    IHttpContextAccessor httpContextAccessor,
    OpenCortexOptions options)
{
    private const string DocumentsActiveCounterKey = "documents.active";

    // -----------------------------------------------------------------------
    // list_brains
    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "List all active brains registered in OpenCortex. " +
        "Returns brain IDs, names, modes, and source root counts. " +
        "Use this to discover available brains before querying them.")]
    public async Task<ListBrainsResult> list_brains(CancellationToken cancellationToken)
    {
        var tokenContext = GetRequiredTokenContext();
        var brains = await brainCatalogStore.ListBrainsByCustomerAsync(tokenContext.CustomerId, cancellationToken);
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
        var tokenContext = GetRequiredTokenContext();

        if (!oql.Contains("FROM brain(", StringComparison.OrdinalIgnoreCase))
        {
            return QueryBrainResult.Failure(
                oql,
                "OQL must include a FROM brain(...) clause. Example: FROM brain(\"my-brain\") SEARCH \"term\" RANK hybrid LIMIT 10");
        }

        var brainIds = ExtractBrainIds(oql);
        if (brainIds.Count > 0)
        {
            var allBrains = await brainCatalogStore.ListBrainsByCustomerAsync(tokenContext.CustomerId, cancellationToken);
            var brainMap = allBrains.ToDictionary(b => b.BrainId, b => b.Status, StringComparer.OrdinalIgnoreCase);
            foreach (var id in brainIds)
            {
                if (brainMap.TryGetValue(id, out var status))
                {
                    if (string.Equals(status, "retired", StringComparison.OrdinalIgnoreCase))
                    {
                        return QueryBrainResult.Failure(oql, $"Brain '{id}' is retired and cannot be queried. Use list_brains to see active brains.");
                    }
                }
                else
                {
                    return QueryBrainResult.Failure(oql, $"Brain '{id}' was not found. Use list_brains to see available brains.");
                }
            }
        }

        var quotaError = await ConsumeQueryQuotaAsync(tokenContext.CustomerId, cancellationToken);
        if (quotaError is not null)
        {
            return QueryBrainResult.Failure(oql, quotaError);
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
    // get_brain
    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Retrieve the full detail for a specific brain by ID. " +
        "Returns the brain's name, mode, status, description, and all configured source roots. " +
        "Use this to understand what a brain indexes before querying it.")]
    public async Task<GetBrainResult> get_brain(
        [Description("The brain ID to look up (e.g. \"my-brain\").")] string brain_id,
        CancellationToken cancellationToken)
    {
        var tokenContext = GetRequiredTokenContext();

        if (string.IsNullOrWhiteSpace(brain_id))
        {
            return new GetBrainResult(null, "brain_id is required.");
        }

        var brain = await brainCatalogStore.GetBrainByCustomerAsync(tokenContext.CustomerId, brain_id, cancellationToken);
        if (brain is null)
        {
            return new GetBrainResult(null, $"Brain '{brain_id}' not found.");
        }

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

    // -----------------------------------------------------------------------
    // create_document
    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Create a managed document in a managed-content brain. " +
        "Requires mcp:write scope and a plan that allows MCP write access. " +
        "The brain is reindexed immediately after the document is created.")]
    public async Task<ManagedDocumentResult> create_document(
        [Description("The managed-content brain ID that will own the document.")] string brain_id,
        [Description("Document title. Required.")] string title,
        [Description("Markdown or plain text content for the document.")] string content,
        [Description("Optional slug override.")] string? slug,
        [Description("Optional frontmatter key/value pairs.")] Dictionary<string, string>? frontmatter,
        [Description("Document status. Defaults to draft when omitted.")] string? status,
        CancellationToken cancellationToken)
    {
        var tokenContext = GetRequiredTokenContext();

        if (string.IsNullOrWhiteSpace(brain_id))
        {
            return ManagedDocumentResult.Failure("brain_id is required.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return ManagedDocumentResult.Failure("title is required.");
        }

        var writeAccessError = await ValidateWriteAccessAsync(tokenContext, cancellationToken);
        if (writeAccessError is not null)
        {
            return ManagedDocumentResult.Failure(writeAccessError);
        }

        var (brain, brainError) = await GetManagedContentBrainAsync(tokenContext.CustomerId, brain_id, cancellationToken);
        if (brain is null)
        {
            return ManagedDocumentResult.Failure(brainError!);
        }

        var (billingState, plan) = await GetBillingContextAsync(tokenContext.CustomerId, cancellationToken);
        if (plan.MaxDocuments >= 0)
        {
            var activeDocuments = await managedDocumentStore.CountActiveManagedDocumentsAsync(tokenContext.CustomerId, cancellationToken);
            if (activeDocuments >= plan.MaxDocuments)
            {
                return ManagedDocumentResult.Failure(
                    $"Document limit reached for plan '{billingState.PlanId}'. Upgrade to continue adding more content.");
            }
        }

        try
        {
            var document = await managedDocumentStore.CreateManagedDocumentAsync(
                new ManagedDocumentCreateRequest(
                    BrainId: brain.BrainId,
                    CustomerId: tokenContext.CustomerId,
                    Title: title,
                    Slug: slug,
                    Content: content ?? string.Empty,
                    Frontmatter: frontmatter ?? new Dictionary<string, string>(),
                    Status: string.IsNullOrWhiteSpace(status) ? "draft" : status,
                    UserId: tokenContext.UserId),
                cancellationToken);

            var indexRun = await managedContentBrainIndexingService.ReindexAsync(
                tokenContext.CustomerId,
                brain.BrainId,
                "mcp-document-create",
                cancellationToken);

            await SyncActiveDocumentCounterAsync(tokenContext.CustomerId, cancellationToken);

            return ManagedDocumentResult.Success(document, indexRun);
        }
        catch (InvalidOperationException ex)
        {
            return ManagedDocumentResult.Failure(ex.Message);
        }
    }

    // -----------------------------------------------------------------------
    // update_document
    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Update an existing managed document in a managed-content brain. " +
        "Requires mcp:write scope and an MCP-write-enabled plan. " +
        "The brain is reindexed immediately after the document is updated.")]
    public async Task<ManagedDocumentResult> update_document(
        [Description("The managed-content brain ID that owns the document.")] string brain_id,
        [Description("Managed document ID to update.")] string managed_document_id,
        [Description("Document title. Required.")] string title,
        [Description("Markdown or plain text content for the document.")] string content,
        [Description("Optional slug override.")] string? slug,
        [Description("Optional frontmatter key/value pairs.")] Dictionary<string, string>? frontmatter,
        [Description("Document status. Defaults to draft when omitted.")] string? status,
        CancellationToken cancellationToken)
    {
        var tokenContext = GetRequiredTokenContext();

        if (string.IsNullOrWhiteSpace(brain_id))
        {
            return ManagedDocumentResult.Failure("brain_id is required.");
        }

        if (string.IsNullOrWhiteSpace(managed_document_id))
        {
            return ManagedDocumentResult.Failure("managed_document_id is required.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return ManagedDocumentResult.Failure("title is required.");
        }

        var writeAccessError = await ValidateWriteAccessAsync(tokenContext, cancellationToken);
        if (writeAccessError is not null)
        {
            return ManagedDocumentResult.Failure(writeAccessError);
        }

        var (brain, brainError) = await GetManagedContentBrainAsync(tokenContext.CustomerId, brain_id, cancellationToken);
        if (brain is null)
        {
            return ManagedDocumentResult.Failure(brainError!);
        }

        try
        {
            var document = await managedDocumentStore.UpdateManagedDocumentAsync(
                new ManagedDocumentUpdateRequest(
                    ManagedDocumentId: managed_document_id,
                    BrainId: brain.BrainId,
                    CustomerId: tokenContext.CustomerId,
                    Title: title,
                    Slug: slug,
                    Content: content ?? string.Empty,
                    Frontmatter: frontmatter ?? new Dictionary<string, string>(),
                    Status: string.IsNullOrWhiteSpace(status) ? "draft" : status,
                    UserId: tokenContext.UserId),
                cancellationToken);

            if (document is null)
            {
                return ManagedDocumentResult.Failure($"Document '{managed_document_id}' was not found in brain '{brain.BrainId}'.");
            }

            var indexRun = await managedContentBrainIndexingService.ReindexAsync(
                tokenContext.CustomerId,
                brain.BrainId,
                "mcp-document-update",
                cancellationToken);

            return ManagedDocumentResult.Success(document, indexRun);
        }
        catch (InvalidOperationException ex)
        {
            return ManagedDocumentResult.Failure(ex.Message);
        }
    }

    // -----------------------------------------------------------------------
    // delete_document
    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Soft-delete a managed document from a managed-content brain. " +
        "Requires mcp:write scope and an MCP-write-enabled plan. " +
        "The brain is reindexed immediately after the document is deleted.")]
    public async Task<DeleteManagedDocumentResult> delete_document(
        [Description("The managed-content brain ID that owns the document.")] string brain_id,
        [Description("Managed document ID to delete.")] string managed_document_id,
        CancellationToken cancellationToken)
    {
        var tokenContext = GetRequiredTokenContext();

        if (string.IsNullOrWhiteSpace(brain_id))
        {
            return DeleteManagedDocumentResult.Failure("brain_id is required.");
        }

        if (string.IsNullOrWhiteSpace(managed_document_id))
        {
            return DeleteManagedDocumentResult.Failure("managed_document_id is required.");
        }

        var writeAccessError = await ValidateWriteAccessAsync(tokenContext, cancellationToken);
        if (writeAccessError is not null)
        {
            return DeleteManagedDocumentResult.Failure(writeAccessError);
        }

        var (brain, brainError) = await GetManagedContentBrainAsync(tokenContext.CustomerId, brain_id, cancellationToken);
        if (brain is null)
        {
            return DeleteManagedDocumentResult.Failure(brainError!);
        }

        var deleted = await managedDocumentStore.SoftDeleteManagedDocumentAsync(
            tokenContext.CustomerId,
            brain.BrainId,
            managed_document_id,
            tokenContext.UserId,
            cancellationToken);

        if (!deleted)
        {
            return DeleteManagedDocumentResult.Failure($"Document '{managed_document_id}' was not found in brain '{brain.BrainId}'.");
        }

        var indexRun = await managedContentBrainIndexingService.ReindexAsync(
            tokenContext.CustomerId,
            brain.BrainId,
            "mcp-document-delete",
            cancellationToken);

        await SyncActiveDocumentCounterAsync(tokenContext.CustomerId, cancellationToken);

        return DeleteManagedDocumentResult.Success(managed_document_id, indexRun);
    }

    // -----------------------------------------------------------------------
    // reindex_brain
    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Force a managed-content brain reindex. " +
        "Requires mcp:write scope and an MCP-write-enabled plan. " +
        "Use this after coordinated content changes that need retrieval to refresh immediately.")]
    public async Task<ReindexBrainResult> reindex_brain(
        [Description("The managed-content brain ID to reindex.")] string brain_id,
        CancellationToken cancellationToken)
    {
        var tokenContext = GetRequiredTokenContext();

        if (string.IsNullOrWhiteSpace(brain_id))
        {
            return ReindexBrainResult.Failure("brain_id is required.");
        }

        var writeAccessError = await ValidateWriteAccessAsync(tokenContext, cancellationToken);
        if (writeAccessError is not null)
        {
            return ReindexBrainResult.Failure(writeAccessError);
        }

        var (brain, brainError) = await GetManagedContentBrainAsync(tokenContext.CustomerId, brain_id, cancellationToken);
        if (brain is null)
        {
            return ReindexBrainResult.Failure(brainError!);
        }

        var indexRun = await managedContentBrainIndexingService.ReindexAsync(
            tokenContext.CustomerId,
            brain.BrainId,
            "mcp-reindex",
            cancellationToken);

        await SyncActiveDocumentCounterAsync(tokenContext.CustomerId, cancellationToken);

        return ReindexBrainResult.Success(indexRun);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static List<string> ExtractBrainIds(string oql)
    {
        var ids = new List<string>();
        var span = oql.AsSpan();
        const string marker = "brain(\"";
        int pos = 0;
        while (true)
        {
            var idx = span[pos..].IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                break;
            }

            var start = pos + idx + marker.Length;
            var end = oql.IndexOf('"', start);
            if (end < 0)
            {
                break;
            }

            ids.Add(oql[start..end]);
            pos = end + 1;
        }

        return ids;
    }

    private McpTokenContext GetRequiredTokenContext() =>
        httpContextAccessor.HttpContext.GetMcpTokenContext()
        ?? throw new InvalidOperationException("MCP token context is not available for this request.");

    private PlanEntitlements ResolvePlanEntitlements(string? planId)
    {
        if (!string.IsNullOrWhiteSpace(planId)
            && options.Billing.Plans.TryGetValue(planId, out var configuredPlan))
        {
            return configuredPlan;
        }

        return options.Billing.Plans["free"];
    }

    private async Task<(EffectiveBillingState BillingState, PlanEntitlements Plan)> GetBillingContextAsync(
        string customerId,
        CancellationToken cancellationToken)
    {
        var subscription = await subscriptionStore.GetSubscriptionAsync(customerId, cancellationToken)
            ?? await subscriptionStore.EnsureFreeSubscriptionAsync(customerId, cancellationToken);
        var billingState = HostedBillingStateResolver.Resolve(subscription, DateTimeOffset.UtcNow);
        return (billingState, ResolvePlanEntitlements(billingState.PlanId));
    }

    private async Task<string?> ValidateWriteAccessAsync(McpTokenContext tokenContext, CancellationToken cancellationToken)
    {
        if (!tokenContext.Scopes.Contains("mcp:write", StringComparer.OrdinalIgnoreCase))
        {
            return "Token scope 'mcp:write' is required for MCP write tools.";
        }

        var (billingState, plan) = await GetBillingContextAsync(tokenContext.CustomerId, cancellationToken);
        return plan.McpWrite
            ? null
            : $"Plan '{billingState.PlanId}' does not allow MCP write tools. Upgrade to continue.";
    }

    private async Task<(BrainDetail? Brain, string? Error)> GetManagedContentBrainAsync(
        string customerId,
        string brainId,
        CancellationToken cancellationToken)
    {
        var brain = await brainCatalogStore.GetBrainByCustomerAsync(customerId, brainId, cancellationToken);
        if (brain is null)
        {
            return (null, $"Brain '{brainId}' was not found in this workspace.");
        }

        if (!string.Equals(brain.Mode, "managed-content", StringComparison.OrdinalIgnoreCase))
        {
            return (null, $"Brain '{brainId}' is not a managed-content brain.");
        }

        return (brain, null);
    }

    private async Task<UsageCounterRecord> SyncActiveDocumentCounterAsync(string customerId, CancellationToken cancellationToken)
    {
        var activeDocuments = await managedDocumentStore.CountActiveManagedDocumentsAsync(customerId, cancellationToken);
        return await usageCounterStore.SetCounterAsync(
            new UsageCounterSetRequest(
                customerId,
                DocumentsActiveCounterKey,
                activeDocuments,
                null),
            cancellationToken);
    }

    private static string BuildMonthlyQueryCounterKey(DateTimeOffset nowUtc) => $"mcp.queries.{nowUtc:yyyy-MM}";

    private static DateTimeOffset BuildMonthlyQueryCounterResetAt(DateTimeOffset nowUtc) =>
        new DateTimeOffset(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);

    private async Task<string?> ConsumeQueryQuotaAsync(string customerId, CancellationToken cancellationToken)
    {
        var (billingState, plan) = await GetBillingContextAsync(customerId, cancellationToken);
        if (plan.McpQueriesPerMonth < 0)
        {
            return null;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var counter = await usageCounterStore.IncrementCounterAsync(
            new UsageCounterIncrementRequest(
                customerId,
                BuildMonthlyQueryCounterKey(nowUtc),
                1,
                BuildMonthlyQueryCounterResetAt(nowUtc)),
            cancellationToken);

        return counter.Value > plan.McpQueriesPerMonth
            ? $"Monthly MCP query limit reached for plan '{billingState.PlanId}'. Upgrade to continue."
            : null;
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

public sealed record ManagedDocumentResult(
    ManagedDocumentItem? Document,
    IndexRunItem? IndexRun,
    string? Error)
{
    public static ManagedDocumentResult Success(ManagedDocumentDetail document, IndexRunRecord indexRun) =>
        new(OpenCortexToolResultMapper.MapManagedDocument(document), OpenCortexToolResultMapper.MapIndexRun(indexRun), null);

    public static ManagedDocumentResult Failure(string message) =>
        new(null, null, message);
}

public sealed record DeleteManagedDocumentResult(
    string? ManagedDocumentId,
    IndexRunItem? IndexRun,
    string? Error)
{
    public static DeleteManagedDocumentResult Success(string managedDocumentId, IndexRunRecord indexRun) =>
        new(managedDocumentId, OpenCortexToolResultMapper.MapIndexRun(indexRun), null);

    public static DeleteManagedDocumentResult Failure(string message) =>
        new(null, null, message);
}

public sealed record ReindexBrainResult(
    IndexRunItem? IndexRun,
    string? Error)
{
    public static ReindexBrainResult Success(IndexRunRecord indexRun) =>
        new(OpenCortexToolResultMapper.MapIndexRun(indexRun), null);

    public static ReindexBrainResult Failure(string message) =>
        new(null, message);
}

public sealed record ManagedDocumentItem(
    string ManagedDocumentId,
    string BrainId,
    string Title,
    string Slug,
    string CanonicalPath,
    string Status,
    string Content,
    IReadOnlyDictionary<string, string> Frontmatter,
    int WordCount,
    DateTimeOffset UpdatedAt);

public sealed record IndexRunItem(
    string IndexRunId,
    string BrainId,
    string TriggerType,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int DocumentsSeen,
    int DocumentsIndexed,
    int DocumentsFailed,
    string? ErrorSummary);

internal static class OpenCortexToolResultMapper
{
    public static ManagedDocumentItem MapManagedDocument(ManagedDocumentDetail document) =>
        new(
            document.ManagedDocumentId,
            document.BrainId,
            document.Title,
            document.Slug,
            document.CanonicalPath,
            document.Status,
            document.Content,
            new Dictionary<string, string>(document.Frontmatter, StringComparer.OrdinalIgnoreCase),
            document.WordCount,
            document.UpdatedAt);

    public static IndexRunItem MapIndexRun(IndexRunRecord indexRun) =>
        new(
            indexRun.IndexRunId,
            indexRun.BrainId,
            indexRun.TriggerType,
            indexRun.Status,
            indexRun.StartedAt,
            indexRun.CompletedAt,
            indexRun.DocumentsSeen,
            indexRun.DocumentsIndexed,
            indexRun.DocumentsFailed,
            indexRun.ErrorSummary);
}
