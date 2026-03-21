using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCortex.Core.Configuration;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Query;
using OpenCortex.Indexer.Indexing;
using OpenCortex.Orchestration;
using OpenCortex.Orchestration.Execution;
using OpenCortex.Orchestration.Memory;
using OpenCortex.Orchestration.Routing;
using OpenCortex.Providers.Abstractions;
using OpenCortex.Retrieval.Execution;
using OpenCortex.Tools;
using OpenCortex.Tools.Memory;
using OpenCortex.Tools.Memory.Handlers;

namespace OpenCortex.Api.Tests;

public sealed class AgenticOrchestrationEngineMemoryTests
{
    [Fact]
    public async Task ExecuteAgenticAsync_RunsSaveMemoryToolAndFeedsResultBackIntoLoop()
    {
        var documentStore = new StubManagedDocumentStore();
        var engine = CreateEngine(
            documentStore,
            new StaticDocumentQueryStore([]),
            new StubMemoryBrainResolver(new MemoryBrainResult(true, "brain-memory", null, false)),
            new StubSubscriptionStore(planId: "pro"),
            new ScriptedUserOrchestrationService(
            [
                request =>
                {
                    Assert.Single(request.Messages);
                    Assert.Equal(ChatRole.User, request.Messages[0].Role);
                    Assert.Contains("save_memory", request.Tools!.Select(tool => tool.Function.Name));

                    return Task.FromResult(CreateToolCallResult(
                        "save-1",
                        "save_memory",
                        """
                        {
                          "content": "Stephen prefers concise roadmap updates.",
                          "category": "preference",
                          "confidence": "high",
                          "tags": ["user", "style"]
                        }
                        """));
                },
                request =>
                {
                    Assert.Contains(request.Messages, message =>
                        message.Role == ChatRole.Tool
                        && string.Equals(message.ToolName, "save_memory", StringComparison.Ordinal)
                        && (message.Content?.Contains("\"success\":true", StringComparison.OrdinalIgnoreCase) ?? false));

                    return Task.FromResult(CreateAssistantResult("I'll remember that preference for future roadmap updates."));
                }
            ]));

        var result = await engine.ExecuteAgenticAsync(new AgenticOrchestrationRequest
        {
            UserId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            ConversationId = "conv-memory-save",
            Messages = [ChatMessage.User("Remember that Stephen prefers concise roadmap updates.")],
            EnabledTools = ["save_memory"],
            RoutingContext = new RoutingContext
            {
                CustomerId = "cust-test",
                UserId = "user-test",
                BrainId = "brain-memory",
                ConversationId = "conv-memory-save"
            }
        });

        Assert.False(result.ReachedMaxIterations);
        Assert.Null(result.Error);
        Assert.Equal(2, result.Iterations);
        Assert.Single(result.ToolExecutions);
        Assert.True(result.ToolExecutions[0].Success);
        Assert.Equal("save_memory", result.ToolExecutions[0].ToolName);
        Assert.Equal("I'll remember that preference for future roadmap updates.", result.Completion.Content);
        Assert.Contains(result.Conversation, message =>
            message.Role == ChatRole.Tool
            && string.Equals(message.ToolName, "save_memory", StringComparison.Ordinal));
        Assert.Single(documentStore.Documents);
        Assert.StartsWith("memories/preference/", documentStore.Documents[0].CanonicalPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StreamAgenticAsync_EmitsRecallMemoryToolResultBeforeFinalAssistantAnswer()
    {
        var documentStore = new StubManagedDocumentStore();
        await documentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest(
                BrainId: "brain-memory",
                CustomerId: "cust-test",
                Title: "[fact] Stephen likes to camp",
                Slug: "memories/fact/stephen-camping",
                Content: "Stephen likes to camp.",
                Frontmatter: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["category"] = "fact",
                    ["confidence"] = "high",
                    ["tags"] = "profile,hobby"
                },
                Status: "published",
                UserId: "user-test"),
            CancellationToken.None);

        var queryStore = new StaticDocumentQueryStore(
        [
            new RetrievalResultRecord(
                "memory-0001",
                "brain-memory",
                "memories/fact/stephen-camping.md",
                "[fact] Stephen likes to camp",
                null,
                "Stephen likes to camp.",
                0.94,
                "semantic",
                new ScoreBreakdown(0.0, 0.94, 0.0))
        ]);

        var orchestration = new ScriptedUserOrchestrationService(
            executeScripts: [],
            streamScripts:
            [
                request =>
                {
                    Assert.Single(request.Messages);
                    Assert.Contains("recall_memories", request.Tools!.Select(tool => tool.Function.Name));
                    return StreamWithToolCall(
                        "recall-1",
                        "recall_memories",
                        """
                        {
                          "query": "camp",
                          "limit": 3
                        }
                        """);
                },
                request =>
                {
                    Assert.Contains(request.Messages, message =>
                        message.Role == ChatRole.Tool
                        && string.Equals(message.ToolName, "recall_memories", StringComparison.Ordinal)
                        && (message.Content?.Contains("stephen-camping", StringComparison.OrdinalIgnoreCase) ?? false));

                    return StreamWithAssistantText("I remember that Stephen likes to camp.");
                }
            ]);

        var engine = CreateEngine(
            documentStore,
            queryStore,
            new StubMemoryBrainResolver(new MemoryBrainResult(true, "brain-memory", null, false)),
            new StubSubscriptionStore(planId: "pro"),
            orchestration);

        var events = new List<AgenticStreamEvent>();
        await foreach (var streamEvent in engine.StreamAgenticAsync(new AgenticOrchestrationRequest
        {
            UserId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            ConversationId = "conv-memory-recall",
            Messages = [ChatMessage.User("What do you remember about camping?")],
            EnabledTools = ["recall_memories"],
            RoutingContext = new RoutingContext
            {
                CustomerId = "cust-test",
                UserId = "user-test",
                BrainId = "brain-memory",
                ConversationId = "conv-memory-recall"
            }
        }))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, streamEvent =>
            streamEvent is AgenticToolCallStartEvent toolCallStart
            && toolCallStart.ToolCalls.Count == 1
            && string.Equals(toolCallStart.ToolCalls[0].Function.Name, "recall_memories", StringComparison.Ordinal));

