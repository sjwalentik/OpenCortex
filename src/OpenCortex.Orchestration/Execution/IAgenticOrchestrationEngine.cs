using OpenCortex.Providers.Abstractions;
using OpenCortex.Tools;

namespace OpenCortex.Orchestration.Execution;

/// <summary>
/// Orchestration engine that executes tool loops autonomously.
/// </summary>
public interface IAgenticOrchestrationEngine
{
    /// <summary>
    /// Execute an agentic request with tool execution loop.
    /// </summary>
    Task<AgenticOrchestrationResult> ExecuteAgenticAsync(
        AgenticOrchestrationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream an agentic request with tool execution events.
    /// </summary>
    IAsyncEnumerable<AgenticStreamEvent> StreamAgenticAsync(
        AgenticOrchestrationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for agentic orchestration with tool execution.
/// </summary>
public sealed record AgenticOrchestrationRequest
{
    /// <summary>
    /// User ID for provider and credential resolution.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Customer/tenant ID.
    /// </summary>
    public required Guid CustomerId { get; init; }

    /// <summary>
    /// Conversation ID for tracking.
    /// </summary>
    public required string ConversationId { get; init; }

    /// <summary>
    /// Messages in the conversation.
    /// </summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>
    /// System message to prepend.
    /// </summary>
    public string? SystemMessage { get; init; }

    /// <summary>
    /// Memory context to inject.
    /// </summary>
    public IReadOnlyList<string>? MemoryContext { get; init; }

    /// <summary>
    /// Enabled tool names (null = all available tools).
    /// </summary>
    public IReadOnlyList<string>? EnabledTools { get; init; }

    /// <summary>
    /// Tool categories to enable (e.g., "github", "filesystem").
    /// </summary>
    public IReadOnlyList<string>? EnabledCategories { get; init; }

    /// <summary>
    /// Maximum number of tool execution iterations.
    /// </summary>
    public int MaxIterations { get; init; } = 25;

    /// <summary>
    /// Routing context for provider selection.
    /// </summary>
    public Routing.RoutingContext? RoutingContext { get; init; }

    /// <summary>
    /// Additional request options.
    /// </summary>
    public ChatRequestOptions? Options { get; init; }

    /// <summary>
    /// Pre-loaded credentials for tool execution.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Credentials { get; init; }

    /// <summary>
    /// Command execution mode: "auto" or "approval".
    /// </summary>
    public string CommandMode { get; init; } = "approval";
}

/// <summary>
/// Result from agentic orchestration.
/// </summary>
public sealed record AgenticOrchestrationResult
{
    /// <summary>
    /// Final completion from the LLM.
    /// </summary>
    public required ChatCompletion Completion { get; init; }

    /// <summary>
    /// Full conversation including tool messages.
    /// </summary>
    public required IReadOnlyList<ChatMessage> Conversation { get; init; }

    /// <summary>
    /// All tool executions that occurred.
    /// </summary>
    public required IReadOnlyList<ToolExecutionResult> ToolExecutions { get; init; }

    /// <summary>
    /// Number of LLM iterations performed.
    /// </summary>
    public required int Iterations { get; init; }

    /// <summary>
    /// Total execution time.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Provider that handled the request.
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// Model that was used.
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// Whether the max iteration limit was reached.
    /// </summary>
    public bool ReachedMaxIterations { get; init; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Event from agentic streaming.
/// </summary>
public abstract record AgenticStreamEvent
{
    /// <summary>
    /// Timestamp of the event.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Streaming text content from the LLM.
/// </summary>
public sealed record AgenticTextEvent : AgenticStreamEvent
{
    /// <summary>
    /// Text content chunk.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Current iteration number.
    /// </summary>
    public required int Iteration { get; init; }
}

/// <summary>
/// LLM is starting to call tools.
/// </summary>
public sealed record AgenticToolCallStartEvent : AgenticStreamEvent
{
    /// <summary>
    /// Tool calls being made.
    /// </summary>
    public required IReadOnlyList<ToolCall> ToolCalls { get; init; }

    /// <summary>
    /// Current iteration number.
    /// </summary>
    public required int Iteration { get; init; }
}

/// <summary>
/// A tool execution completed.
/// </summary>
public sealed record AgenticToolResultEvent : AgenticStreamEvent
{
    /// <summary>
    /// Result of the tool execution.
    /// </summary>
    public required ToolExecutionResult Result { get; init; }

    /// <summary>
    /// Current iteration number.
    /// </summary>
    public required int Iteration { get; init; }
}

/// <summary>
/// Agentic execution completed.
/// </summary>
public sealed record AgenticCompleteEvent : AgenticStreamEvent
{
    /// <summary>
    /// Final result.
    /// </summary>
    public required AgenticOrchestrationResult Result { get; init; }
}

/// <summary>
/// Error occurred during agentic execution.
/// </summary>
public sealed record AgenticErrorEvent : AgenticStreamEvent
{
    /// <summary>
    /// Error message.
    /// </summary>
    public required string Error { get; init; }

    /// <summary>
    /// Current iteration when error occurred.
    /// </summary>
    public int Iteration { get; init; }
}
