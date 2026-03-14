namespace OpenCortex.Providers.Abstractions;

/// <summary>
/// Result of a chat completion request.
/// </summary>
public sealed record ChatCompletion
{
    /// <summary>
    /// Generated text content.
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Tool calls requested by the model.
    /// </summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    /// <summary>
    /// Token usage statistics.
    /// </summary>
    public required TokenUsage Usage { get; init; }

    /// <summary>
    /// Reason the model stopped generating.
    /// </summary>
    public required FinishReason FinishReason { get; init; }

    /// <summary>
    /// Model that was used to generate this completion.
    /// </summary>
    public required string Model { get; init; }
}

/// <summary>
/// Token usage statistics for a completion.
/// </summary>
public sealed record TokenUsage
{
    /// <summary>
    /// Number of tokens in the prompt.
    /// </summary>
    public required int PromptTokens { get; init; }

    /// <summary>
    /// Number of tokens in the completion.
    /// </summary>
    public required int CompletionTokens { get; init; }

    /// <summary>
    /// Total tokens used (prompt + completion).
    /// </summary>
    public int TotalTokens => PromptTokens + CompletionTokens;

    /// <summary>
    /// Empty usage for cases where tracking is unavailable.
    /// </summary>
    public static TokenUsage Empty => new() { PromptTokens = 0, CompletionTokens = 0 };
}

/// <summary>
/// Reason the model stopped generating.
/// </summary>
public enum FinishReason
{
    /// <summary>
    /// Model completed naturally.
    /// </summary>
    Stop,

    /// <summary>
    /// Maximum token limit reached.
    /// </summary>
    Length,

    /// <summary>
    /// Model is requesting tool calls.
    /// </summary>
    ToolCalls,

    /// <summary>
    /// Content was filtered for safety.
    /// </summary>
    ContentFilter,

    /// <summary>
    /// Unknown or provider-specific reason.
    /// </summary>
    Other
}
