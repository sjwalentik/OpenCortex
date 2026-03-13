using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using OpenCortex.Core.Authoring;
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
            RoundFinite(r.Score),
            r.Reason,
            new ScoreBreakdownItem(
                RoundFinite(r.Breakdown.KeywordScore),
                RoundFinite(r.Breakdown.SemanticScore),
                RoundFinite(r.Breakdown.GraphScore)))).ToList();

        var summary = result.Summary;

        return new QueryBrainResult(
            Oql: oql,
            Error: null,
            TotalResults: summary.TotalResults,
            MaxScore: RoundFinite(summary.MaxScore),
            MinScore: RoundFinite(summary.MinScore),
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
    // get_document
    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Retrieve the full stored content for a managed document in a managed-content brain. " +
        "Use this after query_brain when you want more than the ranked snippet. " +
        "Accepts either the retrieval document ID or the canonical path returned by query_brain.")]
    public async Task<GetDocumentResult> get_document(
        [Description("The managed-content brain ID that owns the document.")] string brain_id,
        [Description("Optional retrieval document ID / managed document ID returned by query_brain.")] string? document_id = null,
        [Description("Optional canonical path returned by query_brain, for example \"identity/pixel.md\".")] string? canonical_path = null,
        CancellationToken cancellationToken = default)
    {
        var tokenContext = GetRequiredTokenContext();

        if (string.IsNullOrWhiteSpace(brain_id))
        {
            return GetDocumentResult.Failure("brain_id is required.");
        }

        if (string.IsNullOrWhiteSpace(document_id) && string.IsNullOrWhiteSpace(canonical_path))
        {
            return GetDocumentResult.Failure("Provide document_id or canonical_path.");
        }

        var (brain, brainError) = await GetManagedContentBrainAsync(tokenContext.CustomerId, brain_id, cancellationToken);
        if (brain is null)
        {
            return GetDocumentResult.Failure(brainError!);
        }

        ManagedDocumentDetail? document;
        try
        {
            if (!string.IsNullOrWhiteSpace(document_id))
            {
                document = await managedDocumentStore.GetManagedDocumentAsync(
                    tokenContext.CustomerId,
                    brain.BrainId,
                    document_id,
                    cancellationToken);
            }
            else
            {
                document = await managedDocumentStore.GetManagedDocumentByCanonicalPathAsync(
                    tokenContext.CustomerId,
                    brain.BrainId,
                    canonical_path!,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return GetDocumentResult.Failure($"Document retrieval failed: {ex.Message}");
        }

        if (document is null)
        {
            return GetDocumentResult.Failure(
                !string.IsNullOrWhiteSpace(document_id)
                    ? $"Document '{document_id}' was not found in brain '{brain.BrainId}'."
                    : $"Document '{canonical_path}' was not found in brain '{brain.BrainId}'.");
        }

        return GetDocumentResult.Success(document);
    }

    // -----------------------------------------------------------------------
    // create_document
    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Create a managed document in a managed-content brain. " +
        "Lower-level create-only tool; prefer save_document for most agent workflows. " +
        "Requires mcp:write scope and a plan that allows MCP write access. " +
        "The brain is reindexed immediately after the document is created.")]
    public async Task<ManagedDocumentResult> create_document(
        [Description("The managed-content brain ID that will own the document.")] string brain_id,
        [Description("Document title. Required.")] string title,
        [Description("Markdown or plain text content for the document.")] string content,
        [Description("Optional slug override.")] string? slug = null,
        [Description("Optional frontmatter key/value pairs.")] Dictionary<string, string>? frontmatter = null,
        [Description("Document status. Defaults to draft when omitted.")] string? status = null,
        CancellationToken cancellationToken = default)
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
    // save_document
    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Create or update a managed document in a managed-content brain using its canonical path. " +
        "Preferred write tool for agents because it upserts by path and can infer the brain when the workspace has exactly one active managed-content brain. " +
        "Requires mcp:write scope and an MCP-write-enabled plan. " +
        "The brain is reindexed immediately after the document is saved.")]
    public async Task<SaveManagedDocumentResult> save_document(
        [Description("Canonical path for the document, for example \"projects/opencortex/frontend-portal-direction.md\".")] string canonical_path,
        [Description("Markdown or plain text content for the document.")] string content,
        [Description("Optional managed-content brain ID. Omit this when the workspace has exactly one active managed-content brain.")] string? brain_id = null,
        [Description("Optional document title. Defaults from the canonical path when omitted.")] string? title = null,
        [Description("Optional frontmatter key/value pairs.")] Dictionary<string, string>? frontmatter = null,
        [Description("Document status. Defaults to draft when omitted.")] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var tokenContext = GetRequiredTokenContext();

        if (string.IsNullOrWhiteSpace(canonical_path))
        {
            return SaveManagedDocumentResult.Failure("canonical_path is required.");
        }

        var writeAccessError = await ValidateWriteAccessAsync(tokenContext, cancellationToken);
        if (writeAccessError is not null)
        {
            return SaveManagedDocumentResult.Failure(writeAccessError);
        }

        var (brain, brainError) = await ResolveManagedContentBrainAsync(tokenContext.CustomerId, brain_id, cancellationToken);
        if (brain is null)
        {
            return SaveManagedDocumentResult.Failure(brainError!);
        }

        var normalizedSlug = ManagedDocumentText.NormalizeSlug(canonical_path);
        var normalizedCanonicalPath = ManagedDocumentText.BuildCanonicalPath(normalizedSlug);
        var documentTitle = string.IsNullOrWhiteSpace(title)
            ? BuildTitleFromCanonicalPath(normalizedCanonicalPath)
            : title;
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "draft" : status;
        var normalizedFrontmatter = frontmatter ?? new Dictionary<string, string>();

        var existing = await managedDocumentStore.GetManagedDocumentByCanonicalPathAsync(
            tokenContext.CustomerId,
            brain.BrainId,
            normalizedCanonicalPath,
            cancellationToken);

        try
        {
            if (existing is null)
            {
                var (billingState, plan) = await GetBillingContextAsync(tokenContext.CustomerId, cancellationToken);
                if (plan.MaxDocuments >= 0)
                {
                    var activeDocuments = await managedDocumentStore.CountActiveManagedDocumentsAsync(tokenContext.CustomerId, cancellationToken);
                    if (activeDocuments >= plan.MaxDocuments)
                    {
                        return SaveManagedDocumentResult.Failure(
                            $"Document limit reached for plan '{billingState.PlanId}'. Upgrade to continue adding more content.");
                    }
                }

                var created = await managedDocumentStore.CreateManagedDocumentAsync(
                    new ManagedDocumentCreateRequest(
                        BrainId: brain.BrainId,
                        CustomerId: tokenContext.CustomerId,
                        Title: documentTitle,
                        Slug: normalizedSlug,
                        Content: content ?? string.Empty,
                        Frontmatter: normalizedFrontmatter,
                        Status: normalizedStatus,
                        UserId: tokenContext.UserId),
                    cancellationToken);

                var createIndexRun = await managedContentBrainIndexingService.ReindexAsync(
                    tokenContext.CustomerId,
                    brain.BrainId,
                    "mcp-document-create",
                    cancellationToken);

                await SyncActiveDocumentCounterAsync(tokenContext.CustomerId, cancellationToken);

                return SaveManagedDocumentResult.Success("created", created, createIndexRun);
            }

            var updated = await managedDocumentStore.UpdateManagedDocumentAsync(
                new ManagedDocumentUpdateRequest(
                    ManagedDocumentId: existing.ManagedDocumentId,
                    BrainId: brain.BrainId,
                    CustomerId: tokenContext.CustomerId,
                    Title: documentTitle,
                    Slug: normalizedSlug,
                    Content: content ?? string.Empty,
                    Frontmatter: normalizedFrontmatter,
                    Status: normalizedStatus,
                    UserId: tokenContext.UserId),
                cancellationToken);

            if (updated is null)
            {
                return SaveManagedDocumentResult.Failure($"Document '{normalizedCanonicalPath}' was not found in brain '{brain.BrainId}'.");
            }

            var updateIndexRun = await managedContentBrainIndexingService.ReindexAsync(
                tokenContext.CustomerId,
                brain.BrainId,
                "mcp-document-update",
                cancellationToken);

            return SaveManagedDocumentResult.Success("updated", updated, updateIndexRun);
        }
        catch (InvalidOperationException ex)
        {
            return SaveManagedDocumentResult.Failure(ex.Message);
        }
    }

    // -----------------------------------------------------------------------
    // update_document
    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Update an existing managed document in a managed-content brain. " +
        "Lower-level update-by-id tool; prefer save_document for most agent workflows. " +
        "Requires mcp:write scope and an MCP-write-enabled plan. " +
        "The brain is reindexed immediately after the document is updated.")]
    public async Task<ManagedDocumentResult> update_document(
        [Description("The managed-content brain ID that owns the document.")] string brain_id,
        [Description("Managed document ID to update.")] string managed_document_id,
        [Description("Document title. Required.")] string title,
        [Description("Markdown or plain text content for the document.")] string content,
        [Description("Optional slug override.")] string? slug = null,
        [Description("Optional frontmatter key/value pairs.")] Dictionary<string, string>? frontmatter = null,
        [Description("Document status. Defaults to draft when omitted.")] string? status = null,
        CancellationToken cancellationToken = default)
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
        "Accepts either the managed document ID or canonical path and can infer the brain when the workspace has exactly one active managed-content brain. " +
        "Prefer canonical_path for routine agent workflows. " +
        "Requires mcp:write scope and an MCP-write-enabled plan. " +
        "The brain is reindexed immediately after the document is deleted.")]
    public async Task<DeleteManagedDocumentResult> delete_document(
        [Description("Optional managed-content brain ID. Omit this when the workspace has exactly one active managed-content brain.")] string? brain_id = null,
        [Description("Optional managed document ID to delete.")] string? managed_document_id = null,
        [Description("Optional canonical path to delete, for example \"projects/opencortex/frontend-portal-direction.md\".")] string? canonical_path = null,
        CancellationToken cancellationToken = default)
    {
        var tokenContext = GetRequiredTokenContext();

        if (string.IsNullOrWhiteSpace(managed_document_id) && string.IsNullOrWhiteSpace(canonical_path))
        {
            return DeleteManagedDocumentResult.Failure("Provide managed_document_id or canonical_path.");
        }

        var writeAccessError = await ValidateWriteAccessAsync(tokenContext, cancellationToken);
        if (writeAccessError is not null)
        {
            return DeleteManagedDocumentResult.Failure(writeAccessError);
        }

        var (brain, brainError) = await ResolveManagedContentBrainAsync(tokenContext.CustomerId, brain_id, cancellationToken);
        if (brain is null)
        {
            return DeleteManagedDocumentResult.Failure(brainError!);
        }

        var existing = !string.IsNullOrWhiteSpace(managed_document_id)
            ? await managedDocumentStore.GetManagedDocumentAsync(
                tokenContext.CustomerId,
                brain.BrainId,
                managed_document_id,
                cancellationToken)
            : await managedDocumentStore.GetManagedDocumentByCanonicalPathAsync(
                tokenContext.CustomerId,
                brain.BrainId,
                ManagedDocumentText.BuildCanonicalPath(ManagedDocumentText.NormalizeSlug(canonical_path)),
                cancellationToken);

        if (existing is null)
        {
            return DeleteManagedDocumentResult.Failure(
                !string.IsNullOrWhiteSpace(managed_document_id)
                    ? $"Document '{managed_document_id}' was not found in brain '{brain.BrainId}'."
                    : $"Document '{canonical_path}' was not found in brain '{brain.BrainId}'.");
        }

        var deleted = await managedDocumentStore.SoftDeleteManagedDocumentAsync(
            tokenContext.CustomerId,
            brain.BrainId,
            existing.ManagedDocumentId,
            tokenContext.UserId,
            cancellationToken);

        if (!deleted)
        {
            return DeleteManagedDocumentResult.Failure($"Document '{existing.ManagedDocumentId}' was not found in brain '{brain.BrainId}'.");
        }

        var indexRun = await managedContentBrainIndexingService.ReindexAsync(
            tokenContext.CustomerId,
            brain.BrainId,
            "mcp-document-delete",
            cancellationToken);

        await SyncActiveDocumentCounterAsync(tokenContext.CustomerId, cancellationToken);

        return DeleteManagedDocumentResult.Success(existing.ManagedDocumentId, indexRun);
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

    private async Task<(BrainDetail? Brain, string? Error)> ResolveManagedContentBrainAsync(
        string customerId,
        string? brainId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(brainId))
        {
            return await GetManagedContentBrainAsync(customerId, brainId, cancellationToken);
        }

        var managedContentBrains = (await brainCatalogStore.ListBrainsByCustomerAsync(customerId, cancellationToken))
            .Where(brain =>
                !string.Equals(brain.Status, "retired", StringComparison.OrdinalIgnoreCase)
                && string.Equals(brain.Mode, "managed-content", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return managedContentBrains.Count switch
        {
            0 => (null, "No active managed-content brains were found in this workspace."),
            1 => await GetManagedContentBrainAsync(customerId, managedContentBrains[0].BrainId, cancellationToken),
            _ => (null, "brain_id is required because this workspace has multiple active managed-content brains. Use list_brains to choose one.")
        };
    }

    private static string BuildTitleFromCanonicalPath(string canonicalPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(canonicalPath?.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Document";
        }

        var words = fileName
            .Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]);

        var title = string.Join(' ', words);
        return string.IsNullOrWhiteSpace(title) ? fileName : title;
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

    private static double RoundFinite(double value) => Math.Round(double.IsFinite(value) ? value : 0.0, 4);

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

public sealed record GetDocumentResult(ManagedDocumentItem? Document, string? Error)
{
    public static GetDocumentResult Success(ManagedDocumentDetail document) =>
        new(OpenCortexToolResultMapper.MapManagedDocument(document), null);

    public static GetDocumentResult Failure(string message) =>
        new(null, message);
}

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

public sealed record SaveManagedDocumentResult(
    string? Operation,
    ManagedDocumentItem? Document,
    IndexRunItem? IndexRun,
    string? Error)
{
    public static SaveManagedDocumentResult Success(string operation, ManagedDocumentDetail document, IndexRunRecord indexRun) =>
        new(operation, OpenCortexToolResultMapper.MapManagedDocument(document), OpenCortexToolResultMapper.MapIndexRun(indexRun), null);

    public static SaveManagedDocumentResult Failure(string message) =>
        new(null, null, null, message);
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
