using OpenCortex.Providers.Abstractions;
using OpenCortex.Tools;

namespace OpenCortex.Orchestration.Execution;

/// <summary>
/// Complete telemetry data for an agentic execution session.
/// Provides token usage, timing, and audit trail for observability.
/// </summary>
public sealed record AgenticTelemetry
{
    /// <summary>
    /// Unique trace ID for this execution.
    /// </summary>
    public required string TraceId { get; init; }

    /// <summary>
    /// Span ID for distributed tracing.
    /// </summary>
    public string? SpanId { get; init; }

    /// <summary>
    /// Parent span ID if this is a child span.
    /// </summary>
    public string? ParentSpanId { get; init; }

    /// <summary>
    /// When the execution started.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// When the execution completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Total duration of the execution.
    /// </summary>
    public required TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Time spent waiting for LLM responses.
    /// </summary>
    public required TimeSpan LlmDuration { get; init; }

    /// <summary>
    /// Time spent executing tools.
    /// </summary>
    public required TimeSpan ToolDuration { get; init; }

    /// <summary>
    /// Aggregated token usage across all iterations.
    /// </summary>
    public required AgenticTokenUsage TokenUsage { get; init; }

    /// <summary>
    /// Per-iteration telemetry data.
    /// </summary>
    public required IReadOnlyList<IterationTelemetry> Iterations { get; init; }

    /// <summary>
    /// Per-tool execution telemetry.
    /// </summary>
    public required IReadOnlyList<ToolExecutionTelemetry> ToolExecutions { get; init; }

    /// <summary>
    /// Number of LLM calls made.
    /// </summary>
    public required int LlmCallCount { get; init; }

    /// <summary>
    /// Number of tool calls executed.
    /// </summary>
    public required int ToolCallCount { get; init; }

    /// <summary>
    /// Provider used for LLM calls.
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// Model used for LLM calls.
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// Whether the execution completed successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Whether max iterations was reached.
    /// </summary>
    public required bool ReachedMaxIterations { get; init; }

    /// <summary>
    /// Custom metadata for the trace.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Aggregated token usage across all iterations.
/// </summary>
public sealed record AgenticTokenUsage
{
    /// <summary>
    /// Total prompt tokens across all LLM calls.
    /// </summary>
    public required int TotalPromptTokens { get; init; }

    /// <summary>
    /// Total completion tokens across all LLM calls.
    /// </summary>
    public required int TotalCompletionTokens { get; init; }

    /// <summary>
    /// Total tokens (prompt + completion).
    /// </summary>
    public int TotalTokens => TotalPromptTokens + TotalCompletionTokens;

    /// <summary>
    /// Per-iteration token breakdown.
    /// </summary>
    public required IReadOnlyList<IterationTokenUsage> ByIteration { get; init; }

    /// <summary>
    /// Empty token usage.
    /// </summary>
    public static AgenticTokenUsage Empty => new()
    {
        TotalPromptTokens = 0,
        TotalCompletionTokens = 0,
        ByIteration = Array.Empty<IterationTokenUsage>()
    };
}

/// <summary>
/// Token usage for a single iteration.
/// </summary>
public sealed record IterationTokenUsage
{
    /// <summary>
    /// Iteration number (1-based).
    /// </summary>
    public required int Iteration { get; init; }

    /// <summary>
    /// Prompt tokens for this iteration.
    /// </summary>
    public required int PromptTokens { get; init; }

    /// <summary>
    /// Completion tokens for this iteration.
    /// </summary>
    public required int CompletionTokens { get; init; }

    /// <summary>
    /// Total tokens for this iteration.
    /// </summary>
    public int TotalTokens => PromptTokens + CompletionTokens;

    /// <summary>
    /// Model used for this iteration.
    /// </summary>
    public string? Model { get; init; }
}

/// <summary>
/// Telemetry for a single agentic iteration.
/// </summary>
public sealed record IterationTelemetry
{
    /// <summary>
    /// Iteration number (1-based).
    /// </summary>
    public required int Iteration { get; init; }

    /// <summary>
    /// When this iteration started.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Duration of the LLM call.
    /// </summary>
    public required TimeSpan LlmDuration { get; init; }

    /// <summary>
    /// Duration of tool executions in this iteration.
    /// </summary>
    public required TimeSpan ToolDuration { get; init; }

    /// <summary>
    /// Total duration of this iteration.
    /// </summary>
    public required TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Token usage for the LLM call.
    /// </summary>
    public required TokenUsage TokenUsage { get; init; }

    /// <summary>
    /// Tool calls made in this iteration.
    /// </summary>
    public required IReadOnlyList<string> ToolCallNames { get; init; }

    /// <summary>
    /// Whether this iteration had tool calls.
    /// </summary>
    public required bool HasToolCalls { get; init; }

    /// <summary>
    /// Text content length generated (if any).
    /// </summary>
    public int? ContentLength { get; init; }

    /// <summary>
    /// Finish reason for the LLM call.
    /// </summary>
    public string? FinishReason { get; init; }
}

/// <summary>
/// Telemetry for a single tool execution.
/// </summary>
public sealed record ToolExecutionTelemetry
{
    /// <summary>
    /// Tool call ID.
    /// </summary>
    public required string ToolCallId { get; init; }

