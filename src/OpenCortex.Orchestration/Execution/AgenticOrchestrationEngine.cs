using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenCortex.Providers.Abstractions;
using OpenCortex.Tools;

namespace OpenCortex.Orchestration.Execution;

/// <summary>
/// Orchestration engine that executes tool loops autonomously.
/// </summary>
public sealed class AgenticOrchestrationEngine : IAgenticOrchestrationEngine
{
    private readonly IUserOrchestrationService _orchestration;
    private readonly IToolExecutor _toolExecutor;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<AgenticOrchestrationEngine> _logger;

    public AgenticOrchestrationEngine(
        IUserOrchestrationService orchestration,
        IToolExecutor toolExecutor,
        IWorkspaceManager workspaceManager,
        ILogger<AgenticOrchestrationEngine> logger)
    {
        _orchestration = orchestration;
        _toolExecutor = toolExecutor;
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    public async Task<AgenticOrchestrationResult> ExecuteAgenticAsync(
        AgenticOrchestrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var conversation = new List<ChatMessage>(request.Messages);
        var toolExecutions = new List<ToolExecutionResult>();
        var iteration = 0;
        var providerId = "";
        var modelId = "";

        // Get available tools
        var tools = GetTools(request);

        // Build execution context for tools
        var toolContext = await BuildToolContextAsync(request, cancellationToken);

        try
        {
            while (iteration < request.MaxIterations)
            {
                iteration++;
                _logger.LogDebug("Agentic iteration {Iteration}", iteration);

                // Build orchestration request
                var orchRequest = new OrchestrationRequest
                {
                    Messages = conversation,
                    Tools = tools.Count > 0 ? tools : null,
                    SystemMessage = request.SystemMessage,
                    MemoryContext = request.MemoryContext,
                    RoutingContext = request.RoutingContext,
                    Options = request.Options
                };

                // Execute LLM call
                var result = await _orchestration.ExecuteAsync(
                    request.UserId, orchRequest, cancellationToken);

                providerId = result.ProviderId;
                modelId = result.ModelId;

                // Check for tool calls
                if (result.Completion.ToolCalls is { Count: > 0 })
                {
                    _logger.LogInformation(
                        "Iteration {Iteration}: {ToolCount} tool calls",
                        iteration, result.Completion.ToolCalls.Count);

                    // Add assistant message with tool calls
                    conversation.Add(ChatMessage.AssistantToolCalls(result.Completion.ToolCalls));

                    // Execute each tool call
                    foreach (var toolCall in result.Completion.ToolCalls)
                    {
                        var toolResult = await _toolExecutor.ExecuteAsync(
                            toolCall, toolContext, cancellationToken);

                        toolExecutions.Add(toolResult);

                        // Add tool result to conversation
                        var resultContent = toolResult.Success
                            ? toolResult.Output ?? "{}"
                            : $"Error: {toolResult.Error}";

                        conversation.Add(ChatMessage.ToolResult(
                            toolResult.ToolCallId,
                            toolResult.ToolName,
                            resultContent));
                    }

                    // Continue to next iteration
                    continue;
                }

                // No tool calls - we're done
                if (!string.IsNullOrEmpty(result.Completion.Content))
                {
                    conversation.Add(ChatMessage.Assistant(result.Completion.Content));
                }

                stopwatch.Stop();
                return new AgenticOrchestrationResult
                {
                    Completion = result.Completion,
                    Conversation = conversation,
                    ToolExecutions = toolExecutions,
                    Iterations = iteration,
                    Duration = stopwatch.Elapsed,
                    ProviderId = providerId,
                    ModelId = modelId,
                    ReachedMaxIterations = false
                };
            }

            // Max iterations reached
            _logger.LogWarning("Max iterations ({Max}) reached", request.MaxIterations);
            stopwatch.Stop();

            return new AgenticOrchestrationResult
            {
                Completion = new ChatCompletion
                {
                    Content = "Maximum tool execution iterations reached.",
                    Usage = TokenUsage.Empty,
                    FinishReason = FinishReason.Length,
                    Model = modelId
                },
                Conversation = conversation,
                ToolExecutions = toolExecutions,
                Iterations = iteration,
                Duration = stopwatch.Elapsed,
                ProviderId = providerId,
                ModelId = modelId,
                ReachedMaxIterations = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agentic execution failed at iteration {Iteration}", iteration);
            stopwatch.Stop();

            return new AgenticOrchestrationResult
            {
                Completion = new ChatCompletion
                {
                    Content = null,
                    Usage = TokenUsage.Empty,
                    FinishReason = FinishReason.Other,
                    Model = modelId
                },
                Conversation = conversation,
                ToolExecutions = toolExecutions,
                Iterations = iteration,
                Duration = stopwatch.Elapsed,
                ProviderId = providerId,
                ModelId = modelId,
                ReachedMaxIterations = false,
                Error = ex.Message
            };
        }
    }

    public async IAsyncEnumerable<AgenticStreamEvent> StreamAgenticAsync(
        AgenticOrchestrationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var conversation = new List<ChatMessage>(request.Messages);
        var toolExecutions = new List<ToolExecutionResult>();
        var iteration = 0;
        var providerId = "";
        var modelId = "";

        // Get available tools
        var tools = GetTools(request);

        // Build execution context for tools
        var toolContext = await BuildToolContextAsync(request, cancellationToken);

        while (iteration < request.MaxIterations)
        {
            iteration++;
            _logger.LogDebug("Agentic streaming iteration {Iteration}", iteration);

            // Build orchestration request
            var orchRequest = new OrchestrationRequest
            {
                Messages = conversation,
                Tools = tools.Count > 0 ? tools : null,
                SystemMessage = request.SystemMessage,
                MemoryContext = request.MemoryContext,
                RoutingContext = request.RoutingContext,
                Options = request.Options
            };

            // Stream from LLM
            var contentBuffer = new System.Text.StringBuilder();
            var toolCallAccumulator = new ToolCallAccumulator();
            var isFirstChunk = true;

            await foreach (var chunk in _orchestration.StreamAsync(
                request.UserId, orchRequest, cancellationToken))
            {
                if (isFirstChunk)
                {
                    providerId = chunk.ProviderId;
                    isFirstChunk = false;
                }

                // Handle content delta
                if (!string.IsNullOrEmpty(chunk.Chunk.ContentDelta))
                {
                    contentBuffer.Append(chunk.Chunk.ContentDelta);
                    yield return new AgenticTextEvent
                    {
                        Content = chunk.Chunk.ContentDelta,
                        Iteration = iteration
                    };
                }

                // Collect tool call deltas
                if (chunk.Chunk.ToolCallDelta is not null)
                {
                    toolCallAccumulator.AddDelta(chunk.Chunk.ToolCallDelta);
                }

                // Get model ID from completion
                if (chunk.Chunk.IsComplete && !string.IsNullOrEmpty(chunk.Chunk.Model))
                {
                    modelId = chunk.Chunk.Model;
                }
            }

            // Build completed tool calls
            var toolCalls = toolCallAccumulator.Build();

            // Check for tool calls
            if (toolCalls.Count > 0)
            {
                _logger.LogInformation(
                    "Streaming iteration {Iteration}: {ToolCount} tool calls",
                    iteration, toolCalls.Count);

                yield return new AgenticToolCallStartEvent
                {
                    ToolCalls = toolCalls,
                    Iteration = iteration
                };

                // Add assistant message with tool calls
                conversation.Add(ChatMessage.AssistantToolCalls(toolCalls));

                // Execute each tool call
                foreach (var toolCall in toolCalls)
                {
                    var toolResult = await _toolExecutor.ExecuteAsync(
                        toolCall, toolContext, cancellationToken);

                    toolExecutions.Add(toolResult);

                    yield return new AgenticToolResultEvent
                    {
                        Result = toolResult,
                        Iteration = iteration
                    };

                    // Add tool result to conversation
                    var resultContent = toolResult.Success
                        ? toolResult.Output ?? "{}"
                        : $"Error: {toolResult.Error}";

                    conversation.Add(ChatMessage.ToolResult(
                        toolResult.ToolCallId,
                        toolResult.ToolName,
                        resultContent));
                }

                // Continue to next iteration
                continue;
            }

            // No tool calls - we're done
            var content = contentBuffer.ToString();
            if (!string.IsNullOrEmpty(content))
            {
                conversation.Add(ChatMessage.Assistant(content));
            }

            stopwatch.Stop();
            yield return new AgenticCompleteEvent
            {
                Result = new AgenticOrchestrationResult
                {
                    Completion = new ChatCompletion
                    {
                        Content = content,
                        Usage = TokenUsage.Empty,
                        FinishReason = FinishReason.Stop,
                        Model = modelId
                    },
                    Conversation = conversation,
                    ToolExecutions = toolExecutions,
                    Iterations = iteration,
                    Duration = stopwatch.Elapsed,
                    ProviderId = providerId,
                    ModelId = modelId,
                    ReachedMaxIterations = false
                }
            };
            yield break;
        }

        // Max iterations reached
        _logger.LogWarning("Max iterations ({Max}) reached during streaming", request.MaxIterations);
        stopwatch.Stop();

        yield return new AgenticCompleteEvent
        {
            Result = new AgenticOrchestrationResult
            {
                Completion = new ChatCompletion
                {
                    Content = "Maximum tool execution iterations reached.",
                    Usage = TokenUsage.Empty,
                    FinishReason = FinishReason.Length,
                    Model = modelId
                },
                Conversation = conversation,
                ToolExecutions = toolExecutions,
                Iterations = iteration,
                Duration = stopwatch.Elapsed,
                ProviderId = providerId,
                ModelId = modelId,
                ReachedMaxIterations = true
            }
        };
    }

    private IReadOnlyList<ToolDefinition> GetTools(AgenticOrchestrationRequest request)
    {
        if (request.EnabledTools is { Count: > 0 })
        {
            return _toolExecutor.GetToolsByName(request.EnabledTools);
        }

        return _toolExecutor.GetAvailableTools(
            request.UserId,
            request.EnabledCategories);
    }

    private async Task<ToolExecutionContext> BuildToolContextAsync(
        AgenticOrchestrationRequest request,
        CancellationToken cancellationToken)
    {
        var workspacePath = await _workspaceManager.GetWorkspacePathAsync(
            request.UserId, cancellationToken);

        return new ToolExecutionContext
        {
            UserId = request.UserId,
            CustomerId = request.CustomerId,
            ConversationId = request.ConversationId,
            WorkspacePath = workspacePath,
            Credentials = request.Credentials,
            CommandMode = request.CommandMode
        };
    }

    /// <summary>
    /// Helper to accumulate streaming tool call deltas into complete tool calls.
    /// </summary>
    private sealed class ToolCallAccumulator
    {
        private readonly Dictionary<int, (string? Id, string? Name, System.Text.StringBuilder Args)> _calls = new();

        public void AddDelta(ToolCallDelta delta)
        {
            if (!_calls.TryGetValue(delta.Index, out var existing))
            {
                existing = (null, null, new System.Text.StringBuilder());
                _calls[delta.Index] = existing;
            }

            // Update ID and name if provided
            if (!string.IsNullOrEmpty(delta.Id))
            {
                existing = existing with { Id = delta.Id };
                _calls[delta.Index] = existing;
            }

            if (!string.IsNullOrEmpty(delta.FunctionName))
            {
                existing = existing with { Name = delta.FunctionName };
                _calls[delta.Index] = existing;
            }

            // Append arguments
            if (!string.IsNullOrEmpty(delta.ArgumentsDelta))
            {
                existing.Args.Append(delta.ArgumentsDelta);
            }
        }

        public List<ToolCall> Build()
        {
            return _calls
                .OrderBy(kvp => kvp.Key)
                .Where(kvp => !string.IsNullOrEmpty(kvp.Value.Id) && !string.IsNullOrEmpty(kvp.Value.Name))
                .Select(kvp => new ToolCall
                {
                    Id = kvp.Value.Id!,
                    Function = new FunctionCall
                    {
                        Name = kvp.Value.Name!,
                        Arguments = kvp.Value.Args.ToString()
                    }
                })
                .ToList();
        }
    }
}
