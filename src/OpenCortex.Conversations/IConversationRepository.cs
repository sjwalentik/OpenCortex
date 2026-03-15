namespace OpenCortex.Conversations;

/// <summary>
/// Repository for conversation persistence.
/// </summary>
public interface IConversationRepository
{
    /// <summary>
    /// Create a new conversation.
    /// </summary>
    Task<Conversation> CreateAsync(Conversation conversation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a conversation by ID.
    /// </summary>
    Task<Conversation?> GetByIdAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a conversation with its messages.
    /// </summary>
    Task<Conversation?> GetWithMessagesAsync(string conversationId, int? messageLimit = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// List conversations for a customer.
    /// </summary>
    Task<IReadOnlyList<Conversation>> ListAsync(
        string customerId,
        ConversationStatus? status = null,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a conversation.
    /// </summary>
    Task UpdateAsync(Conversation conversation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete (archive) a conversation.
    /// </summary>
    Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a message to a conversation.
    /// </summary>
    Task<Message> AddMessageAsync(Message message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get messages for a conversation.
    /// </summary>
    Task<IReadOnlyList<Message>> GetMessagesAsync(
        string conversationId,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a message.
    /// </summary>
    Task UpdateMessageAsync(Message message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Count conversations for a customer.
    /// </summary>
    Task<int> CountAsync(string customerId, ConversationStatus? status = null, CancellationToken cancellationToken = default);
}
