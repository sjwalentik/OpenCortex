using Microsoft.Extensions.Logging;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Conversations;

/// <summary>
/// Service for managing conversations.
/// </summary>
public interface IConversationService
{
    /// <summary>
    /// Create a new conversation.
    /// </summary>
    Task<Conversation> CreateConversationAsync(
        Guid customerId,
        Guid? userId = null,
        Guid? brainId = null,
        string? title = null,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a conversation by ID.
    /// </summary>
    Task<Conversation?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List conversations for a customer.
    /// </summary>
    Task<IReadOnlyList<Conversation>> ListConversationsAsync(
        Guid customerId,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archive a conversation.
    /// </summary>
    Task ArchiveConversationAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a user message to a conversation.
    /// </summary>
    Task<Message> AddUserMessageAsync(
        Guid conversationId,
        string content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add an assistant message to a conversation.
    /// </summary>
    Task<Message> AddAssistantMessageAsync(
        Guid conversationId,
        ChatCompletion completion,
        string providerId,
        int? latencyMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get messages for a conversation, formatted for the provider.
    /// </summary>
    Task<IReadOnlyList<ChatMessage>> GetMessagesForProviderAsync(
        Guid conversationId,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the conversation title.
    /// </summary>
    Task UpdateTitleAsync(Guid conversationId, string title, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default conversation service implementation.
/// </summary>
public sealed class ConversationService : IConversationService
{
    private readonly IConversationRepository _repository;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(
        IConversationRepository repository,
        ILogger<ConversationService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Conversation> CreateConversationAsync(
        Guid customerId,
        Guid? userId = null,
        Guid? brainId = null,
        string? title = null,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        var conversation = new Conversation
        {
            ConversationId = Guid.NewGuid(),
            CustomerId = customerId,
            UserId = userId,
            BrainId = brainId,
            Title = title,
            SystemPrompt = systemPrompt,
            Status = ConversationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.CreateAsync(conversation, cancellationToken);

        _logger.LogInformation("Created conversation {ConversationId} for customer {CustomerId}",
            conversation.ConversationId, customerId);

        return conversation;
    }

    public Task<Conversation?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        return _repository.GetWithMessagesAsync(conversationId, cancellationToken: cancellationToken);
    }

    public Task<IReadOnlyList<Conversation>> ListConversationsAsync(
        Guid customerId,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default)
    {
        return _repository.ListAsync(customerId, ConversationStatus.Active, limit, offset, cancellationToken);
    }

    public async Task ArchiveConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await _repository.GetByIdAsync(conversationId, cancellationToken);
        if (conversation is null) return;

        conversation.Status = ConversationStatus.Archived;
        await _repository.UpdateAsync(conversation, cancellationToken);

        _logger.LogInformation("Archived conversation {ConversationId}", conversationId);
    }

    public async Task<Message> AddUserMessageAsync(
        Guid conversationId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var message = new Message
        {
            MessageId = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = MessageRole.User,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.AddMessageAsync(message, cancellationToken);

        // Update conversation last message time
        var conversation = await _repository.GetByIdAsync(conversationId, cancellationToken);
        if (conversation is not null)
        {
            conversation.LastMessageAt = message.CreatedAt;
            await _repository.UpdateAsync(conversation, cancellationToken);
        }

        return message;
    }

    public async Task<Message> AddAssistantMessageAsync(
        Guid conversationId,
        ChatCompletion completion,
        string providerId,
        int? latencyMs = null,
        CancellationToken cancellationToken = default)
    {
        var message = new Message
        {
            MessageId = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = MessageRole.Assistant,
            Content = completion.Content,
            ProviderId = providerId,
            ModelId = completion.Model,
            LatencyMs = latencyMs,
            CreatedAt = DateTimeOffset.UtcNow
        };

        message.SetTokenUsage(new MessageTokenUsage
        {
            PromptTokens = completion.Usage.PromptTokens,
            CompletionTokens = completion.Usage.CompletionTokens
        });

        if (completion.ToolCalls?.Count > 0)
        {
            message.ToolCallsJson = System.Text.Json.JsonSerializer.Serialize(completion.ToolCalls);
        }

        await _repository.AddMessageAsync(message, cancellationToken);

        // Update conversation last message time
        var conversation = await _repository.GetByIdAsync(conversationId, cancellationToken);
        if (conversation is not null)
        {
            conversation.LastMessageAt = message.CreatedAt;
            await _repository.UpdateAsync(conversation, cancellationToken);
        }

        return message;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesForProviderAsync(
        Guid conversationId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var messages = await _repository.GetMessagesAsync(conversationId, limit, cancellationToken: cancellationToken);

        return messages.Select(m => new ChatMessage
        {
            Role = m.Role switch
            {
                MessageRole.System => ChatRole.System,
                MessageRole.User => ChatRole.User,
                MessageRole.Assistant => ChatRole.Assistant,
                MessageRole.Tool => ChatRole.Tool,
                _ => ChatRole.User
            },
            Content = m.Content
        }).ToList();
    }

    public async Task UpdateTitleAsync(Guid conversationId, string title, CancellationToken cancellationToken = default)
    {
        var conversation = await _repository.GetByIdAsync(conversationId, cancellationToken);
        if (conversation is null) return;

        conversation.Title = title;
        await _repository.UpdateAsync(conversation, cancellationToken);
    }
}
