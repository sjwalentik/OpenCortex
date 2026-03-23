namespace OpenCortex.Conversations;

/// <summary>
/// A conversation between a user and AI models.
/// </summary>
public sealed class Conversation
{
    /// <summary>
    /// Unique identifier for the conversation (prefixed ID like "conv_xxx").
    /// </summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// Brain ID for memory context (prefixed ID like "brain_xxx").
    /// </summary>
    public string? BrainId { get; set; }

    /// <summary>
    /// Customer/tenant that owns this conversation (prefixed ID like "cust_xxx").
    /// </summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// User who created this conversation (prefixed ID like "user_xxx").
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Title of the conversation (auto-generated or user-set).
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// When the conversation was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the last message was added.
    /// </summary>
    public DateTimeOffset? LastMessageAt { get; set; }

    /// <summary>
    /// Current status of the conversation.
    /// </summary>
    public ConversationStatus Status { get; set; }

    /// <summary>
    /// Additional metadata as JSON.
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// System prompt for this conversation.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Messages in this conversation (not always loaded).
    /// </summary>
    public List<Message>? Messages { get; set; }
}

/// <summary>
/// Status of a conversation.
/// </summary>
public enum ConversationStatus
{
    /// <summary>
    /// Conversation is active and can receive messages.
    /// </summary>
    Active,

    /// <summary>
    /// Conversation is archived but still accessible.
    /// </summary>
    Archived,

    /// <summary>
    /// Conversation is deleted (soft delete).
    /// </summary>
    Deleted
}