    /// <summary>
    /// Name of the tool.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Iteration in which this tool was called.
    /// </summary>
    public required int Iteration { get; init; }

    /// <summary>
    /// When the tool execution started.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Duration of the tool execution.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Whether the tool execution succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Size of the input arguments in characters.
    /// </summary>
    public int? InputSize { get; init; }

    /// <summary>
    /// Size of the output in characters.
    /// </summary>
    public int? OutputSize { get; init; }

    /// <summary>
    /// Tool category (e.g., "github", "filesystem").
    /// </summary>
    public string? Category { get; init; }
}

/// <summary>
/// Builder for accumulating telemetry during execution.
/// </summary>
public sealed class AgenticTelemetryBuilder
{
    private readonly string _traceId = Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly List<IterationTelemetry> _iterations = new();
    private readonly List<ToolExecutionTelemetry> _toolExecutions = new();
    private readonly List<IterationTokenUsage> _tokenUsageByIteration = new();
    private TimeSpan _totalLlmDuration = TimeSpan.Zero;
    private TimeSpan _totalToolDuration = TimeSpan.Zero;
    private string _providerId = "";
    private string _modelId = "";
    private string? _error;
    private bool _reachedMaxIterations;

    /// <summary>
    /// Get the trace ID for this execution.
    /// </summary>
    public string TraceId => _traceId;

    /// <summary>
    /// Record an iteration.
    /// </summary>
    public void RecordIteration(
        int iteration,
        DateTimeOffset startedAt,
        TimeSpan llmDuration,
        TimeSpan toolDuration,
        TokenUsage tokenUsage,
        IReadOnlyList<string> toolCallNames,
        string? finishReason = null,
        int? contentLength = null)
    {
        var iterationTelemetry = new IterationTelemetry
        {
            Iteration = iteration,
            StartedAt = startedAt,
            LlmDuration = llmDuration,
            ToolDuration = toolDuration,
            TotalDuration = llmDuration + toolDuration,
            TokenUsage = tokenUsage,
            ToolCallNames = toolCallNames,
            HasToolCalls = toolCallNames.Count > 0,
            ContentLength = contentLength,
            FinishReason = finishReason
        };

        _iterations.Add(iterationTelemetry);
        _totalLlmDuration += llmDuration;
        _totalToolDuration += toolDuration;

        // Track token usage
        _tokenUsageByIteration.Add(new IterationTokenUsage
        {
            Iteration = iteration,
            PromptTokens = tokenUsage.PromptTokens,
            CompletionTokens = tokenUsage.CompletionTokens,
            Model = _modelId
        });
    }

    /// <summary>
    /// Record a tool execution.
    /// </summary>
    public void RecordToolExecution(
        ToolExecutionResult result,
        int iteration,
        DateTimeOffset startedAt,
        TimeSpan duration,
        string? category = null)
    {
        _toolExecutions.Add(new ToolExecutionTelemetry
        {
            ToolCallId = result.ToolCallId,
            ToolName = result.ToolName,
            Iteration = iteration,
            StartedAt = startedAt,
            Duration = duration,
            Success = result.Success,
            Error = result.Error,
            InputSize = result.Output?.Length,
            OutputSize = result.Output?.Length,
            Category = category
        });
    }

    /// <summary>
    /// Set the provider and model used.
    /// </summary>
    public void SetProvider(string providerId, string modelId)
    {
        _providerId = providerId;
        _modelId = modelId;
    }

    /// <summary>
    /// Set the error if execution failed.
    /// </summary>
    public void SetError(string error)
    {
        _error = error;
    }

    /// <summary>
    /// Mark that max iterations was reached.
    /// </summary>
    public void SetReachedMaxIterations()
    {
        _reachedMaxIterations = true;
    }

    /// <summary>
    /// Build the final telemetry record.
    /// </summary>
    public AgenticTelemetry Build()
    {
        var completedAt = DateTimeOffset.UtcNow;
        var totalDuration = completedAt - _startedAt;

        var totalPromptTokens = _tokenUsageByIteration.Sum(t => t.PromptTokens);
        var totalCompletionTokens = _tokenUsageByIteration.Sum(t => t.CompletionTokens);

        return new AgenticTelemetry
        {
            TraceId = _traceId,
            StartedAt = _startedAt,
            CompletedAt = completedAt,
            TotalDuration = totalDuration,
            LlmDuration = _totalLlmDuration,
            ToolDuration = _totalToolDuration,
            TokenUsage = new AgenticTokenUsage
            {
                TotalPromptTokens = totalPromptTokens,
                TotalCompletionTokens = totalCompletionTokens,
                ByIteration = _tokenUsageByIteration
            },
            Iterations = _iterations,
            ToolExecutions = _toolExecutions,
            LlmCallCount = _iterations.Count,
            ToolCallCount = _toolExecutions.Count,
            ProviderId = _providerId,
            ModelId = _modelId,
            Success = _error is null && !_reachedMaxIterations,
            Error = _error,
            ReachedMaxIterations = _reachedMaxIterations
        };
    }
}
