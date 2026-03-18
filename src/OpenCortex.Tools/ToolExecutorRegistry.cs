using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Tools;

/// <summary>
/// Registry-based tool executor that routes tool calls to registered handlers.
/// </summary>
public sealed class ToolExecutorRegistry : IToolExecutor
{
    private readonly Dictionary<string, IToolHandler> _handlers;
    private readonly Dictionary<string, ToolDefinition> _definitions;
    private readonly ILogger<ToolExecutorRegistry> _logger;

    public ToolExecutorRegistry(
        IEnumerable<IToolHandler> handlers,
        IEnumerable<IToolDefinitionProvider> definitionProviders,
        ILogger<ToolExecutorRegistry> logger)
    {
        _handlers = handlers.ToDictionary(
            h => h.ToolName,
            StringComparer.OrdinalIgnoreCase);

        _definitions = definitionProviders
            .SelectMany(p => p.GetToolDefinitions())
            .ToDictionary(
                d => d.Function.Name,
                StringComparer.OrdinalIgnoreCase);

        _logger = logger;

        _logger.LogInformation(
            "Tool executor initialized with {HandlerCount} handlers and {DefinitionCount} definitions",
            _handlers.Count,
            _definitions.Count);
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolCall toolCall,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!_handlers.TryGetValue(toolCall.Function.Name, out var handler))
        {
            _logger.LogWarning("Unknown tool: {ToolName}", toolCall.Function.Name);

            return ToolExecutionResult.Fail(
                toolCall.Id,
                toolCall.Function.Name,
                $"Unknown tool: {toolCall.Function.Name}",
                stopwatch.Elapsed);
        }

        try
        {
            _logger.LogDebug(
                "Executing tool {ToolName} for user {UserId}",
                toolCall.Function.Name,
                context.UserId);

            var arguments = JsonDocument.Parse(toolCall.Function.Arguments).RootElement;
            var output = await handler.ExecuteAsync(arguments, context, cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Tool {ToolName} completed in {Duration}ms. Output length: {OutputLength}",
                toolCall.Function.Name,
                stopwatch.ElapsedMilliseconds,
                output?.Length ?? 0);

            // Log first 500 chars of output for debugging
            if (!string.IsNullOrEmpty(output))
            {
                var preview = output.Length > 500 ? output[..500] + "..." : output;
                _logger.LogInformation("Tool {ToolName} output: {Output}", toolCall.Function.Name, preview);
            }

            return ToolExecutionResult.Ok(
                toolCall.Id,
                toolCall.Function.Name,
                output,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Tool {ToolName} failed after {Duration}ms",
                toolCall.Function.Name,
                stopwatch.ElapsedMilliseconds);

            return ToolExecutionResult.Fail(
                toolCall.Id,
                toolCall.Function.Name,
                ex.Message,
                stopwatch.Elapsed);
        }
    }

    public IReadOnlyList<ToolDefinition> GetAvailableTools(
        Guid userId,
        IEnumerable<string>? categories = null)
    {
        var categorySet = categories?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (categorySet is null || categorySet.Count == 0)
        {
            return _definitions.Values.ToList();
        }

        // Filter by category using handler metadata
        return _definitions.Values
            .Where(d => _handlers.TryGetValue(d.Function.Name, out var h)
                        && categorySet.Contains(h.Category))
            .ToList();
    }

    public IReadOnlyList<ToolDefinition> GetToolsByName(IEnumerable<string> toolNames)
    {
        return toolNames
            .Where(name => _definitions.ContainsKey(name))
            .Select(name => _definitions[name])
            .ToList();
    }

    public bool HasTool(string toolName)
    {
        return _handlers.ContainsKey(toolName);
    }
}
