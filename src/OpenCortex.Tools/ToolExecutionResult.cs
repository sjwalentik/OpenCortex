namespace OpenCortex.Tools;

/// <summary>
/// Result of a tool execution.
/// </summary>
public sealed record ToolExecutionResult
{
    /// <summary>
    /// ID of the tool call this result corresponds to.
    /// </summary>
    public required string ToolCallId { get; init; }

    /// <summary>
    /// Name of the tool that was executed.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Whether the tool executed successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Output from the tool (JSON or text).
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Time taken to execute the tool.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Create a successful result.
    /// </summary>
    public static ToolExecutionResult Ok(string toolCallId, string toolName, string output, TimeSpan duration)
    {
        return new ToolExecutionResult
        {
            ToolCallId = toolCallId,
            ToolName = toolName,
            Success = true,
            Output = output,
            Duration = duration
        };
    }

    /// <summary>
    /// Create a failed result.
    /// </summary>
    public static ToolExecutionResult Fail(string toolCallId, string toolName, string error, TimeSpan duration)
    {
        return new ToolExecutionResult
        {
            ToolCallId = toolCallId,
            ToolName = toolName,
            Success = false,
            Error = error,
            Duration = duration
        };
    }
}
