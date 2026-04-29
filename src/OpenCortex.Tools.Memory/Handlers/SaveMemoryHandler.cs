using System.Text.Json;
using OpenCortex.Core.Configuration;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Tenancy;
using OpenCortex.Orchestration.Memory;
using OpenCortex.Indexer.Indexing;

namespace OpenCortex.Tools.Memory.Handlers;

public sealed class SaveMemoryHandler : IToolHandler
{
    private readonly IManagedDocumentStore _documentStore;
    private readonly IMemoryBrainResolver _brainResolver;
    private readonly IManagedContentBrainIndexingService _indexingService;
    private readonly ISubscriptionStore _subscriptionStore;
    private readonly OpenCortexOptions _options;

    public SaveMemoryHandler(
        IManagedDocumentStore documentStore,
        IMemoryBrainResolver brainResolver,
        IManagedContentBrainIndexingService indexingService,
        ISubscriptionStore subscriptionStore,
        OpenCortexOptions options)
    {
        _documentStore = documentStore;
        _brainResolver = brainResolver;
        _indexingService = indexingService;
        _subscriptionStore = subscriptionStore;
        _options = options;
    }

    public string ToolName => "save_memory";
    public string Category => "memory";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var content = arguments.GetProperty("content").GetString()?.Trim()
            ?? throw new ArgumentException("content is required");
        var category = arguments.GetProperty("category").GetString()?.Trim()
            ?? throw new ArgumentException("category is required");

        if (!MemoryToolSupport.IsValidCategory(category))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Unsupported memory category '{category}'."
            });
        }

        if (!MemoryToolSupport.TryResolveTenantScope(context, out var customerId, out var userId, out var scopeError))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = scopeError
            });
        }

        var normalizedCategory = MemoryToolSupport.NormalizeCategory(category);
        var confidence = arguments.TryGetProperty("confidence", out var confidenceElement)
            ? MemoryToolSupport.NormalizeConfidence(confidenceElement.GetString())
            : "medium";
        var tags = MemoryToolSupport.ReadTags(arguments);

        var brainResult = await _brainResolver.ResolveAsync(customerId, userId, cancellationToken);
        if (!brainResult.Success)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = brainResult.Error,
                needs_configuration = brainResult.NeedsConfiguration
            });
        }

        var billingState = await GetBillingStateAsync(customerId, cancellationToken);
        var plan = ResolvePlanEntitlements(billingState.PlanId);
        var duplicate = await FindDuplicateMemoryAsync(
            customerId,
            brainResult.BrainId!,
            normalizedCategory,
            content,
            cancellationToken);

        if (duplicate is not null)
        {
            return JsonSerializer.Serialize(new
            {
                success = true,
                duplicate = true,
                memory_path = duplicate.CanonicalPath,
                brain_id = duplicate.BrainId,
                category = normalizedCategory,
                confidence = duplicate.Frontmatter.TryGetValue("confidence", out var existingConfidence)
                    ? existingConfidence
                    : null,
                tags = duplicate.Frontmatter.TryGetValue("tags", out var existingTags) && !string.IsNullOrWhiteSpace(existingTags)
                    ? existingTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : Array.Empty<string>()
            });
        }

        var slug = MemoryToolSupport.CreateMemorySlug(normalizedCategory);
        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["category"] = normalizedCategory,
            ["confidence"] = confidence,
            ["tags"] = string.Join(",", tags)
        };

        if (!context.ConversationId.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
        {
            frontmatter["source_conversation"] = context.ConversationId;
        }

        ManagedDocumentDetail document;
        try
        {
            document = await _documentStore.CreateManagedDocumentAsync(
                new ManagedDocumentCreateRequest(
                    BrainId: brainResult.BrainId!,
                    CustomerId: customerId,
                    Title: MemoryToolSupport.BuildTitle(normalizedCategory, content),
                    Slug: slug,
                    Content: content,
                    Frontmatter: frontmatter,
                    Status: "published",
                    UserId: userId,
                    MaxActiveDocuments: plan.MaxDocuments >= 0 ? plan.MaxDocuments : null,
                    QuotaExceededMessage: $"Document limit reached for plan '{billingState.PlanId}'. Review existing memories, forget one if needed, or upgrade before retrying save_memory."),
                cancellationToken);
        }
        catch (ManagedDocumentQuotaExceededException)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Document limit reached for plan '{billingState.PlanId}'. Review existing memories, forget one if needed, or upgrade before retrying save_memory.",
                error_code = "memory_quota_reached",
                quota_exceeded = true,
                suggestion = "Use recall_memories to review what is already saved, remove an older or low-value memory with forget_memory or from the Memories page, or upgrade your plan, then retry save_memory."
            });
        }

        await _indexingService.ReindexAsync(
            customerId,
            brainResult.BrainId!,
            "memory-save",
            cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            memory_path = document.CanonicalPath,
            brain_id = document.BrainId,
            category = normalizedCategory,
            confidence,
            tags
        });
    }

    private PlanEntitlements ResolvePlanEntitlements(string? planId)
    {
        if (!string.IsNullOrWhiteSpace(planId)
            && _options.Billing.Plans.TryGetValue(planId, out var configuredPlan))
        {
            return configuredPlan;
        }

        return _options.Billing.Plans[HostedBillingStateResolver.FreePlanId];
    }

    private async Task<ManagedDocumentDetail?> FindDuplicateMemoryAsync(
        string customerId,
        string brainId,
        string category,
        string content,
        CancellationToken cancellationToken)
    {
        var pathPrefix = MemoryToolSupport.BuildPathPrefix(category);
        var candidates = await _documentStore.ListManagedDocumentDetailsAsync(
            customerId,
            brainId,
            pathPrefix,
            limit: int.MaxValue,
            cancellationToken: cancellationToken);

        ManagedDocumentDetail? bestDuplicate = null;
        var bestSimilarity = 0.0;

        foreach (var candidate in candidates)
        {
            if (candidate.IsDeleted)
            {
                continue;
            }

            var similarity = MemoryToolSupport.CalculateContentSimilarity(content, candidate.Content);
            if (similarity > bestSimilarity)
            {
                bestDuplicate = candidate;
                bestSimilarity = similarity;
            }
        }

        return bestSimilarity >= 0.92 ? bestDuplicate : null;
    }

    private async Task<EffectiveBillingState> GetBillingStateAsync(string customerId, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionStore.GetSubscriptionAsync(customerId, cancellationToken)
            ?? await _subscriptionStore.EnsureFreeSubscriptionAsync(customerId, cancellationToken);
        return HostedBillingStateResolver.Resolve(subscription, DateTimeOffset.UtcNow);
    }
}
