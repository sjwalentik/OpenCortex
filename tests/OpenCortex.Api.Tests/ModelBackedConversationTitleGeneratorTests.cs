using Microsoft.Extensions.Logging.Abstractions;
using OpenCortex.Conversations;
using OpenCortex.Orchestration;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Api.Tests;

public sealed class ModelBackedConversationTitleGeneratorTests
{
    [Fact]
    public async Task GenerateTitleAsync_UsesConfiguredProviderAndSanitizesResponse()
    {
        var provider = new RecordingProvider("openai", "gpt-5.4-mini", "\"Debugging conversation titles.\"\n");
        var factory = new StubUserProviderFactory(provider);
        var generator = new ModelBackedConversationTitleGenerator(
            factory,
            NullLogger<ModelBackedConversationTitleGenerator>.Instance);

        var title = await generator.GenerateTitleAsync(
            new Conversation
            {
                ConversationId = "conv-1",
                CustomerId = Guid.NewGuid().ToString(),
                UserId = Guid.NewGuid().ToString(),
                Metadata = """{"providerId":"openai","modelId":"gpt-5.4-mini"}"""
            },
            [
                new Message { Role = MessageRole.User, Content = "Help me debug conversation title generation.", CreatedAt = DateTimeOffset.UtcNow },
                new Message { Role = MessageRole.Assistant, Content = "I can inspect the title path and provider selection.", CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1) }
            ],
            CancellationToken.None);

        Assert.Equal("Debugging conversation titles", title);
        Assert.Equal("gpt-5.4-mini", provider.LastRequest?.Model);
        Assert.NotNull(provider.LastRequest);
        Assert.Equal(2, provider.LastRequest!.Messages.Count);
    }

    [Fact]
    public async Task GenerateTitleAsync_ReturnsNullWhenConversationIdsAreNotGuids()
    {
        var provider = new RecordingProvider("openai", "gpt-5.4-mini", "Should not run");
        var factory = new StubUserProviderFactory(provider);
        var generator = new ModelBackedConversationTitleGenerator(
            factory,
            NullLogger<ModelBackedConversationTitleGenerator>.Instance);

        var title = await generator.GenerateTitleAsync(
            new Conversation
            {
                ConversationId = "conv-2",
                CustomerId = "cust_123",
                UserId = "user_123"
            },
            [new Message { Role = MessageRole.User, Content = "Test", CreatedAt = DateTimeOffset.UtcNow }],
            CancellationToken.None);

        Assert.Null(title);
        Assert.Null(provider.LastRequest);
    }

    private sealed class StubUserProviderFactory : IUserProviderFactory
    {
        private readonly IModelProvider _provider;

        public StubUserProviderFactory(IModelProvider provider)
        {
            _provider = provider;
        }

        public Task<IModelProvider?> GetProviderForUserAsync(Guid customerId, Guid userId, string providerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IModelProvider?>(string.Equals(providerId, _provider.ProviderId, StringComparison.OrdinalIgnoreCase) ? _provider : null);

        public Task<IReadOnlyList<IModelProvider>> GetProvidersForUserAsync(Guid customerId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IModelProvider>>([_provider]);

        public Task<bool> HasConfiguredProvidersAsync(Guid customerId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class RecordingProvider : IModelProvider
    {
        private readonly string _responseContent;

        public RecordingProvider(string providerId, string modelId, string responseContent)
        {
            ProviderId = providerId;
            DefaultModel = modelId;
            _responseContent = responseContent;
        }

        public string DefaultModel { get; }

        public ChatRequest? LastRequest { get; private set; }

        public string ProviderId { get; }

        public string Name => ProviderId;

        public string ProviderType => ProviderId;

        public ProviderCapabilities Capabilities { get; } = ProviderCapabilities.Default;

        public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ChatCompletion
            {
                Content = _responseContent,
                FinishReason = FinishReason.Stop,
                Model = string.IsNullOrWhiteSpace(request.Model) ? DefaultModel : request.Model,
                Usage = TokenUsage.Empty
            });
        }

        public IAsyncEnumerable<StreamChunk> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderHealthResult.Healthy());

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModelInfo>>([new ModelInfo { Id = DefaultModel }]);
    }
}
