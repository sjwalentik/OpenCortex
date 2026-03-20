using System.Text.Json;
using OpenCortex.Core.Persistence;
using OpenCortex.Orchestration.Memory;
using OpenCortex.Indexer.Indexing;

namespace OpenCortex.Tools.Memory.Handlers;

public sealed class ForgetMemoryHandler : IToolHandler
{
    private readonly IManagedDocumentStore _documentStore;
    private readonly IMemoryBrainResolver _brainResolver;
    private readonly IManagedContentBrainIndexingService _indexingService;

    public ForgetMemoryHandler(
        IManagedDocumentStore documentStore,
        IMemoryBrainResolver brainResolver,
        IManagedContentBrainIndexingService indexingService)
    {
        _documentStore = documentStore;
        _brainResolver = brainResolver;
        _indexingService = indexingService;
    }

    public string ToolName => "forget_memory";
    public string Category => "memory";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var memoryPath = arguments.GetProperty("memory_path").GetString()?.Trim()
            ?? throw new ArgumentException("memory_path is required");
        var reason = arguments.TryGetProperty("reason", out var reasonElement)
            ? reasonElement.GetString()?.Trim()
            : null;

        if (!memoryPath.StartsWith("memories/", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Only memory documents under memories/ can be forgotten."
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

        var document = await _documentStore.GetManagedDocumentByCanonicalPathAsync(
            customerId,
            brainResult.BrainId!,
            memoryPath,
            cancellationToken);

        if (document is null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Memory not found: {memoryPath}"
            });
        }

        await _documentStore.SoftDeleteManagedDocumentAsync(
            customerId,
            brainResult.BrainId!,
            document.ManagedDocumentId,
            userId,
            cancellationToken);

        await _indexingService.ReindexAsync(
            customerId,
            brainResult.BrainId!,
            "memory-forget",
            cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            forgotten = memoryPath,
            reason
        });
    }
}
