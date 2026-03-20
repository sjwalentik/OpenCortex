using System.Text.Json;
using OpenCortex.Core.Persistence;
using OpenCortex.Orchestration.Memory;
using OpenCortex.Retrieval.Execution;

namespace OpenCortex.Tools.Memory.Handlers;

public sealed class RecallMemoriesHandler : IToolHandler
{
    private readonly OqlQueryExecutor _queryExecutor;
    private readonly IManagedDocumentStore _documentStore;
    private readonly IMemoryBrainResolver _brainResolver;

    public RecallMemoriesHandler(
        OqlQueryExecutor queryExecutor,
        IManagedDocumentStore documentStore,
        IMemoryBrainResolver brainResolver)
    {
        _queryExecutor = queryExecutor;
        _documentStore = documentStore;
        _brainResolver = brainResolver;
    }

    public string ToolName => "recall_memories";
    public string Category => "memory";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var query = arguments.GetProperty("query").GetString()?.Trim()
            ?? throw new ArgumentException("query is required");
        var category = arguments.TryGetProperty("category", out var categoryElement)
            ? categoryElement.GetString()?.Trim()
            : null;

        if (!string.IsNullOrWhiteSpace(category) && !MemoryToolSupport.IsValidCategory(category))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Unsupported memory category '{category}'.",
                memories = Array.Empty<object>()
            });
        }

        if (!MemoryToolSupport.TryResolveTenantScope(context, out var customerId, out var userId, out var scopeError))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = scopeError,
                memories = Array.Empty<object>()
            });
        }

        var brainResult = await _brainResolver.ResolveAsync(customerId, userId, cancellationToken);
        if (!brainResult.Success)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = brainResult.Error,
                needs_configuration = brainResult.NeedsConfiguration,
                memories = Array.Empty<object>()
            });
        }

        var limit = arguments.TryGetProperty("limit", out var limitElement) && limitElement.TryGetInt32(out var parsedLimit)
            ? Math.Clamp(parsedLimit, 1, 20)
            : 5;
        var escapedQuery = MemoryToolSupport.EscapeOqlLiteral(query);
        var pathPrefix = MemoryToolSupport.BuildPathPrefix(category);
        var escapedPathPrefix = MemoryToolSupport.EscapeOqlLiteral(pathPrefix);
        var escapedBrainId = MemoryToolSupport.EscapeOqlLiteral(brainResult.BrainId!);

        var oql = $"""
            FROM brain("{escapedBrainId}")
            WHERE path_prefix = "{escapedPathPrefix}"
            SEARCH "{escapedQuery}"
            RANK semantic
            LIMIT {limit}
            """;

        var results = await _queryExecutor.ExecuteAsync(oql, cancellationToken);
        var memories = new List<object>(results.Results.Count);

        foreach (var result in results.Results)
        {
            var document = await _documentStore.GetManagedDocumentByCanonicalPathAsync(
                customerId,
                brainResult.BrainId!,
                result.CanonicalPath,
                cancellationToken);

            string? resolvedCategory = null;
            string? confidence = null;
            string? tagsValue = null;
            document?.Frontmatter.TryGetValue("category", out resolvedCategory);
            document?.Frontmatter.TryGetValue("confidence", out confidence);
            document?.Frontmatter.TryGetValue("tags", out tagsValue);

            memories.Add(new
            {
                path = result.CanonicalPath,
                title = result.Title,
                content = result.Snippet ?? MemoryToolSupport.CreateSnippet(document?.Content),
                category = resolvedCategory ?? MemoryToolSupport.InferCategoryFromPath(result.CanonicalPath),
                confidence,
                tags = string.IsNullOrWhiteSpace(tagsValue)
                    ? Array.Empty<string>()
                    : tagsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                score = result.Score
            });
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            query,
            count = memories.Count,
            memories
        });
    }
}
