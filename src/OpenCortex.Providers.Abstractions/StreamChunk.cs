namespace OpenCortex.Providers.Abstractions;

/// <summary>
/// A chunk of streaming response data.
/// </summary>
public sealed record StreamChunk
{
    /// <summary>
    /// Incremental text content.
    /// </summary>
    public string? ContentDelta { get; init; }

    /// <summary>
    /// Incremental tool call data.
    /// </summary>
    public ToolCallDelta? ToolCallDelta { get; init; }

    /// <summary>
    /// Whether this is the final chunk in the stream.
    /// </summary>
    public bool IsComplete { get; init; }

    /// <summary>
    /// Final token usage (only present when IsComplete is true).
    /// </summary>
    public TokenUsage? FinalUsage { get; init; }

    /// <summary>
    /// Finish reason (only present when IsComplete is true).
    /// </summary>
    public FinishReason? FinishReason { get; init; }

    /// <summary>
    /// Model that generated this chunk.
    /// </summary>
    public string? Model { get; init; }
}

/// <summary>
/// Incremental tool call data during streaming.
/// </summary>
public sealed record ToolCallDelta
{
    /// <summary>
    /// Index of this tool call in the array.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// Tool call ID (may only be present in first delta for this index).
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Function name (may only be present in first delta for this index).
    /// </summary>
    public string? FunctionName { get; init; }

    /// <summary>
    /// Incremental function arguments JSON.
    /// </summary>
    public string? ArgumentsDelta { get; init; }
}
