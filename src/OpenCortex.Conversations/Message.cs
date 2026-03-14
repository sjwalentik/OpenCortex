using System.Text.Json;

namespace OpenCortex.Conversations;

/// <summary>
/// A message in a conversation.
/// </summary>
public sealed class Message
{
    /// <summary>
    /// Unique identifier for the message.
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// Conversation this message belongs to.
    /// </summary>
    public Guid ConversationId { get; set; }

    /// <summary>
    /// Role of the message sender.
    /// </summary>
    public MessageRole Role { get; set; }

    /// <summary>
    /// Text content of the message.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Provider that generated this message (for assistant messages).
    /// </summary>
    public string? ProviderId { get; set; }

    /// <summary>
    /// Model that generated this message.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Tool calls made by the assistant (JSON array).
    /// </summary>
    public string? ToolCallsJson { get; set; }

    /// <summary>
    /// Token usage for this message (JSON).
    /// </summary>
    public string? TokenUsageJson { get; set; }

    /// <summary>
    /// Latency in milliseconds for generating this message.
    /// </summary>
    public int? LatencyMs { get; set; }

    /// <summary>
    /// When the message was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Parent message ID (for branching conversations).
    /// </summary>
    public Guid? ParentMessageId { get; set; }

    /// <summary>
    /// Additional metadata as JSON.
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// Get token usage from JSON.
    /// </summary>
    public MessageTokenUsage? GetTokenUsage()
    {
        if (string.IsNullOrEmpty(TokenUsageJson)) return null;
        return JsonSerializer.Deserialize<MessageTokenUsage>(TokenUsageJson);
    }

    /// <summary>
    /// Set token usage as JSON.
    /// </summary>
    public void SetTokenUsage(MessageTokenUsage usage)
    {
        TokenUsageJson = JsonSerializer.Serialize(usage);
    }
}

/// <summary>
/// Role of a message in a conversation.
/// </summary>
public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool
}

/// <summary>
/// Token usage for a message.
/// </summary>
public sealed record MessageTokenUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens => PromptTokens + CompletionTokens;
}
