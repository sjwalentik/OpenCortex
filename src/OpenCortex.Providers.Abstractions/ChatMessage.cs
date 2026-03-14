namespace OpenCortex.Providers.Abstractions;

/// <summary>
/// A message in a chat conversation.
/// </summary>
public sealed record ChatMessage
{
    /// <summary>
    /// Role of the message sender.
    /// </summary>
    public required ChatRole Role { get; init; }

    /// <summary>
    /// Text content of the message.
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Tool calls made by the assistant (when Role is Assistant).
    /// </summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    /// <summary>
    /// Tool call ID this message is responding to (when Role is Tool).
    /// </summary>
    public string? ToolCallId { get; init; }

    /// <summary>
    /// Name of the tool that produced this result (when Role is Tool).
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Create a system message.
    /// </summary>
    public static ChatMessage System(string content) =>
        new() { Role = ChatRole.System, Content = content };

    /// <summary>
    /// Create a user message.
    /// </summary>
    public static ChatMessage User(string content) =>
        new() { Role = ChatRole.User, Content = content };

    /// <summary>
    /// Create an assistant message.
    /// </summary>
    public static ChatMessage Assistant(string content) =>
        new() { Role = ChatRole.Assistant, Content = content };

    /// <summary>
    /// Create an assistant message with tool calls.
    /// </summary>
    public static ChatMessage AssistantToolCalls(IReadOnlyList<ToolCall> toolCalls) =>
        new() { Role = ChatRole.Assistant, ToolCalls = toolCalls };

    /// <summary>
    /// Create a tool result message.
    /// </summary>
    public static ChatMessage ToolResult(string toolCallId, string toolName, string content) =>
        new() { Role = ChatRole.Tool, ToolCallId = toolCallId, ToolName = toolName, Content = content };
}

/// <summary>
/// Role of a message participant.
/// </summary>
public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}
