using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCortex.Conversations;
using OpenCortex.Core.Credentials;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Tenancy;
using OpenCortex.Orchestration.Execution;
using OpenCortex.Providers.Abstractions;
using OpenCortex.Tools;

namespace OpenCortex.Api.Tests;

public sealed class ChatAgenticMemoryTests : IClassFixture<ChatAgenticMemoryTests.ChatAgenticWebApplicationFactory>
{
    private readonly ChatAgenticWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ChatAgenticMemoryTests(ChatAgenticWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", "valid-user");
    }

    [Fact]
    public async Task AgenticCompletion_IncludesMemoryGuidance_WhenToolsAreAvailableByDefault()
    {
        _factory.Engine.LastRequest = null;

        var response = await _client.PostAsync(
            "/api/chat/completions/agentic",
            Json("""
            {
              "messages": [
                { "role": "user", "content": "Remember that I prefer concise roadmap updates." }
              ],
              "enableTools": true
            }
            """));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(_factory.Engine.LastRequest);
        Assert.Contains("Agent memory tools are available", _factory.Engine.LastRequest!.SystemMessage, StringComparison.Ordinal);
        Assert.Contains("recall_memories", _factory.Engine.LastRequest.SystemMessage, StringComparison.Ordinal);
        Assert.Contains("save_memory", _factory.Engine.LastRequest.SystemMessage, StringComparison.Ordinal);
        Assert.Contains("forget_memory", _factory.Engine.LastRequest.SystemMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgenticCompletion_DoesNotIncludeMemoryGuidance_WhenMemoryCategoryIsExcluded()
    {
        _factory.Engine.LastRequest = null;

        var response = await _client.PostAsync(
            "/api/chat/completions/agentic",
            Json("""
            {
              "messages": [
                { "role": "user", "content": "Open the GitHub repository and list branches." }
              ],
              "enableTools": true,
              "enabledCategories": ["github"]
            }
            """));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(_factory.Engine.LastRequest);
        Assert.DoesNotContain("Agent memory tools are available", _factory.Engine.LastRequest!.SystemMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgenticCompletion_PreservesCallerSystemPrompt_WhenAppendingMemoryGuidance()
    {
        _factory.Engine.LastRequest = null;

        var response = await _client.PostAsync(
            "/api/chat/completions/agentic",
            Json("""
            {
              "systemMessage": "Be precise and terse.",
              "messages": [
                { "role": "user", "content": "Keep track of my standing preference for terse answers." }
              ],
              "enabledTools": ["save_memory"]
            }
            """));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(_factory.Engine.LastRequest);
        Assert.StartsWith("Be precise and terse.", _factory.Engine.LastRequest!.SystemMessage, StringComparison.Ordinal);
        Assert.Contains("save_memory", _factory.Engine.LastRequest.SystemMessage, StringComparison.Ordinal);
    }

    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    public sealed class ChatAgenticWebApplicationFactory : WebApplicationFactory<Program>
    {
        public RecordingAgenticEngine Engine { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            Environment.SetEnvironmentVariable("OpenCortex__HostedAuth__Enabled", "true");
            Environment.SetEnvironmentVariable("OpenCortex__HostedAuth__FirebaseProjectId", "test-project");
            Environment.SetEnvironmentVariable("OpenCortex__Database__ConnectionString", "Host=localhost;Database=opencortex_test");

            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });

                ReplaceSingleton<ITenantCatalogStore>(services, new StubTenantCatalogStore());
                ReplaceScoped<IAgenticOrchestrationEngine>(services, _ => Engine);
                ReplaceScoped<IConversationRepository>(services, _ => new StubConversationRepository());
                ReplaceScoped<IConversationService>(services, _ => new StubConversationService());
                ReplaceScoped<IUserCredentialService>(services, _ => new StubUserCredentialService());
            });
        }

        private static void ReplaceSingleton<TService>(IServiceCollection services, TService implementation)
            where TService : class
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(TService));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton(implementation);
        }

        private static void ReplaceScoped<TService>(IServiceCollection services, Func<IServiceProvider, TService> factory)
            where TService : class
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(TService));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddScoped(factory);
        }
    }

    public sealed class RecordingAgenticEngine : IAgenticOrchestrationEngine
    {
        public AgenticOrchestrationRequest? LastRequest { get; set; }

        public Task<AgenticOrchestrationResult> ExecuteAgenticAsync(AgenticOrchestrationRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new AgenticOrchestrationResult
            {
                Completion = new ChatCompletion
                {
                    Content = "ok",
                    Usage = TokenUsage.Empty,
                    FinishReason = FinishReason.Stop,
                    Model = "test-model"
                },
                Conversation = request.Messages,
                ToolExecutions = Array.Empty<ToolExecutionResult>(),
                Iterations = 1,
                Duration = TimeSpan.Zero,
                ProviderId = "test-provider",
                ModelId = "test-model",
                ReachedMaxIterations = false
            });
        }

        public async IAsyncEnumerable<AgenticStreamEvent> StreamAgenticAsync(
            AgenticOrchestrationRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubTenantCatalogStore : ITenantCatalogStore
    {
        public Task<TenantContext> EnsureTenantContextAsync(AuthenticatedUserProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(new TenantContext(
                UserId: "user_test123",
                ExternalId: profile.ExternalId,
                Email: profile.Email,
                DisplayName: profile.DisplayName,
                AvatarUrl: profile.AvatarUrl,
                CustomerId: "cust_test123",
                CustomerSlug: "test-customer",
                CustomerName: "Test Customer",
                Role: "owner",
                PlanId: "pro",
                SubscriptionStatus: "active",
                CurrentPeriodEnd: null,
                CancelAtPeriodEnd: false,
                BrainId: "brain_test123",
                BrainSlug: "brain-test123",
                BrainName: "Test Brain"));
    }

    private sealed class StubUserCredentialService : IUserCredentialService
    {
        public Task<IReadOnlyDictionary<string, string>> GetDecryptedCredentialsAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task<string?> GetDecryptedCredentialAsync(Guid userId, string providerId, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class StubConversationRepository : IConversationRepository
    {
        public Task<Message> AddMessageAsync(Message message, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(string customerId, ConversationStatus? status = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Conversation> CreateAsync(Conversation conversation, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Conversation?> GetByIdAsync(string conversationId, CancellationToken cancellationToken = default) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<Message>> GetMessagesAsync(string conversationId, int? limit = null, int? offset = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Conversation?> GetWithMessagesAsync(string conversationId, int? messageLimit = null, CancellationToken cancellationToken = default) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<Conversation>> ListAsync(string customerId, ConversationStatus? status = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAsync(Conversation conversation, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateMessageAsync(Message message, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class StubConversationService : IConversationService
    {
        public Task<Message> AddAssistantMessageAsync(string conversationId, ChatCompletion completion, string providerId, int? latencyMs = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<Message> AddUserMessageAsync(string conversationId, string content, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task ArchiveConversationAsync(string conversationId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<Conversation> CreateConversationAsync(string customerId, string? userId = null, string? brainId = null, string? title = null, string? systemPrompt = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<ChatMessage>> GetMessagesForProviderAsync(string conversationId, int? limit = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<Conversation>> ListConversationsAsync(string customerId, int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task UpdateTitleAsync(string conversationId, string title, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var authorization) || authorization.Count == 0)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var token = authorization.ToString().Replace("Test ", "", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(token, "valid-user", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid test token."));
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "firebase-uid-123"),
                new Claim("sub", "firebase-uid-123"),
                new Claim(ClaimTypes.Email, "test@example.com"),
                new Claim("email", "test@example.com"),
                new Claim("name", "Test User"),
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
