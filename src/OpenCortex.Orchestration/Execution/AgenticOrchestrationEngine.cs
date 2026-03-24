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
        var routedProviderId = await ResolveRequestedOrRoutedProviderIdAsync(request, cancellationToken);
        if (IsCodexNativeProvider(routedProviderId))
        {
            return await ExecuteCodexNativeAgenticAsync(request, cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();
        var telemetryBuilder = new AgenticTelemetryBuilder();
        var conversation = new List<ChatMessage>(request.Messages);
        var toolExecutions = new List<ToolExecutionResult>();
        var iteration = 0;
        var providerId = "";
        var modelId = "";

        _logger.LogInformation(
            "Starting agentic execution. TraceId={TraceId}, MaxIterations={MaxIterations}",
            telemetryBuilder.TraceId, request.MaxIterations);

        // Get available tools
        var tools = GetTools(request);

        // Build execution context for tools
        var toolContext = await BuildToolContextAsync(request, cancellationToken);

        try
        {
            while (iteration < request.MaxIterations)
            {
                iteration++;
                var iterationStartedAt = DateTimeOffset.UtcNow;
                var iterationStopwatch = Stopwatch.StartNew();

                _logger.LogDebug(
                    "Agentic iteration {Iteration}. TraceId={TraceId}",
                    iteration, telemetryBuilder.TraceId);

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
                var llmStopwatch = Stopwatch.StartNew();
                var result = await _orchestration.ExecuteAsync(
                    request.CustomerId,
                    request.UserId, orchRequest, cancellationToken);
                llmStopwatch.Stop();

                providerId = result.ProviderId;
                modelId = result.ModelId;
                telemetryBuilder.SetProvider(providerId, modelId);

                // Track tool execution time for this iteration
                var toolDuration = TimeSpan.Zero;
                var toolCallNames = new List<string>();

                // Check for tool calls
                if (result.Completion.ToolCalls is { Count: > 0 })
                {
                    _logger.LogInformation(
                        "Iteration {Iteration}: {ToolCount} tool calls. TraceId={TraceId}",
                        iteration, result.Completion.ToolCalls.Count, telemetryBuilder.TraceId);

                    // Add assistant message with tool calls
                    conversation.Add(ChatMessage.AssistantToolCalls(result.Completion.ToolCalls));

                    // Execute each tool call
                    foreach (var toolCall in result.Completion.ToolCalls)
                    {
                        var toolStartedAt = DateTimeOffset.UtcNow;
                        var toolStopwatch = Stopwatch.StartNew();

                        var toolResult = await _toolExecutor.ExecuteAsync(
                            toolCall, toolContext, cancellationToken);

                        toolStopwatch.Stop();
                        toolDuration += toolStopwatch.Elapsed;
                        toolExecutions.Add(toolResult);
                        toolCallNames.Add(toolCall.Function.Name);

                        // Record tool telemetry
                        telemetryBuilder.RecordToolExecution(
                            toolResult,
                            iteration,
                            toolStartedAt,
                            toolStopwatch.Elapsed,
                            GetToolCategory(toolCall.Function.Name));

                        // Add tool result to conversation
                        var resultContent = toolResult.Success
                            ? toolResult.Output ?? "{}"
                            : $"Error: {toolResult.Error}";

                        conversation.Add(ChatMessage.ToolResult(
                            toolResult.ToolCallId,
                            toolResult.ToolName,
                            resultContent));
                    }

                    // Record iteration telemetry
                    telemetryBuilder.RecordIteration(
                        iteration,
                        iterationStartedAt,
                        llmStopwatch.Elapsed,
                        toolDuration,
                        result.Completion.Usage,
                        toolCallNames,
                        result.Completion.FinishReason.ToString());

                    // Continue to next iteration
                    continue;
                }

                // No tool calls - we're done
                if (!string.IsNullOrEmpty(result.Completion.Content))
                {
                    conversation.Add(ChatMessage.Assistant(result.Completion.Content));
                }

                // Record final iteration telemetry
                telemetryBuilder.RecordIteration(
                    iteration,
                    iterationStartedAt,
                    llmStopwatch.Elapsed,
                    TimeSpan.Zero,
                    result.Completion.Usage,
                    Array.Empty<string>(),
                    result.Completion.FinishReason.ToString(),
                    result.Completion.Content?.Length);

                stopwatch.Stop();
                var telemetry = telemetryBuilder.Build();

                _logger.LogInformation(
                    "Agentic execution completed. TraceId={TraceId}, Iterations={Iterations}, " +
                    "TotalTokens={TotalTokens}, ToolCalls={ToolCalls}, Duration={Duration}ms",
                    telemetry.TraceId, iteration, telemetry.TokenUsage.TotalTokens,
                    telemetry.ToolCallCount, telemetry.TotalDuration.TotalMilliseconds);

                return new AgenticOrchestrationResult
                {
                    Completion = result.Completion,
                    Conversation = conversation,
                    ToolExecutions = toolExecutions,
                    Iterations = iteration,
                    Duration = stopwatch.Elapsed,
                    ProviderId = providerId,
                    ModelId = modelId,
                    ReachedMaxIterations = false,
                    Telemetry = telemetry
                };
            }

            // Max iterations reached
            telemetryBuilder.SetReachedMaxIterations();
            _logger.LogWarning(
                "Max iterations ({Max}) reached. TraceId={TraceId}",
                request.MaxIterations, telemetryBuilder.TraceId);

            stopwatch.Stop();
            var maxIterTelemetry = telemetryBuilder.Build();

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
                ReachedMaxIterations = true,
                Telemetry = maxIterTelemetry
            };
        }
        catch (Exception ex)
        {
            var safeError = ErrorRedaction.Sanitize(
                "Agentic execution failed.",
                ex.Message,
                request.Credentials);
            telemetryBuilder.SetError(safeError);
            _logger.LogError(ex,
                "Agentic execution failed at iteration {Iteration}. TraceId={TraceId}",
                iteration, telemetryBuilder.TraceId);

            stopwatch.Stop();
            var errorTelemetry = telemetryBuilder.Build();

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
                Error = safeError,
                Telemetry = errorTelemetry
            };
        }
    }

    public async IAsyncEnumerable<AgenticStreamEvent> StreamAgenticAsync(
        AgenticOrchestrationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var routedProviderId = await ResolveRequestedOrRoutedProviderIdAsync(request, cancellationToken);
        if (IsCodexNativeProvider(routedProviderId))
        {
            await foreach (var codexEvent in StreamCodexNativeAgenticAsync(request, cancellationToken))
            {
                yield return codexEvent;
            }

            yield break;
        }

        var stopwatch = Stopwatch.StartNew();
        var telemetryBuilder = new AgenticTelemetryBuilder();
        var conversation = new List<ChatMessage>(request.Messages);
        var toolExecutions = new List<ToolExecutionResult>();
        var iteration = 0;
        var providerId = "";
        var modelId = "";

        _logger.LogInformation(
            "Starting agentic streaming. TraceId={TraceId}, MaxIterations={MaxIterations}",
            telemetryBuilder.TraceId, request.MaxIterations);

        // Get available tools
        var tools = GetTools(request);

        // Ensure workspace is ready (emit events for container provisioning)
        var workspaceEvents = await EnsureWorkspaceWithEventsAsync(
            request, telemetryBuilder.TraceId, cancellationToken);

        foreach (var wsEvent in workspaceEvents)
        {
            yield return wsEvent;

            // If workspace failed, stop execution
            if (wsEvent is AgenticWorkspaceErrorEvent errorEvent)
            {
                yield return new AgenticErrorEvent
                {
                    Error = errorEvent.Error,
                    Iteration = 0,
                    TraceId = telemetryBuilder.TraceId
                };
                yield break;
            }
        }

        // Build execution context for tools (workspace should be ready now)
        var toolContext = await BuildToolContextAsync(request, cancellationToken);

        while (iteration < request.MaxIterations)
        {
            iteration++;
            var iterationStartedAt = DateTimeOffset.UtcNow;
            var llmStopwatch = Stopwatch.StartNew();

            _logger.LogDebug(
                "Agentic streaming iteration {Iteration}. TraceId={TraceId}",
                iteration, telemetryBuilder.TraceId);

            // Emit iteration start event
            yield return new AgenticIterationStartEvent
            {
                Iteration = iteration,
                TraceId = telemetryBuilder.TraceId
            };

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
            TokenUsage iterationUsage = TokenUsage.Empty;

            await foreach (var chunk in _orchestration.StreamAsync(
                request.CustomerId,
                request.UserId, orchRequest, cancellationToken))
            {
                if (isFirstChunk)
                {
                    providerId = chunk.ProviderId;
                    telemetryBuilder.SetProvider(providerId, modelId);
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

                // Get model ID and usage from completion
                if (chunk.Chunk.IsComplete)
                {
                    if (!string.IsNullOrEmpty(chunk.Chunk.Model))
                    {
                        modelId = chunk.Chunk.Model;
                        telemetryBuilder.SetProvider(providerId, modelId);
                    }
                    if (chunk.Chunk.FinalUsage is not null)
                    {
                        iterationUsage = chunk.Chunk.FinalUsage;
                    }
                }
            }

            llmStopwatch.Stop();

            // Build completed tool calls
            var toolCalls = toolCallAccumulator.Build();
            var toolCallNames = toolCalls.Select(tc => tc.Function.Name).ToList();

            // Check for tool calls
            if (toolCalls.Count > 0)
            {
                _logger.LogInformation(
                    "Streaming iteration {Iteration}: {ToolCount} tool calls. TraceId={TraceId}",
                    iteration, toolCalls.Count, telemetryBuilder.TraceId);

                yield return new AgenticToolCallStartEvent
                {
                    ToolCalls = toolCalls,
                    Iteration = iteration
                };

                // Add assistant message with tool calls
                conversation.Add(ChatMessage.AssistantToolCalls(toolCalls));

                var toolDuration = TimeSpan.Zero;

                // Execute each tool call
                foreach (var toolCall in toolCalls)
                {
                    var toolStartedAt = DateTimeOffset.UtcNow;
                    var toolStopwatch = Stopwatch.StartNew();

                    var toolResult = await _toolExecutor.ExecuteAsync(
                        toolCall, toolContext, cancellationToken);

                    toolStopwatch.Stop();
                    toolDuration += toolStopwatch.Elapsed;
                    toolExecutions.Add(toolResult);

                    // Record tool telemetry
                    telemetryBuilder.RecordToolExecution(
                        toolResult,
                        iteration,
                        toolStartedAt,
                        toolStopwatch.Elapsed,
                        GetToolCategory(toolCall.Function.Name));

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

                // Record iteration telemetry
                telemetryBuilder.RecordIteration(
                    iteration,
                    iterationStartedAt,
                    llmStopwatch.Elapsed,
                    toolDuration,
                    iterationUsage,
                    toolCallNames);

                // Emit iteration complete event
                yield return new AgenticIterationCompleteEvent
                {
                    Iteration = iteration,
                    Duration = llmStopwatch.Elapsed + toolDuration,
                    TokenUsage = iterationUsage,
                    HasToolCalls = true
                };

                // Continue to next iteration
                continue;
            }

            // No tool calls - we're done
            var content = contentBuffer.ToString();
            if (!string.IsNullOrEmpty(content))
            {
                conversation.Add(ChatMessage.Assistant(content));
            }

            // Record final iteration telemetry
            telemetryBuilder.RecordIteration(
                iteration,
                iterationStartedAt,
                llmStopwatch.Elapsed,
                TimeSpan.Zero,
                iterationUsage,
                Array.Empty<string>(),
                null,
                content?.Length);

            stopwatch.Stop();
            var telemetry = telemetryBuilder.Build();

            _logger.LogInformation(
                "Agentic streaming completed. TraceId={TraceId}, Iterations={Iterations}, " +
                "TotalTokens={TotalTokens}, ToolCalls={ToolCalls}, Duration={Duration}ms",
                telemetry.TraceId, iteration, telemetry.TokenUsage.TotalTokens,
                telemetry.ToolCallCount, telemetry.TotalDuration.TotalMilliseconds);

            yield return new AgenticIterationCompleteEvent
            {
                Iteration = iteration,
                Duration = llmStopwatch.Elapsed,
                TokenUsage = iterationUsage,
                HasToolCalls = false
            };

            yield return new AgenticCompleteEvent
            {
                Result = new AgenticOrchestrationResult
                {
                    Completion = new ChatCompletion
                    {
                        Content = content,
                        Usage = iterationUsage,
                        FinishReason = FinishReason.Stop,
                        Model = modelId
                    },
                    Conversation = conversation,
                    ToolExecutions = toolExecutions,
                    Iterations = iteration,
                    Duration = stopwatch.Elapsed,
                    ProviderId = providerId,
                    ModelId = modelId,
                    ReachedMaxIterations = false,
                    Telemetry = telemetry
                }
            };
            yield break;
        }

        // Max iterations reached
        telemetryBuilder.SetReachedMaxIterations();
        _logger.LogWarning(
            "Max iterations ({Max}) reached during streaming. TraceId={TraceId}",
            request.MaxIterations, telemetryBuilder.TraceId);

        stopwatch.Stop();
        var maxIterTelemetry = telemetryBuilder.Build();

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
                ReachedMaxIterations = true,
                Telemetry = maxIterTelemetry
            }
        };
    }

    private async Task<AgenticOrchestrationResult> ExecuteCodexNativeAgenticAsync(
        AgenticOrchestrationRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var telemetryBuilder = new AgenticTelemetryBuilder();
        var conversation = new List<ChatMessage>(request.Messages);
        var iterationStartedAt = DateTimeOffset.UtcNow;

        try
        {
            var orchRequest = BuildCodexNativeOrchestrationRequest(request);
            var llmStopwatch = Stopwatch.StartNew();
            var result = await _orchestration.ExecuteAsync(
                request.CustomerId,
                request.UserId,
                orchRequest,
                cancellationToken);
            llmStopwatch.Stop();

            telemetryBuilder.SetProvider(result.ProviderId, result.ModelId);

            if (!string.IsNullOrWhiteSpace(result.Completion.Content))
            {
                conversation.Add(ChatMessage.Assistant(result.Completion.Content));
            }

            telemetryBuilder.RecordIteration(
                1,
                iterationStartedAt,
                llmStopwatch.Elapsed,
                TimeSpan.Zero,
                result.Completion.Usage,
                Array.Empty<string>(),
                result.Completion.FinishReason.ToString(),
                result.Completion.Content?.Length);

            stopwatch.Stop();
            var telemetry = telemetryBuilder.Build();

            return new AgenticOrchestrationResult
            {
                Completion = result.Completion,
                Conversation = conversation,
                ToolExecutions = Array.Empty<ToolExecutionResult>(),
                Iterations = 1,
                Duration = stopwatch.Elapsed,
                ProviderId = result.ProviderId,
                ModelId = result.ModelId,
                ReachedMaxIterations = false,
                Telemetry = telemetry
            };
        }
        catch (Exception ex)
        {
            var safeError = ErrorRedaction.Sanitize(
                "Codex agentic execution failed.",
                ex.Message,
                request.Credentials);
            telemetryBuilder.SetError(safeError);
            stopwatch.Stop();

            return new AgenticOrchestrationResult
            {
                Completion = new ChatCompletion
                {
                    Content = null,
                    Usage = TokenUsage.Empty,
                    FinishReason = FinishReason.Other,
                    Model = string.Empty
                },
                Conversation = conversation,
                ToolExecutions = Array.Empty<ToolExecutionResult>(),
                Iterations = 1,
                Duration = stopwatch.Elapsed,
                ProviderId = "openai-codex",
                ModelId = request.RoutingContext?.RequestedModelId ?? string.Empty,
                ReachedMaxIterations = false,
                Error = safeError,
                Telemetry = telemetryBuilder.Build()
            };
        }
    }

    private async IAsyncEnumerable<AgenticStreamEvent> StreamCodexNativeAgenticAsync(
        AgenticOrchestrationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var telemetryBuilder = new AgenticTelemetryBuilder();
        var conversation = new List<ChatMessage>(request.Messages);
        var iterationStartedAt = DateTimeOffset.UtcNow;
        var contentBuffer = new System.Text.StringBuilder();
        var providerId = "openai-codex";
        var modelId = request.RoutingContext?.RequestedModelId ?? string.Empty;
        var usage = TokenUsage.Empty;

        _logger.LogInformation(
            "Starting Codex-native agentic streaming. TraceId={TraceId}",
            telemetryBuilder.TraceId);

        var workspaceEvents = await EnsureWorkspaceWithEventsAsync(
            request, telemetryBuilder.TraceId, cancellationToken);

        foreach (var wsEvent in workspaceEvents)
        {
            yield return wsEvent;

            if (wsEvent is AgenticWorkspaceErrorEvent errorEvent)
            {
                yield return new AgenticErrorEvent
                {
                    Error = errorEvent.Error,
                    Iteration = 0,
                    TraceId = telemetryBuilder.TraceId
                };
                yield break;
            }
        }

        yield return new AgenticIterationStartEvent
        {
            Iteration = 1,
            TraceId = telemetryBuilder.TraceId
        };

        var orchRequest = BuildCodexNativeOrchestrationRequest(request);
        var llmStopwatch = Stopwatch.StartNew();

        await foreach (var chunk in _orchestration.StreamAsync(
            request.CustomerId,
            request.UserId,
            orchRequest,
            cancellationToken))
        {
            providerId = chunk.ProviderId;

            if (!string.IsNullOrWhiteSpace(chunk.Chunk.Model))
            {
                modelId = chunk.Chunk.Model;
                telemetryBuilder.SetProvider(providerId, modelId);
            }

            if (!string.IsNullOrEmpty(chunk.Chunk.ContentDelta))
            {
                contentBuffer.Append(chunk.Chunk.ContentDelta);
                yield return new AgenticTextEvent
                {
                    Content = chunk.Chunk.ContentDelta,
                    Iteration = 1
                };
            }

            if (chunk.Chunk.IsComplete && chunk.Chunk.FinalUsage is not null)
            {
                usage = chunk.Chunk.FinalUsage;
            }
        }

        llmStopwatch.Stop();

        var content = contentBuffer.ToString();
        if (!string.IsNullOrWhiteSpace(content))
        {
            conversation.Add(ChatMessage.Assistant(content));
        }

        telemetryBuilder.SetProvider(providerId, modelId);
        telemetryBuilder.RecordIteration(
            1,
            iterationStartedAt,
            llmStopwatch.Elapsed,
            TimeSpan.Zero,
            usage,
            Array.Empty<string>(),
            FinishReason.Stop.ToString(),
            content.Length);

        stopwatch.Stop();
        var telemetry = telemetryBuilder.Build();

        yield return new AgenticIterationCompleteEvent
        {
            Iteration = 1,
            Duration = llmStopwatch.Elapsed,
            TokenUsage = usage,
            HasToolCalls = false
        };

        yield return new AgenticCompleteEvent
        {
            Result = new AgenticOrchestrationResult
            {
                Completion = new ChatCompletion
                {
                    Content = content,
                    Usage = usage,
                    FinishReason = FinishReason.Stop,
                    Model = modelId
                },
                Conversation = conversation,
                ToolExecutions = Array.Empty<ToolExecutionResult>(),
                Iterations = 1,
                Duration = stopwatch.Elapsed,
                ProviderId = providerId,
                ModelId = modelId,
                ReachedMaxIterations = false,
                Telemetry = telemetry
            }
        };
    }

    private static bool IsCodexNativeProvider(string? providerId) =>
        string.Equals(providerId?.Trim(), "openai-codex", StringComparison.OrdinalIgnoreCase);

    private async Task<string?> ResolveRequestedOrRoutedProviderIdAsync(
        AgenticOrchestrationRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RoutingContext?.RequestedProviderId))
        {
            return request.RoutingContext.RequestedProviderId;
        }

        var userMessage = request.Messages.LastOrDefault(m => m.Role == ChatRole.User)?.Content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return null;
        }

        var routing = await _orchestration.RouteAsync(
            request.CustomerId,
            request.UserId,
            userMessage,
            request.RoutingContext,
            cancellationToken);

        return routing.ProviderId;
    }

    private static OrchestrationRequest BuildCodexNativeOrchestrationRequest(
        AgenticOrchestrationRequest request)
    {
        var routingContext = request.RoutingContext is null
            ? new Routing.RoutingContext
            {
                RequestedProviderId = "openai-codex"
            }
            : request.RoutingContext with
            {
                RequestedProviderId = "openai-codex"
            };

        return new OrchestrationRequest
        {
            Messages = request.Messages,
            Tools = null,
            SystemMessage = BuildCodexNativeSystemMessage(request.SystemMessage),
            MemoryContext = request.MemoryContext,
            RoutingContext = routingContext,
            Options = request.Options
        };
    }

    private static string BuildCodexNativeSystemMessage(string? existingSystemMessage)
    {
        const string codexAgenticInstruction =
            "You are operating in OpenCortex agentic mode backed by Codex. " +
            "Work autonomously inside the workspace when needed, including inspecting files and running commands. " +
            "Do not emit OpenCortex tool calls. Use the workspace directly and return a concise final response describing what you did and the result.";

        return string.IsNullOrWhiteSpace(existingSystemMessage)
            ? codexAgenticInstruction
            : $"{existingSystemMessage.Trim()}\n\n{codexAgenticInstruction}";
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
        // Ensure workspace is running (for container-based managers)
        var workspaceStatus = await _workspaceManager.EnsureRunningAsync(
            request.UserId, request.Credentials, cancellationToken);

        var workspacePath = workspaceStatus.WorkspacePath
            ?? await _workspaceManager.GetWorkspacePathAsync(request.UserId, cancellationToken);

        return new ToolExecutionContext
        {
            UserId = request.UserId,
            CustomerId = request.CustomerId,
            ConversationId = request.ConversationId,
            TenantUserId = request.RoutingContext?.UserId,
            TenantCustomerId = request.RoutingContext?.CustomerId,
            BrainId = request.RoutingContext?.BrainId,
            WorkspacePath = workspacePath,
            Credentials = request.Credentials,
            CommandMode = request.CommandMode
        };
    }

    private async Task<List<AgenticStreamEvent>> EnsureWorkspaceWithEventsAsync(
        AgenticOrchestrationRequest request,
        string traceId,
        CancellationToken cancellationToken)
    {
        var events = new List<AgenticStreamEvent>();

        // Check if workspace manager supports container isolation
        if (!_workspaceManager.SupportsContainerIsolation)
        {
            return events;
        }

        // Check current status
        var status = await _workspaceManager.GetStatusAsync(request.UserId, cancellationToken);

        if (status.State == WorkspaceState.Running)
        {
            // Already running, no events needed
            return events;
        }

        // Emit provisioning event
        events.Add(new AgenticWorkspaceProvisioningEvent
        {
            Status = "initializing",
            Message = "Initializing your workspace...",
            TraceId = traceId
        });

        var provisioningStopwatch = Stopwatch.StartNew();

        try
        {
            // Start provisioning
            events.Add(new AgenticWorkspaceProvisioningEvent
            {
                Status = "creating",
                Message = "Creating workspace container...",
                TraceId = traceId
            });

            var workspaceStatus = await _workspaceManager.EnsureRunningAsync(
                request.UserId, request.Credentials, cancellationToken);

            provisioningStopwatch.Stop();

            if (workspaceStatus.State == WorkspaceState.Running)
            {
                _logger.LogInformation(
                    "Workspace ready for user {UserId}. Pod={PodName}, Container={ContainerId}, " +
                    "StartupTime={StartupMs}ms, TraceId={TraceId}",
                    request.UserId, workspaceStatus.PodName, workspaceStatus.ContainerId,
                    provisioningStopwatch.ElapsedMilliseconds, traceId);

                events.Add(new AgenticWorkspaceReadyEvent
                {
                    PodName = workspaceStatus.PodName,
                    ContainerId = workspaceStatus.ContainerId,
                    StartupDuration = provisioningStopwatch.Elapsed,
                    TraceId = traceId
                });
            }
            else if (workspaceStatus.State == WorkspaceState.Failed)
            {
                _logger.LogError(
                    "Workspace provisioning failed for user {UserId}: {Message}. TraceId={TraceId}",
                    request.UserId, workspaceStatus.Message, traceId);
                var safeWorkspaceError = ErrorRedaction.Sanitize(
                    "Workspace provisioning failed.",
                    workspaceStatus.Message,
                    request.Credentials);

                events.Add(new AgenticWorkspaceErrorEvent
                {
                    Error = safeWorkspaceError,
                    Retryable = true,
                    TraceId = traceId
                });
            }
        }
        catch (Exception ex)
        {
            provisioningStopwatch.Stop();

            _logger.LogError(ex,
                "Workspace provisioning exception for user {UserId}. TraceId={TraceId}",
                request.UserId, traceId);
            var safeProvisioningError = ErrorRedaction.Sanitize(
                "Failed to provision workspace.",
                ex.Message,
                request.Credentials);

            events.Add(new AgenticWorkspaceErrorEvent
            {
                Error = safeProvisioningError,
                Retryable = true,
                TraceId = traceId
            });
        }

        return events;
    }

    private static string? GetToolCategory(string toolName)
    {
        // Map tool names to categories for telemetry
        if (toolName.StartsWith("github_", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("repository", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("pull_request", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("branch", StringComparison.OrdinalIgnoreCase))
        {
            return "github";
        }

        if (toolName.StartsWith("read_", StringComparison.OrdinalIgnoreCase) ||
            toolName.StartsWith("write_", StringComparison.OrdinalIgnoreCase) ||
            toolName.StartsWith("list_", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("file", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("directory", StringComparison.OrdinalIgnoreCase))
        {
            return "filesystem";
        }

        if (toolName.StartsWith("execute_", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("command", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("shell", StringComparison.OrdinalIgnoreCase))
        {
            return "shell";
        }

        if (string.Equals(toolName, "save_memory", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(toolName, "recall_memories", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(toolName, "forget_memory", StringComparison.OrdinalIgnoreCase))
        {
            return "memory";
        }

        return null;
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

