using System.Text.Json;
using OpenCortex.Core.Persistence;
using OpenCortex.Orchestration.Memory;
using OpenCortex.Indexer.Indexing;

namespace OpenCortex.Tools.Memory.Handlers;

public sealed class SaveMemoryHandler : IToolHandler
{
    private readonly IManagedDocumentStore _documentStore;
    private readonly IMemoryBrainResolver _brainResolver;
    private readonly IManagedContentBrainIndexingService _indexingService;

    public SaveMemoryHandler(
        IManagedDocumentStore documentStore,
        IMemoryBrainResolver brainResolver,
        IManagedContentBrainIndexingService indexingService)
    {
        _documentStore = documentStore;
        _brainResolver = brainResolver;
        _indexingService = indexingService;
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

        var slug = MemoryToolSupport.CreateMemorySlug(category);
        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["category"] = MemoryToolSupport.NormalizeCategory(category),
            ["confidence"] = confidence,
            ["tags"] = string.Join(",", tags)
        };

        if (!context.ConversationId.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
        {
            frontmatter["source_conversation"] = context.ConversationId;
        }

        var document = await _documentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest(
                BrainId: brainResult.BrainId!,
                CustomerId: customerId,
                Title: MemoryToolSupport.BuildTitle(category, content),
                Slug: slug,
                Content: content,
                Frontmatter: frontmatter,
                Status: "published",
                UserId: userId),
            cancellationToken);

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
            category = MemoryToolSupport.NormalizeCategory(category),
            confidence,
            tags
        });
    }
}
