using Microsoft.Extensions.Logging.Abstractions;
using OpenCortex.Conversations;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Api.Tests;

public sealed class ConversationTitleGenerationTests
{
    [Fact]
    public async Task AddAssistantMessageAsync_GeneratesTitleFromFirstUserMessage_WhenConversationIsUntitled()
    {
        var repository = new InMemoryConversationRepository(new Conversation
        {
            ConversationId = "conv-1",
            CustomerId = "cust-1",
            UserId = "user-1",
            Status = ConversationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            Title = null,
        });
        var service = new ConversationService(
            repository,
            NullConversationTitleGenerator.Instance,
            NullLogger<ConversationService>.Instance);

        await service.AddUserMessageAsync("conv-1", "Please help me debug conversation titles in OpenCortex", CancellationToken.None);

        await service.AddAssistantMessageAsync(
            "conv-1",
            new ChatCompletion
            {
                Content = "I can help with the title generation flow.",
                Usage = TokenUsage.Empty,
                FinishReason = FinishReason.Stop,
                Model = "gpt-test",
            },
            "openai",
            cancellationToken: CancellationToken.None);

        Assert.Equal("Please help me debug conversation titles in OpenCortex", repository.Conversation.Title);
    }

    [Fact]
    public async Task AddAssistantMessageAsync_DoesNotOverrideUserDefinedTitle()
    {
        var repository = new InMemoryConversationRepository(new Conversation
        {
            ConversationId = "conv-2",
            CustomerId = "cust-1",
            UserId = "user-1",
            Status = ConversationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            Title = "Release notes draft",
        });
        var service = new ConversationService(
            repository,
            NullConversationTitleGenerator.Instance,
            NullLogger<ConversationService>.Instance);

        await service.AddUserMessageAsync("conv-2", "Can you rewrite this announcement?", CancellationToken.None);

        await service.AddAssistantMessageAsync(
            "conv-2",
            new ChatCompletion
            {
                Content = "Here is a tighter version of the announcement.",
                Usage = TokenUsage.Empty,
                FinishReason = FinishReason.Stop,
                Model = "gpt-test",
            },
            "openai",
            cancellationToken: CancellationToken.None);

        Assert.Equal("Release notes draft", repository.Conversation.Title);
    }

    private sealed class InMemoryConversationRepository : IConversationRepository
    {
        private readonly Dictionary<string, Conversation> _conversations = new(StringComparer.Ordinal);
        private readonly List<Message> _messages = [];

        public InMemoryConversationRepository(Conversation conversation)
        {
            _conversations[conversation.ConversationId] = conversation;
        }

        /// <summary>
        /// Returns the current persisted state of the first seeded conversation.
        /// Always reflects the last <see cref="UpdateAsync"/> call, so assertions
        /// remain correct even if the service creates a new object rather than
        /// mutating the original reference.
        /// </summary>
        public Conversation Conversation => _conversations.Values.First();

        public Task<int> CountAsync(string customerId, ConversationStatus? status = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(1);

        public Task<Conversation> CreateAsync(Conversation conversation, CancellationToken cancellationToken = default)
        {
            _conversations[conversation.ConversationId] = conversation;
            return Task.FromResult(conversation);
        }

        public Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Message> AddMessageAsync(Message message, CancellationToken cancellationToken = default)
        {
            _messages.Add(message);
            return Task.FromResult(message);
        }

        public Task<Conversation?> GetByIdAsync(string conversationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_conversations.TryGetValue(conversationId, out var c) ? c : null);

        public Task<IReadOnlyList<Message>> GetMessagesAsync(string conversationId, int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Message> messages = _messages
                .Where(message => message.ConversationId == conversationId)
                .OrderBy(message => message.CreatedAt)
                .ToList();

            return Task.FromResult(messages);
        }

        public Task<Conversation?> GetWithMessagesAsync(string conversationId, int? messageLimit = null, CancellationToken cancellationToken = default)
        {
            if (!_conversations.TryGetValue(conversationId, out var conversation))
            {
                return Task.FromResult<Conversation?>(null);
            }

            conversation.Messages = _messages
                .Where(m => m.ConversationId == conversationId)
                .ToList();
            return Task.FromResult<Conversation?>(conversation);
        }

        public Task<IReadOnlyList<Conversation>> ListAsync(string customerId, string userId, ConversationStatus? status = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Conversation>>([.._conversations.Values]);

        public Task UpdateAsync(Conversation conversation, CancellationToken cancellationToken = default)
        {
            _conversations[conversation.ConversationId] = conversation;
            return Task.CompletedTask;
        }

        public Task UpdateMessageAsync(Message message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