        var toolResult = Assert.Single(events.OfType<AgenticToolResultEvent>());
        Assert.True(toolResult.Result.Success);
        Assert.Equal("recall_memories", toolResult.Result.ToolName);
        Assert.Contains("stephen-camping", toolResult.Result.Output, StringComparison.OrdinalIgnoreCase);

        var completion = Assert.Single(events.OfType<AgenticCompleteEvent>());
        Assert.Equal("I remember that Stephen likes to camp.", completion.Result.Completion.Content);
        Assert.Equal(2, completion.Result.Iterations);
    }

    private static AgenticOrchestrationEngine CreateEngine(
        StubManagedDocumentStore documentStore,
        IDocumentQueryStore queryStore,
        IMemoryBrainResolver memoryBrainResolver,
        ISubscriptionStore subscriptionStore,
        ScriptedUserOrchestrationService orchestration)
    {
        var indexingService = new StubManagedContentBrainIndexingService();
        var toolExecutor = new ToolExecutorRegistry(
        [
            new SaveMemoryHandler(
                documentStore,
                memoryBrainResolver,
                indexingService,
                subscriptionStore,
                new OpenCortexOptions()),
            new RecallMemoriesHandler(
                new OqlQueryExecutor(queryStore),
                documentStore,
                memoryBrainResolver,
                NullLogger<RecallMemoriesHandler>.Instance),
            new ForgetMemoryHandler(
                documentStore,
                memoryBrainResolver,
                indexingService)
        ],
        [
            new MemoryToolDefinitions()
        ],
        NullLogger<ToolExecutorRegistry>.Instance);

        return new AgenticOrchestrationEngine(
            orchestration,
            toolExecutor,
            new StubWorkspaceManager(),
            NullLogger<AgenticOrchestrationEngine>.Instance);
    }

    private static OrchestrationResult CreateAssistantResult(string content) =>
        new()
        {
            Completion = new ChatCompletion
            {
                Content = content,
                Usage = TokenUsage.Empty,
                FinishReason = FinishReason.Stop,
                Model = "test-model"
            },
            Routing = CreateRoutingDecision(),
            ProviderId = "test-provider",
            ModelId = "test-model",
            LatencyMs = 0
        };

    private static OrchestrationResult CreateToolCallResult(string toolCallId, string toolName, string arguments) =>
        new()
        {
            Completion = new ChatCompletion
            {
                ToolCalls =
                [
                    new ToolCall
                    {
                        Id = toolCallId,
                        Function = new FunctionCall
                        {
                            Name = toolName,
                            Arguments = arguments
                        }
                    }
                ],
                Usage = TokenUsage.Empty,
                FinishReason = FinishReason.ToolCalls,
                Model = "test-model"
            },
            Routing = CreateRoutingDecision(),
            ProviderId = "test-provider",
            ModelId = "test-model",
            LatencyMs = 0
        };

    private static RoutingDecision CreateRoutingDecision() =>
        new()
        {
            ProviderId = "test-provider",
            ModelId = "test-model",
            Classification = new TaskClassification
            {
                Category = TaskCategory.General,
                Confidence = 1.0
            }
        };

    private static async IAsyncEnumerable<OrchestrationStreamChunk> StreamWithToolCall(
        string toolCallId,
        string toolName,
        string arguments)
    {
        yield return new OrchestrationStreamChunk
        {
            ProviderId = "test-provider",
            Routing = CreateRoutingDecision(),
            Chunk = new StreamChunk
            {
                ToolCallDelta = new ToolCallDelta
                {
                    Index = 0,
                    Id = toolCallId,
                    FunctionName = toolName,
                    ArgumentsDelta = arguments
                }
            }
        };

        yield return new OrchestrationStreamChunk
        {
            ProviderId = "test-provider",
            Chunk = new StreamChunk
            {
                IsComplete = true,
                FinishReason = FinishReason.ToolCalls,
                FinalUsage = TokenUsage.Empty,
                Model = "test-model"
            }
        };

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<OrchestrationStreamChunk> StreamWithAssistantText(string content)
    {
        yield return new OrchestrationStreamChunk
        {
            ProviderId = "test-provider",
            Routing = CreateRoutingDecision(),
            Chunk = new StreamChunk
            {
                ContentDelta = content
            }
        };

        yield return new OrchestrationStreamChunk
        {
            ProviderId = "test-provider",
            Chunk = new StreamChunk
            {
                IsComplete = true,
                FinishReason = FinishReason.Stop,
                FinalUsage = TokenUsage.Empty,
                Model = "test-model"
            }
        };

        await Task.CompletedTask;
    }

    private sealed class ScriptedUserOrchestrationService(
        IReadOnlyList<Func<OrchestrationRequest, Task<OrchestrationResult>>> executeScripts,
        IReadOnlyList<Func<OrchestrationRequest, IAsyncEnumerable<OrchestrationStreamChunk>>>? streamScripts = null)
        : IUserOrchestrationService
    {
        private readonly Queue<Func<OrchestrationRequest, Task<OrchestrationResult>>> _executeScripts = new(executeScripts);
        private readonly Queue<Func<OrchestrationRequest, IAsyncEnumerable<OrchestrationStreamChunk>>> _streamScripts = new(streamScripts ?? []);

        public List<OrchestrationRequest> ExecuteRequests { get; } = [];
        public List<OrchestrationRequest> StreamRequests { get; } = [];

        public Task<IReadOnlyList<IModelProvider>> GetProvidersAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<IModelProvider>>([]);

        public Task<IModelProvider?> GetProviderAsync(Guid userId, string providerId, CancellationToken cancellationToken = default)
            => Task.FromResult<IModelProvider?>(null);

        public Task<RoutingDecision> RouteAsync(Guid userId, string message, RoutingContext? context = null, CancellationToken cancellationToken = default)
            => Task.FromResult(CreateRoutingDecision());

        public Task<OrchestrationResult> ExecuteAsync(Guid userId, OrchestrationRequest request, CancellationToken cancellationToken = default)
        {
            ExecuteRequests.Add(request);
            return _executeScripts.Dequeue()(request);
        }

        public IAsyncEnumerable<OrchestrationStreamChunk> StreamAsync(Guid userId, OrchestrationRequest request, CancellationToken cancellationToken = default)
        {
            StreamRequests.Add(request);
            return _streamScripts.Dequeue()(request);
        }
    }

    private sealed class StubWorkspaceManager : IWorkspaceManager
    {
        public bool SupportsContainerIsolation => false;

        public Task CleanupExpiredWorkspacesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteWorkspaceAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<WorkspaceStatus> EnsureRunningAsync(Guid userId, IReadOnlyDictionary<string, string>? credentials = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceStatus
            {
                UserId = userId,
                State = WorkspaceState.Running,
                WorkspacePath = $"C:\\temp\\workspace\\{userId:N}"
            });

        public Task<CommandResult> ExecuteCommandAsync(Guid userId, string command, string? arguments = null, string? workingDirectory = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<string> GetWorkspacePathAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult($"C:\\temp\\workspace\\{userId:N}");

        public Task<WorkspaceStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceStatus
            {
                UserId = userId,
                State = WorkspaceState.Running,
                WorkspacePath = $"C:\\temp\\workspace\\{userId:N}"
            });

        public bool IsPathAllowed(Guid userId, string path) => true;

        public string ResolvePath(Guid userId, string relativePath) => $"C:\\temp\\workspace\\{userId:N}\\{relativePath}";

        public Task StopAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubMemoryBrainResolver(MemoryBrainResult result) : IMemoryBrainResolver
    {
        public Task<MemoryBrainResult> ResolveAsync(string customerId, string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class StubSubscriptionStore(string planId = "free", string status = "active") : ISubscriptionStore
    {
        public Task<SubscriptionRecord> EnsureFreeSubscriptionAsync(string customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(BuildSubscription(customerId, "free", "active"));

        public Task<SubscriptionRecord?> GetSubscriptionAsync(string customerId, CancellationToken cancellationToken = default)
            => Task.FromResult<SubscriptionRecord?>(BuildSubscription(customerId, planId, status));

        public Task<CustomerBillingProfile?> GetCustomerBillingProfileAsync(string customerId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<string?> FindCustomerIdByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task LinkStripeCustomerAsync(string customerId, string stripeCustomerId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SubscriptionRecord> UpsertSubscriptionAsync(SubscriptionUpsertRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> TryRecordSubscriptionEventAsync(SubscriptionEventRecord record, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task MarkSubscriptionEventProcessedAsync(string stripeEventId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        private static SubscriptionRecord BuildSubscription(string customerId, string planId, string status) =>
            new(
                "sub_test",
                customerId,
                planId,
                status,
                null,
                null,
                1,
                null,
                DateTimeOffset.UtcNow.AddDays(30),
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
    }

    private sealed class StaticDocumentQueryStore(IReadOnlyList<RetrievalResultRecord> results) : IDocumentQueryStore
    {
        public Task<IReadOnlyList<RetrievalResultRecord>> SearchAsync(OqlQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(results);
    }

    private sealed class StubManagedDocumentStore : IManagedDocumentStore
    {
        private readonly Dictionary<string, ManagedDocumentDetail> _documents = new(StringComparer.OrdinalIgnoreCase);
        private int _nextId = 1;

        public List<ManagedDocumentDetail> Documents => _documents.Values.Where(document => !document.IsDeleted).ToList();

        public Task<IReadOnlyList<ManagedDocumentSummary>> ListManagedDocumentsAsync(
            string customerId,
            string brainId,
            string? pathPrefix = null,
            string? excludePathPrefix = null,
            int limit = 200,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ManagedDocumentSummary>>([]);

        public Task<int> CountActiveManagedDocumentsAsync(string customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(_documents.Values.Count(document =>
                string.Equals(document.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
                && !document.IsDeleted));

        public Task<IReadOnlyList<ManagedDocumentDetail>> ListManagedDocumentsForIndexingAsync(string customerId, string brainId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ManagedDocumentDetail>>(_documents.Values.ToList());

        public Task<ManagedDocumentDetail?> GetManagedDocumentAsync(string customerId, string brainId, string managedDocumentId, CancellationToken cancellationToken = default)
            => Task.FromResult(_documents.TryGetValue(managedDocumentId, out var document) ? document : null);

        public Task<ManagedDocumentDetail?> GetManagedDocumentByCanonicalPathAsync(string customerId, string brainId, string canonicalPath, CancellationToken cancellationToken = default)
            => Task.FromResult<ManagedDocumentDetail?>(_documents.Values.FirstOrDefault(document =>
                string.Equals(document.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(document.BrainId, brainId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(document.CanonicalPath, canonicalPath, StringComparison.OrdinalIgnoreCase)
                && !document.IsDeleted));

        public Task<IReadOnlyList<ManagedDocumentVersionSummary>> ListManagedDocumentVersionsAsync(string customerId, string brainId, string managedDocumentId, int limit = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ManagedDocumentVersionSummary>>([]);

        public Task<ManagedDocumentVersionDetail?> GetManagedDocumentVersionAsync(string customerId, string brainId, string managedDocumentId, string managedDocumentVersionId, CancellationToken cancellationToken = default)
            => Task.FromResult<ManagedDocumentVersionDetail?>(null);

        public Task<ManagedDocumentDetail> CreateManagedDocumentAsync(ManagedDocumentCreateRequest request, CancellationToken cancellationToken = default)
        {
            var activeDocuments = _documents.Values.Count(document =>
                string.Equals(document.CustomerId, request.CustomerId, StringComparison.OrdinalIgnoreCase)
                && !document.IsDeleted);
            if (request.MaxActiveDocuments is int maxActiveDocuments && activeDocuments >= maxActiveDocuments)
            {
                throw new ManagedDocumentQuotaExceededException(
                    request.QuotaExceededMessage
                    ?? $"Document limit reached for customer '{request.CustomerId}'.");
            }

            var now = DateTimeOffset.UtcNow;
            var managedDocumentId = $"memory-{_nextId++:D4}";
            var slug = request.Slug ?? "memory";
            var document = new ManagedDocumentDetail(
                managedDocumentId,
                request.BrainId,
                request.CustomerId,
                request.Title,
                slug,
                OpenCortex.Core.Authoring.ManagedDocumentText.BuildCanonicalPath(slug),
                request.Content,
                new Dictionary<string, string>(request.Frontmatter, StringComparer.OrdinalIgnoreCase),
                "hash",
                request.Status,
                request.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                request.UserId,
                request.UserId,
                now,
                now,
                false);

            _documents[managedDocumentId] = document;
            return Task.FromResult(document);
        }

        public Task<ManagedDocumentDetail?> UpdateManagedDocumentAsync(ManagedDocumentUpdateRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> SoftDeleteManagedDocumentAsync(string customerId, string brainId, string managedDocumentId, string userId, CancellationToken cancellationToken = default)
        {
            if (!_documents.TryGetValue(managedDocumentId, out var document))
            {
                return Task.FromResult(false);
            }

            _documents[managedDocumentId] = document with
            {
                IsDeleted = true,
                UpdatedBy = userId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return Task.FromResult(true);
        }

        public Task<ManagedDocumentDetail?> RestoreManagedDocumentVersionAsync(string customerId, string brainId, string managedDocumentId, string managedDocumentVersionId, string userId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubManagedContentBrainIndexingService : IManagedContentBrainIndexingService
    {
        public Task<IndexRunRecord> ReindexAsync(string customerId, string brainId, string triggerType, CancellationToken cancellationToken = default)
            => Task.FromResult(new IndexRunRecord(
                $"idx-{triggerType}",
                brainId,
                triggerType,
                "completed",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                1,
                1,
                0,
                null));
    }
}
