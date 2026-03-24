using System.Security.Claims;
using OpenCortex.Conversations;
using OpenCortex.Orchestration;
using OpenCortex.Orchestration.Execution;
using OpenCortex.Orchestration.Routing;
using OpenCortex.Providers.Abstractions;
using OpenCortex.Core.Persistence;

namespace OpenCortex.Api;

/// <summary>
/// Chat and orchestration API endpoints.
/// </summary>
public static class ChatEndpoints
{
    /// <summary>
    /// Map chat endpoints to the application.
    /// </summary>
    public static void MapChatEndpoints(this WebApplication app, bool requireAuth)
    {
        var chatRoutes = app.MapGroup("/api/chat")
            .RequireRateLimiting("chat-api");

        if (requireAuth)
        {
            chatRoutes.RequireAuthorization();
        }

        if (requireAuth)
        {
            MapAuthenticatedChatEndpoints(chatRoutes);
            return;
        }

        // List available providers
        chatRoutes.MapGet("/providers", (IModelRouter router) =>
        {
            var providers = router.GetProviders();
            return Results.Ok(new
            {
                count = providers.Count,
                providers = providers.Select(p => new
                {
                    providerId = p.ProviderId,
                    name = p.Name,
                    type = p.ProviderType,
                    capabilities = new
                    {
                        p.Capabilities.SupportsChat,
                        p.Capabilities.SupportsCode,
                        p.Capabilities.SupportsVision,
                        p.Capabilities.SupportsTools,
                        p.Capabilities.SupportsStreaming,
                        p.Capabilities.MaxContextTokens,
                        p.Capabilities.MaxOutputTokens
                    }
                })
            });
        });

        // Provider health check
        chatRoutes.MapGet("/providers/{providerId}/health", async (
            string providerId,
            IModelRouter router,
            CancellationToken cancellationToken) =>
        {
            var provider = router.GetProvider(providerId);
            if (provider is null)
            {
                return Results.NotFound(new { message = $"Provider '{providerId}' was not found." });
            }

            var health = await provider.CheckHealthAsync(cancellationToken);
            return Results.Ok(new
            {
                providerId,
                health.IsHealthy,
                health.LatencyMs,
                health.Error,
                checkedAt = health.CheckedAt
            });
        });

        // List models for a provider
        chatRoutes.MapGet("/providers/{providerId}/models", async (
            string providerId,
            IModelRouter router,
            CancellationToken cancellationToken) =>
        {
            var provider = router.GetProvider(providerId);
            if (provider is null)
            {
                return Results.NotFound(new { message = $"Provider '{providerId}' was not found." });
            }

            var models = await provider.ListModelsAsync(cancellationToken);
            return Results.Ok(new
            {
                providerId,
                count = models.Count,
                models
            });
        });

        // Classify a message (for debugging routing)
        chatRoutes.MapPost("/classify", (
            ClassifyRequest request,
            ITaskClassifier classifier) =>
        {
            var classification = classifier.Classify(request.Message);
            return Results.Ok(classification);
        });

        // Route a message (for debugging routing)
        chatRoutes.MapPost("/route", async (
            RouteRequest request,
            IModelRouter router,
            CancellationToken cancellationToken) =>
        {
            var context = new RoutingContext
            {
                RequestedProviderId = NormalizeProviderId(request.ProviderId),
                RequestedModelId = request.ModelId,
                IsPrivate = request.IsPrivate,
                ForceMultiModel = request.ForceMultiModel
            };

            var decision = await router.RouteAsync(request.Message, context, cancellationToken);
            return Results.Ok(decision);
        });

        // Chat completion (non-streaming)
        chatRoutes.MapPost("/completions", async (
            ChatCompletionRequest request,
            IOrchestrationEngine engine,
            CancellationToken cancellationToken) =>
        {
            var orchestrationRequest = new OrchestrationRequest
            {
                Messages = request.Messages.Select(m => new ChatMessage
                {
                    Role = ParseRole(m.Role),
                    Content = m.Content
                }).ToList(),
                SystemMessage = request.SystemMessage,
                RoutingContext = new RoutingContext
                {
                    RequestedProviderId = NormalizeProviderId(request.ProviderId),
                    RequestedModelId = request.ModelId,
                    IsPrivate = request.IsPrivate
                },
                Options = new ChatRequestOptions
                {
                    Temperature = request.Temperature,
                    MaxTokens = request.MaxTokens
                }
            };

            var result = await engine.ExecuteAsync(orchestrationRequest, cancellationToken);

            return Results.Ok(new
            {
                content = result.Completion.Content,
                toolCalls = result.Completion.ToolCalls,
                usage = new
                {
                    result.Completion.Usage.PromptTokens,
                    result.Completion.Usage.CompletionTokens,
                    result.Completion.Usage.TotalTokens
                },
                finishReason = result.Completion.FinishReason.ToString().ToLowerInvariant(),
                providerId = result.ProviderId,
                modelId = result.ModelId,
                latencyMs = result.LatencyMs,
                routing = new
                {
                    category = result.Routing.Classification.Category.ToString().ToLowerInvariant(),
                    confidence = result.Routing.Classification.Confidence,
                    matchedRule = result.Routing.MatchedRule?.Name
                }
            });
        });

        // Chat completion (streaming via SSE)
        chatRoutes.MapPost("/completions/stream", async (
            ChatCompletionRequest request,
            IOrchestrationEngine engine,
            HttpResponse response,
            CancellationToken cancellationToken) =>
        {
            var orchestrationRequest = new OrchestrationRequest
            {
                Messages = request.Messages.Select(m => new ChatMessage
                {
                    Role = ParseRole(m.Role),
                    Content = m.Content
                }).ToList(),
                SystemMessage = request.SystemMessage,
                RoutingContext = new RoutingContext
                {
                    RequestedProviderId = NormalizeProviderId(request.ProviderId),
                    RequestedModelId = request.ModelId,
                    IsPrivate = request.IsPrivate
                },
                Options = new ChatRequestOptions
                {
                    Temperature = request.Temperature,
                    MaxTokens = request.MaxTokens
                }
            };

            await StreamChatCompletionAsync(
                response,
                engine.StreamAsync(orchestrationRequest, cancellationToken),
                cancellationToken);

            return Results.Empty;
        });
    }

    private static void MapAuthenticatedChatEndpoints(RouteGroupBuilder chatRoutes)
    {
        chatRoutes.MapGet("/providers", async (
            ClaimsPrincipal user,
            ITenantCatalogStore catalogStore,
            IUserOrchestrationService orchestrationService,
            CancellationToken cancellationToken) =>
        {
            var resolved = await ResolveAuthenticatedChatContextAsync(user, catalogStore, cancellationToken);
            if (resolved.ErrorResult is not null)
            {
                return resolved.ErrorResult;
            }

            var providers = await orchestrationService.GetProvidersAsync(
                resolved.CustomerGuid!.Value,
                resolved.UserGuid!.Value,
                cancellationToken);
            return Results.Ok(BuildProvidersPayload(providers));
        });

        chatRoutes.MapGet("/providers/{providerId}/health", async (
            string providerId,
            ClaimsPrincipal user,
            ITenantCatalogStore catalogStore,
            IUserOrchestrationService orchestrationService,
            CancellationToken cancellationToken) =>
        {
            var resolved = await ResolveAuthenticatedChatContextAsync(user, catalogStore, cancellationToken);
            if (resolved.ErrorResult is not null)
            {
                return resolved.ErrorResult;
            }

            var provider = await orchestrationService.GetProviderAsync(
                resolved.CustomerGuid!.Value,
                resolved.UserGuid!.Value,
                providerId,
                cancellationToken);
            if (provider is null)
            {
                return Results.NotFound(new { message = $"Provider '{providerId}' was not found for the current user." });
            }

            var health = await provider.CheckHealthAsync(cancellationToken);
            return Results.Ok(new
            {
                providerId,
                health.IsHealthy,
                health.LatencyMs,
                health.Error,
                checkedAt = health.CheckedAt
            });
        });

        chatRoutes.MapGet("/providers/{providerId}/models", async (
            string providerId,
            ClaimsPrincipal user,
            ITenantCatalogStore catalogStore,
            IUserOrchestrationService orchestrationService,
            CancellationToken cancellationToken) =>
        {
            var resolved = await ResolveAuthenticatedChatContextAsync(user, catalogStore, cancellationToken);
            if (resolved.ErrorResult is not null)
            {
                return resolved.ErrorResult;
            }

            var provider = await orchestrationService.GetProviderAsync(
                resolved.CustomerGuid!.Value,
                resolved.UserGuid!.Value,
                providerId,
                cancellationToken);
            if (provider is null)
            {
                return Results.NotFound(new { message = $"Provider '{providerId}' was not found for the current user." });
            }

            var models = await provider.ListModelsAsync(cancellationToken);
            return Results.Ok(new
            {
                providerId,
                count = models.Count,
                models
            });
        });

        chatRoutes.MapPost("/classify", (
            ClassifyRequest request,
            ITaskClassifier classifier) =>
        {
            var classification = classifier.Classify(request.Message);
            return Results.Ok(classification);
        });

        chatRoutes.MapPost("/route", async (
            RouteRequest request,
            ClaimsPrincipal user,
            ITenantCatalogStore catalogStore,
            IUserOrchestrationService orchestrationService,
            CancellationToken cancellationToken) =>
        {
            var resolved = await ResolveAuthenticatedChatContextAsync(user, catalogStore, cancellationToken);
            if (resolved.ErrorResult is not null)
            {
                return resolved.ErrorResult;
            }

            try
            {
                var context = new RoutingContext
                {
                    UserId = resolved.TenantContext!.UserId,
                    BrainId = request.BrainId ?? resolved.TenantContext.BrainId,
                    ConversationId = request.ConversationId,
                    RequestedProviderId = NormalizeProviderId(request.ProviderId),
                    RequestedModelId = request.ModelId,
                    IsPrivate = request.IsPrivate,
                    ForceMultiModel = request.ForceMultiModel,
                    PreviousProviderId = request.PreviousProviderId
                };

                var decision = await orchestrationService.RouteAsync(
                    resolved.CustomerGuid!.Value,
                    resolved.UserGuid!.Value,
                    request.Message,
                    context,
                    cancellationToken);

                return Results.Ok(decision);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new
                {
                    message = ErrorMessages.ForExternalFailure("Request could not be completed.", ex.Message)
                });
            }
        });

        chatRoutes.MapPost("/completions", async (
            ChatCompletionRequest request,
            ClaimsPrincipal user,
            ITenantCatalogStore catalogStore,
            IUserOrchestrationService orchestrationService,
            IConversationService conversationService,
            IConversationRepository conversationRepository,
            CancellationToken cancellationToken) =>
        {
            var resolved = await ResolveAuthenticatedChatContextAsync(user, catalogStore, cancellationToken);
            if (resolved.ErrorResult is not null)
            {
                return resolved.ErrorResult;
            }

            try
            {
                var conversationError = await ValidateConversationAccessAsync(
                    request.ConversationId,
                    resolved.TenantContext!.CustomerId,
                    resolved.TenantContext.UserId,
                    conversationRepository,
                    cancellationToken);
                if (conversationError is not null)
                {
                    return conversationError;
                }

                var userMessageContent = GetLatestUserMessageContent(request);
                if (!string.IsNullOrWhiteSpace(request.ConversationId) && !string.IsNullOrWhiteSpace(userMessageContent))
                {
                    await conversationService.AddUserMessageAsync(
                        request.ConversationId,
                        userMessageContent,
                        cancellationToken);
                }

                var orchestrationRequest = BuildOrchestrationRequest(request, resolved.TenantContext);
                var result = await orchestrationService.ExecuteAsync(
                    resolved.CustomerGuid!.Value,
                    resolved.UserGuid!.Value,
                    orchestrationRequest,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(request.ConversationId))
                {
                    await conversationService.AddAssistantMessageAsync(
                        request.ConversationId,
                        result.Completion,
                        result.ProviderId,
                        result.LatencyMs,
                        cancellationToken);
                }

                return Results.Ok(BuildCompletionPayload(result));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new
                {
                    message = ErrorMessages.ForExternalFailure("Request could not be completed.", ex.Message)
                });
            }
        });

        chatRoutes.MapPost("/completions/stream", async (
            ChatCompletionRequest request,
            ClaimsPrincipal user,
            ITenantCatalogStore catalogStore,
            IUserOrchestrationService orchestrationService,
            IConversationService conversationService,
            IConversationRepository conversationRepository,
            HttpResponse response,
            CancellationToken cancellationToken) =>
        {
            var resolved = await ResolveAuthenticatedChatContextAsync(user, catalogStore, cancellationToken);
            if (resolved.ErrorResult is not null)
            {
                return resolved.ErrorResult;
            }

            try
            {
                var conversationError = await ValidateConversationAccessAsync(
                    request.ConversationId,
                    resolved.TenantContext!.CustomerId,
                    resolved.TenantContext.UserId,
                    conversationRepository,
                    cancellationToken);
                if (conversationError is not null)
                {
                    return conversationError;
                }

                var userMessageContent = GetLatestUserMessageContent(request);
                if (!string.IsNullOrWhiteSpace(request.ConversationId) && !string.IsNullOrWhiteSpace(userMessageContent))
                {
                    await conversationService.AddUserMessageAsync(
                        request.ConversationId,
                        userMessageContent,
                        cancellationToken);
                }

                var orchestrationRequest = BuildOrchestrationRequest(request, resolved.TenantContext);

                var streamResult = await StreamChatCompletionAsync(
                    response,
                    orchestrationService.StreamAsync(
                        resolved.CustomerGuid!.Value,
                        resolved.UserGuid!.Value,
                        orchestrationRequest,
                        cancellationToken),
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(request.ConversationId) && streamResult.Succeeded)
                {
                    await conversationService.AddAssistantMessageAsync(
                        request.ConversationId,
                        streamResult.ToChatCompletion(),
                        streamResult.ProviderId ?? "unknown",
                        streamResult.LatencyMs,
                        cancellationToken);
                }

                return Results.Empty;
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new
                {
                    message = ErrorMessages.ForExternalFailure("Request could not be completed.", ex.Message)
                });
            }
        });

        // Agentic chat completion (with tool execution)
        chatRoutes.MapPost("/completions/agentic", async (
            ChatCompletionRequest request,
            ClaimsPrincipal user,
            ITenantCatalogStore catalogStore,
            IAgenticOrchestrationEngine agenticEngine,
            IUserOrchestrationService orchestrationService,
            IConversationService conversationService,
            IConversationRepository conversationRepository,
            OpenCortex.Core.Credentials.IUserCredentialService credentialService,
            CancellationToken cancellationToken) =>
        {
            var resolved = await ResolveAuthenticatedChatContextAsync(user, catalogStore, cancellationToken);
            if (resolved.ErrorResult is not null)
            {
                return resolved.ErrorResult;
            }

            try
            {
                var conversationError = await ValidateConversationAccessAsync(
                    request.ConversationId,
                    resolved.TenantContext!.CustomerId,
                    resolved.TenantContext.UserId,
                    conversationRepository,
                    cancellationToken);
                if (conversationError is not null)
                {
                    return conversationError;
                }

                var providerCapabilityError = await ValidateAgenticProviderSelectionAsync(
                    request.ProviderId,
                    resolved.CustomerGuid!.Value,
                    resolved.UserGuid!.Value,
                    orchestrationService,
                    cancellationToken);
                if (providerCapabilityError is not null)
                {
                    return providerCapabilityError;
                }

                var userMessageContent = GetLatestUserMessageContent(request);
                if (!string.IsNullOrWhiteSpace(request.ConversationId) && !string.IsNullOrWhiteSpace(userMessageContent))
                {
                    await conversationService.AddUserMessageAsync(
                        request.ConversationId,
                        userMessageContent,
                        cancellationToken);
                }

                // Load user credentials for tool execution
                var credentials = await credentialService.GetDecryptedCredentialsAsync(
                    resolved.CustomerGuid!.Value,
                    resolved.UserGuid!.Value, cancellationToken);

                var agenticRequest = BuildAgenticRequest(request, resolved, credentials);
                var result = await agenticEngine.ExecuteAgenticAsync(agenticRequest, cancellationToken);

                if (!string.IsNullOrWhiteSpace(request.ConversationId))
                {
                    await conversationService.AddAssistantMessageAsync(
                        request.ConversationId,
                        result.Completion,
                        result.ProviderId,
                        (int)result.Duration.TotalMilliseconds,
                        cancellationToken);
                }

                return Results.Ok(BuildAgenticCompletionPayload(result));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new
                {
                    message = ErrorMessages.ForExternalFailure("Request could not be completed.", ex.Message)
                });
            }
        });

        // Agentic chat completion (streaming with tool execution events)
        chatRoutes.MapPost("/completions/agentic/stream", async (
            ChatCompletionRequest request,
            ClaimsPrincipal user,
            ITenantCatalogStore catalogStore,
            IAgenticOrchestrationEngine agenticEngine,
            IUserOrchestrationService orchestrationService,
            IConversationService conversationService,
            IConversationRepository conversationRepository,
            OpenCortex.Core.Credentials.IUserCredentialService credentialService,
            HttpResponse response,
            CancellationToken cancellationToken) =>
        {
            var resolved = await ResolveAuthenticatedChatContextAsync(user, catalogStore, cancellationToken);
            if (resolved.ErrorResult is not null)
            {
                return resolved.ErrorResult;
            }

            try
            {
                var conversationError = await ValidateConversationAccessAsync(
                    request.ConversationId,
                    resolved.TenantContext!.CustomerId,
                    resolved.TenantContext.UserId,
                    conversationRepository,
                    cancellationToken);
                if (conversationError is not null)
                {
                    return conversationError;
                }

                var providerCapabilityError = await ValidateAgenticProviderSelectionAsync(
                    request.ProviderId,
                    resolved.CustomerGuid!.Value,
                    resolved.UserGuid!.Value,
                    orchestrationService,
                    cancellationToken);
                if (providerCapabilityError is not null)
                {
                    return providerCapabilityError;
                }

                var userMessageContent = GetLatestUserMessageContent(request);
                if (!string.IsNullOrWhiteSpace(request.ConversationId) && !string.IsNullOrWhiteSpace(userMessageContent))
                {
                    await conversationService.AddUserMessageAsync(
                        request.ConversationId,
                        userMessageContent,
                        cancellationToken);
                }

                // Load user credentials for tool execution
                var credentials = await credentialService.GetDecryptedCredentialsAsync(
                    resolved.CustomerGuid!.Value,
                    resolved.UserGuid!.Value, cancellationToken);

                var agenticRequest = BuildAgenticRequest(request, resolved, credentials);

                var streamResult = await StreamAgenticCompletionAsync(
                    response,
                    agenticEngine.StreamAgenticAsync(agenticRequest, cancellationToken),
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(request.ConversationId) && streamResult.Succeeded)
                {
                    await conversationService.AddAssistantMessageAsync(
                        request.ConversationId,
                        streamResult.ToChatCompletion(),
                        streamResult.ProviderId ?? "unknown",
                        streamResult.LatencyMs,
                        cancellationToken);
                }

                return Results.Empty;
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new
                {
                    message = ErrorMessages.ForExternalFailure("Request could not be completed.", ex.Message)
                });
            }
        });
    }

    private static async Task<(Guid? CustomerGuid, Guid? UserGuid, OpenCortex.Core.Tenancy.TenantContext? TenantContext, IResult? ErrorResult)> ResolveAuthenticatedChatContextAsync(
        ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken)
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return (null, null, null, errorResult);
        }

        if (context is null || string.IsNullOrWhiteSpace(context.ExternalId) || string.IsNullOrWhiteSpace(context.CustomerId))
        {
            return (null, null, null, Results.Problem(
                title: "Invalid authenticated user profile",
                detail: "Authenticated token is missing a stable user or customer identifier.",
                statusCode: StatusCodes.Status401Unauthorized));
        }

        return (GuidFromString(context.CustomerId), GuidFromString(context.ExternalId), context, null);
    }

    private static object BuildProvidersPayload(IReadOnlyList<IModelProvider> providers) => new
    {
        count = providers.Count,
        providers = providers.Select(p => new
        {
            providerId = p.ProviderId,
            name = p.Name,
            type = p.ProviderType,
            capabilities = new
            {
                p.Capabilities.SupportsChat,
                p.Capabilities.SupportsCode,
                p.Capabilities.SupportsVision,
                p.Capabilities.SupportsTools,
                p.Capabilities.SupportsStreaming,
                p.Capabilities.MaxContextTokens,
                p.Capabilities.MaxOutputTokens
            }
        })
    };

    private static object BuildCompletionPayload(OrchestrationResult result) => new
    {
        content = result.Completion.Content,
        toolCalls = result.Completion.ToolCalls,
        usage = new
        {
            result.Completion.Usage.PromptTokens,
            result.Completion.Usage.CompletionTokens,
            result.Completion.Usage.TotalTokens
        },
        finishReason = result.Completion.FinishReason.ToString().ToLowerInvariant(),
        providerId = result.ProviderId,
        modelId = result.ModelId,
        latencyMs = result.LatencyMs,
        routing = new
        {
            category = result.Routing.Classification.Category.ToString().ToLowerInvariant(),
            confidence = result.Routing.Classification.Confidence,
            matchedRule = result.Routing.MatchedRule?.Name
        }
    };

    private static OrchestrationRequest BuildOrchestrationRequest(
        ChatCompletionRequest request,
        OpenCortex.Core.Tenancy.TenantContext? context = null)
    {
        return new OrchestrationRequest
        {
            Messages = request.Messages.Select(m => new ChatMessage
            {
                Role = ParseRole(m.Role),
                Content = m.Content
            }).ToList(),
            SystemMessage = request.SystemMessage,
            RoutingContext = new RoutingContext
            {
                UserId = context?.UserId,
                BrainId = request.BrainId ?? context?.BrainId,
                ConversationId = request.ConversationId,
                PreviousProviderId = request.PreviousProviderId,
                RequestedProviderId = NormalizeProviderId(request.ProviderId),
                RequestedModelId = request.ModelId,
                IsPrivate = request.IsPrivate
            },
            Options = new ChatRequestOptions
            {
                Temperature = request.Temperature,
                MaxTokens = request.MaxTokens
            }
        };
    }

    private static async Task<StreamChatCompletionResult> StreamChatCompletionAsync(
        HttpResponse response,
        IAsyncEnumerable<OrchestrationStreamChunk> stream,
        CancellationToken cancellationToken)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        using var writeLock = new SemaphoreSlim(1, 1);
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var fullContent = new System.Text.StringBuilder();
        string? providerId = null;
        string? modelId = null;
        var finishReason = FinishReason.Other;
        var usage = TokenUsage.Empty;
        var succeeded = false;

        var heartbeatProviderId = (string?)null;
        var heartbeatStage = "routing";
        var heartbeatMessage = "Choosing provider...";

        await WriteChatStreamEventAsync(response, new
            {
                eventType = "status",
                stage = heartbeatStage,
                message = heartbeatMessage,
            timestamp = DateTimeOffset.UtcNow
        }, writeLock, cancellationToken);

        var heartbeatTask = RunHeartbeatLoopAsync(
            response,
            writeLock,
            () => new
            {
                eventType = "heartbeat",
                stage = heartbeatStage,
                message = heartbeatMessage,
                providerId = heartbeatProviderId,
                timestamp = DateTimeOffset.UtcNow
            },
            heartbeatCts.Token);

        try
        {
            var announcedProvider = false;
            var announcedStreaming = false;
            string? activeProviderId = null;

            await foreach (var chunk in stream)
            {
                providerId = chunk.ProviderId;
                modelId ??= chunk.Chunk.Model;

                if (!announcedProvider && chunk.Routing is not null)
                {
                    announcedProvider = true;
                    activeProviderId = chunk.ProviderId;
                    heartbeatProviderId = chunk.ProviderId;
                    heartbeatStage = "waiting";
                    heartbeatMessage = $"Waiting on {chunk.ProviderId} to start responding...";

                    await WriteChatStreamEventAsync(response, new
                    {
                        eventType = "status",
                        stage = "provider",
                        message = BuildProviderSelectedMessage(chunk),
                        providerId = chunk.ProviderId,
                        routing = new
                        {
                            category = chunk.Routing.Classification.Category.ToString().ToLowerInvariant(),
                            confidence = chunk.Routing.Classification.Confidence
                        },
                        timestamp = DateTimeOffset.UtcNow
                    }, writeLock, cancellationToken);
                }

                if (!announcedStreaming && !string.IsNullOrEmpty(chunk.Chunk.ContentDelta))
                {
                    announcedStreaming = true;
                    activeProviderId ??= chunk.ProviderId;
                    heartbeatProviderId = activeProviderId;
                    heartbeatStage = "streaming";
                    heartbeatMessage = $"Streaming response from {activeProviderId}...";

                    await WriteChatStreamEventAsync(response, new
                    {
                        eventType = "status",
                        stage = heartbeatStage,
                        message = heartbeatMessage,
                        providerId = activeProviderId,
                        timestamp = DateTimeOffset.UtcNow
                    }, writeLock, cancellationToken);
                }

                if (!string.IsNullOrEmpty(chunk.Chunk.ContentDelta))
                {
                    fullContent.Append(chunk.Chunk.ContentDelta);
                }

                if (chunk.Chunk.FinalUsage is not null)
                {
                    usage = chunk.Chunk.FinalUsage;
                }

                if (chunk.Chunk.FinishReason.HasValue)
                {
                    finishReason = chunk.Chunk.FinishReason.Value;
                }

                await WriteChatStreamEventAsync(response, BuildContentEvent(chunk), writeLock, cancellationToken);

                if (chunk.Chunk.IsComplete)
                {
                    heartbeatProviderId = activeProviderId ?? chunk.ProviderId;
                    heartbeatStage = "complete";
                    heartbeatMessage = "Response complete.";
                    succeeded = true;
                }
            }

            await WriteChatStreamEventAsync(response, new
            {
                eventType = "status",
                stage = "complete",
                message = "Response complete.",
                providerId = heartbeatProviderId,
                timestamp = DateTimeOffset.UtcNow
            }, writeLock, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var safeMessage = ErrorMessages.ForExternalFailure("Streaming failed.", ex.Message);
            heartbeatStage = "error";
            heartbeatMessage = safeMessage;

            await WriteChatStreamEventAsync(response, new
            {
                eventType = "error",
                stage = heartbeatStage,
                message = safeMessage,
                providerId = heartbeatProviderId,
                timestamp = DateTimeOffset.UtcNow
            }, writeLock, cancellationToken);
        }
        finally
        {
            heartbeatCts.Cancel();

            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }

            await WriteDoneEventAsync(response, writeLock, cancellationToken);
            stopwatch.Stop();
        }

        return new StreamChatCompletionResult(
            succeeded,
            fullContent.ToString(),
            providerId,
            modelId,
            finishReason,
            usage,
            (int)stopwatch.ElapsedMilliseconds);
    }

    private static object BuildContentEvent(OrchestrationStreamChunk chunk) => new
    {
        eventType = "content",
        contentDelta = chunk.Chunk.ContentDelta,
        isComplete = chunk.Chunk.IsComplete,
        finishReason = chunk.Chunk.FinishReason?.ToString().ToLowerInvariant(),
        providerId = chunk.ProviderId,
        routing = chunk.Routing is not null ? new
        {
            category = chunk.Routing.Classification.Category.ToString().ToLowerInvariant(),
            confidence = chunk.Routing.Classification.Confidence
        } : null
    };

    private static string BuildProviderSelectedMessage(OrchestrationStreamChunk chunk)
    {
        var category = chunk.Routing?.Classification.Category.ToString().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(category)
            ? $"Using {chunk.ProviderId}."
            : $"Using {chunk.ProviderId} for {category}.";
    }

    private static async Task RunHeartbeatLoopAsync(
        HttpResponse response,
        SemaphoreSlim writeLock,
        Func<object> buildHeartbeatEvent,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(4));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await WriteChatStreamEventAsync(response, buildHeartbeatEvent(), writeLock, cancellationToken);
        }
    }

    private static async Task WriteChatStreamEventAsync(
        HttpResponse response,
        object payload,
        SemaphoreSlim writeLock,
        CancellationToken cancellationToken)
    {
        var data = System.Text.Json.JsonSerializer.Serialize(payload);

        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await response.WriteAsync($"data: {data}\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static async Task WriteDoneEventAsync(
        HttpResponse response,
        SemaphoreSlim writeLock,
        CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static async Task<IResult?> ValidateConversationAccessAsync(
        string? conversationId,
        string customerId,
        string userId,
        IConversationRepository conversationRepository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        var conversation = await conversationRepository.GetByIdAsync(conversationId, cancellationToken);
        if (conversation is null
            || !string.Equals(conversation.CustomerId, customerId, StringComparison.Ordinal)
            || !string.Equals(conversation.UserId, userId, StringComparison.Ordinal))
        {
            return Results.NotFound(new { message = $"Conversation '{conversationId}' was not found." });
        }

        if (conversation.Status != ConversationStatus.Active)
        {
            return Results.BadRequest(new { message = $"Conversation '{conversationId}' is not active." });
        }

        return null;
    }

    private static async Task<IResult?> ValidateAgenticProviderSelectionAsync(
        string? providerId,
        Guid customerGuid,
        Guid userGuid,
        IUserOrchestrationService orchestrationService,
        CancellationToken cancellationToken)
    {
        var normalizedProviderId = NormalizeProviderId(providerId);
        if (string.IsNullOrWhiteSpace(normalizedProviderId))
        {
            return null;
        }

        var provider = await orchestrationService.GetProviderAsync(
            customerGuid,
            userGuid,
            normalizedProviderId,
            cancellationToken);
        if (provider is null)
        {
            return Results.NotFound(new { message = $"Provider '{normalizedProviderId}' was not found for the current user." });
        }

        if (!provider.Capabilities.SupportsTools)
        {
            return Results.BadRequest(new
            {
                message = $"Provider '{normalizedProviderId}' does not support agentic tool execution. Use standard chat or choose a tool-capable provider."
            });
        }

        return null;
    }

    private static string? GetLatestUserMessageContent(ChatCompletionRequest request) =>
        request.Messages
            .LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
            ?.Content
            ?.Trim();

    private static Guid GuidFromString(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }

    private static ChatRole ParseRole(string role)
    {
        return role.ToLowerInvariant() switch
        {
            "system" => ChatRole.System,
            "user" => ChatRole.User,
            "assistant" => ChatRole.Assistant,
            "tool" => ChatRole.Tool,
            _ => ChatRole.User
        };
    }

    private static AgenticOrchestrationRequest BuildAgenticRequest(
        ChatCompletionRequest request,
        (Guid? CustomerGuid, Guid? UserGuid, OpenCortex.Core.Tenancy.TenantContext? TenantContext, IResult? ErrorResult) resolved,
        IReadOnlyDictionary<string, string>? credentials)
    {
        return new AgenticOrchestrationRequest
        {
            UserId = resolved.UserGuid!.Value,
            CustomerId = resolved.CustomerGuid!.Value,
            ConversationId = request.ConversationId ?? Guid.NewGuid().ToString(),
            Messages = request.Messages.Select(m => new ChatMessage
            {
                Role = ParseRole(m.Role),
                Content = m.Content
            }).ToList(),
            SystemMessage = BuildAgenticSystemMessage(request),
            EnabledTools = request.EnabledTools,
            EnabledCategories = request.EnabledCategories,
            MaxIterations = request.MaxToolIterations ?? 25,
            RoutingContext = new RoutingContext
            {
                CustomerId = resolved.TenantContext!.CustomerId,
                UserId = resolved.TenantContext.UserId,
                BrainId = request.BrainId ?? resolved.TenantContext.BrainId,
                ConversationId = request.ConversationId,
                PreviousProviderId = request.PreviousProviderId,
                RequestedProviderId = NormalizeProviderId(request.ProviderId),
                RequestedModelId = request.ModelId,
                IsPrivate = request.IsPrivate
            },
            Options = new ChatRequestOptions
            {
                Temperature = request.Temperature,
                MaxTokens = request.MaxTokens
            },
            Credentials = credentials
        };
    }

    private static string? BuildAgenticSystemMessage(ChatCompletionRequest request)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.SystemMessage))
        {
            parts.Add(request.SystemMessage.Trim());
        }

        if (ShouldIncludeMemoryToolGuidance(request))
        {
            parts.Add("""
                Agent memory tools are available in this session.
                Use recall_memories when prior user preferences, decisions, facts, or learnings may matter.
                Use save_memory only for durable information worth keeping beyond this conversation.
                Use forget_memory when a saved memory is stale, incorrect, or superseded.
                Do not save transient chatter or one-off details unless they are likely to matter later.
                """);
        }

        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    private static bool ShouldIncludeMemoryToolGuidance(ChatCompletionRequest request)
    {
        if (request.EnabledTools is { Count: > 0 })
        {
            return request.EnabledTools.Any(IsMemoryToolName);
        }

        if (request.EnabledCategories is { Count: > 0 })
        {
            return request.EnabledCategories.Any(category =>
                string.Equals(category, "memory", StringComparison.OrdinalIgnoreCase));
        }

        return request.EnableTools || (request.EnabledTools is null && request.EnabledCategories is null);
    }

    private static bool IsMemoryToolName(string? toolName) =>
        string.Equals(toolName, "save_memory", StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, "recall_memories", StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, "forget_memory", StringComparison.OrdinalIgnoreCase);

    private static object BuildAgenticCompletionPayload(AgenticOrchestrationResult result) => new
    {
        content = result.Completion.Content,
        toolCalls = result.Completion.ToolCalls,
        usage = new
        {
            result.Completion.Usage.PromptTokens,
            result.Completion.Usage.CompletionTokens,
            result.Completion.Usage.TotalTokens
        },
        finishReason = result.Completion.FinishReason.ToString().ToLowerInvariant(),
        providerId = result.ProviderId,
        modelId = result.ModelId,
        durationMs = (int)result.Duration.TotalMilliseconds,
        iterations = result.Iterations,
        reachedMaxIterations = result.ReachedMaxIterations,
        toolExecutions = result.ToolExecutions.Select(t => new
        {
            t.ToolCallId,
            t.ToolName,
            t.Success,
            t.Output,
            error = string.IsNullOrWhiteSpace(t.Error)
                ? null
                : ErrorMessages.ForExternalFailure("Tool execution failed.", t.Error),
            durationMs = (int)t.Duration.TotalMilliseconds
        }),
        error = string.IsNullOrWhiteSpace(result.Error)
            ? null
            : ErrorMessages.ForExternalFailure("Request could not be completed.", result.Error),
        telemetry = result.Telemetry is not null ? new
        {
            traceId = result.Telemetry.TraceId,
            startedAt = result.Telemetry.StartedAt,
            completedAt = result.Telemetry.CompletedAt,
            totalDurationMs = (int)result.Telemetry.TotalDuration.TotalMilliseconds,
            llmDurationMs = (int)result.Telemetry.LlmDuration.TotalMilliseconds,
            toolDurationMs = (int)result.Telemetry.ToolDuration.TotalMilliseconds,
            tokenUsage = new
            {
                totalPromptTokens = result.Telemetry.TokenUsage.TotalPromptTokens,
                totalCompletionTokens = result.Telemetry.TokenUsage.TotalCompletionTokens,
                totalTokens = result.Telemetry.TokenUsage.TotalTokens,
                byIteration = result.Telemetry.TokenUsage.ByIteration.Select(i => new
                {
                    iteration = i.Iteration,
                    promptTokens = i.PromptTokens,
                    completionTokens = i.CompletionTokens,
                    totalTokens = i.TotalTokens,
                    model = i.Model
                })
            },
            iterations = result.Telemetry.Iterations.Select(i => new
            {
                iteration = i.Iteration,
                startedAt = i.StartedAt,
                llmDurationMs = (int)i.LlmDuration.TotalMilliseconds,
                toolDurationMs = (int)i.ToolDuration.TotalMilliseconds,
                totalDurationMs = (int)i.TotalDuration.TotalMilliseconds,
                tokenUsage = new
                {
                    promptTokens = i.TokenUsage.PromptTokens,
                    completionTokens = i.TokenUsage.CompletionTokens,
                    totalTokens = i.TokenUsage.TotalTokens
                },
                toolCalls = i.ToolCallNames,
                hasToolCalls = i.HasToolCalls,
                contentLength = i.ContentLength,
                finishReason = i.FinishReason
            }),
            toolExecutions = result.Telemetry.ToolExecutions.Select(t => new
            {
                toolCallId = t.ToolCallId,
                toolName = t.ToolName,
                iteration = t.Iteration,
                startedAt = t.StartedAt,
                durationMs = (int)t.Duration.TotalMilliseconds,
                success = t.Success,
                error = string.IsNullOrWhiteSpace(t.Error)
                    ? null
                    : ErrorMessages.ForExternalFailure("Tool execution failed.", t.Error),
                inputSize = t.InputSize,
                outputSize = t.OutputSize,
                category = t.Category
            }),
            llmCallCount = result.Telemetry.LlmCallCount,
            toolCallCount = result.Telemetry.ToolCallCount,
            success = result.Telemetry.Success
        } : null
    };

    private static async Task<StreamChatCompletionResult> StreamAgenticCompletionAsync(
        HttpResponse response,
        IAsyncEnumerable<AgenticStreamEvent> stream,
        CancellationToken cancellationToken)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        using var writeLock = new SemaphoreSlim(1, 1);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var fullContent = new System.Text.StringBuilder();
        string? providerId = null;
        string? modelId = null;
        var finishReason = FinishReason.Other;
        var usage = TokenUsage.Empty;
        var succeeded = false;

        await WriteChatStreamEventAsync(response, new
        {
            eventType = "status",
            stage = "starting",
            message = "Starting agentic execution...",
            timestamp = DateTimeOffset.UtcNow
        }, writeLock, cancellationToken);

        try
        {
            await foreach (var evt in stream)
            {
                switch (evt)
                {
                    case AgenticWorkspaceProvisioningEvent wsProvisioningEvent:
                        await WriteChatStreamEventAsync(response, new
                        {
                            eventType = "workspace_provisioning",
                            status = wsProvisioningEvent.Status,
                            message = wsProvisioningEvent.Message,
                            traceId = wsProvisioningEvent.TraceId,
                            timestamp = wsProvisioningEvent.Timestamp
                        }, writeLock, cancellationToken);
                        break;

                    case AgenticWorkspaceReadyEvent wsReadyEvent:
                        await WriteChatStreamEventAsync(response, new
                        {
                            eventType = "workspace_ready",
                            podName = wsReadyEvent.PodName,
                            containerId = wsReadyEvent.ContainerId,
                            startupDurationMs = (int)wsReadyEvent.StartupDuration.TotalMilliseconds,
                            traceId = wsReadyEvent.TraceId,
                            timestamp = wsReadyEvent.Timestamp
                        }, writeLock, cancellationToken);
                        break;

                    case AgenticWorkspaceErrorEvent wsErrorEvent:
                        var safeWorkspaceError = ErrorMessages.ForExternalFailure(
                            "Workspace failed.",
                            wsErrorEvent.Error);
                        await WriteChatStreamEventAsync(response, new
                        {
                            eventType = "workspace_error",
                            error = safeWorkspaceError,
                            retryable = wsErrorEvent.Retryable,
                            traceId = wsErrorEvent.TraceId,
                            timestamp = wsErrorEvent.Timestamp
                        }, writeLock, cancellationToken);
                        break;

                    case AgenticIterationStartEvent iterStartEvent:
                        await WriteChatStreamEventAsync(response, new
                        {
                            eventType = "iteration_start",
                            iteration = iterStartEvent.Iteration,
                            traceId = iterStartEvent.TraceId,
                            timestamp = iterStartEvent.Timestamp
                        }, writeLock, cancellationToken);
                        break;

                    case AgenticTextEvent textEvent:
                        fullContent.Append(textEvent.Content);
                        await WriteChatStreamEventAsync(response, new
                        {
                            eventType = "content",
                            contentDelta = textEvent.Content,
                            iteration = textEvent.Iteration,
                            timestamp = textEvent.Timestamp
                        }, writeLock, cancellationToken);
                        break;

                    case AgenticIterationCompleteEvent iterCompleteEvent:
                        await WriteChatStreamEventAsync(response, new
                        {
                            eventType = "iteration_complete",
                            iteration = iterCompleteEvent.Iteration,
                            durationMs = (int)iterCompleteEvent.Duration.TotalMilliseconds,
                            tokenUsage = new
                            {
                                promptTokens = iterCompleteEvent.TokenUsage.PromptTokens,
                                completionTokens = iterCompleteEvent.TokenUsage.CompletionTokens,
                                totalTokens = iterCompleteEvent.TokenUsage.TotalTokens
                            },
                            hasToolCalls = iterCompleteEvent.HasToolCalls,
                            timestamp = iterCompleteEvent.Timestamp
                        }, writeLock, cancellationToken);
                        break;

                    case AgenticToolCallStartEvent toolCallEvent:
                        await WriteChatStreamEventAsync(response, new
                        {
                            eventType = "tool_calls",
                            toolCalls = toolCallEvent.ToolCalls.Select(tc => new
                            {
                                tc.Id,
                                tc.Function.Name,
                                tc.Function.Arguments
                            }),
                            iteration = toolCallEvent.Iteration,
                            timestamp = toolCallEvent.Timestamp
                        }, writeLock, cancellationToken);
                        break;

                    case AgenticToolResultEvent toolResultEvent:
                        var safeToolError = string.IsNullOrWhiteSpace(toolResultEvent.Result.Error)
                            ? null
                            : ErrorMessages.ForExternalFailure(
                                "Tool execution failed.",
                                toolResultEvent.Result.Error);
                        await WriteChatStreamEventAsync(response, new
                        {
                            eventType = "tool_result",
                            toolCallId = toolResultEvent.Result.ToolCallId,
                            toolName = toolResultEvent.Result.ToolName,
                            success = toolResultEvent.Result.Success,
                            output = toolResultEvent.Result.Output,
                            error = safeToolError,
                            durationMs = (int)toolResultEvent.Result.Duration.TotalMilliseconds,
                            iteration = toolResultEvent.Iteration,
                            timestamp = toolResultEvent.Timestamp
                        }, writeLock, cancellationToken);
                        break;

                    case AgenticCompleteEvent completeEvent:
                        providerId = completeEvent.Result.ProviderId;
                        modelId = completeEvent.Result.ModelId;
                        finishReason = completeEvent.Result.Completion.FinishReason;
                        usage = completeEvent.Result.Completion.Usage;
                        succeeded = string.IsNullOrEmpty(completeEvent.Result.Error);

                        var telemetry = completeEvent.Result.Telemetry;
                        await WriteChatStreamEventAsync(response, new
                        {
                            eventType = "complete",
                            providerId = completeEvent.Result.ProviderId,
                            modelId = completeEvent.Result.ModelId,
                            iterations = completeEvent.Result.Iterations,
                            reachedMaxIterations = completeEvent.Result.ReachedMaxIterations,
                            durationMs = (int)completeEvent.Result.Duration.TotalMilliseconds,
                            toolExecutionCount = completeEvent.Result.ToolExecutions.Count,
                            timestamp = completeEvent.Timestamp,
                            telemetry = telemetry is not null ? new
                            {
                                traceId = telemetry.TraceId,
                                totalDurationMs = (int)telemetry.TotalDuration.TotalMilliseconds,
                                llmDurationMs = (int)telemetry.LlmDuration.TotalMilliseconds,
                                toolDurationMs = (int)telemetry.ToolDuration.TotalMilliseconds,
                                tokenUsage = new
                                {
                                    totalPromptTokens = telemetry.TokenUsage.TotalPromptTokens,
                                    totalCompletionTokens = telemetry.TokenUsage.TotalCompletionTokens,
                                    totalTokens = telemetry.TokenUsage.TotalTokens
                                },
                                llmCallCount = telemetry.LlmCallCount,
                                toolCallCount = telemetry.ToolCallCount,
                                success = telemetry.Success
                            } : null
                        }, writeLock, cancellationToken);
                        break;

                    case AgenticErrorEvent errorEvent:
                        var safeAgenticError = ErrorMessages.ForExternalFailure(
                            "Request could not be completed.",
                            errorEvent.Error);
                        await WriteChatStreamEventAsync(response, new
                        {
                            eventType = "error",
                            error = safeAgenticError,
                            iteration = errorEvent.Iteration,
                            traceId = errorEvent.TraceId,
                            timestamp = errorEvent.Timestamp
                        }, writeLock, cancellationToken);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var safeError = ErrorMessages.ForExternalFailure("Streaming failed.", ex.Message);
            await WriteChatStreamEventAsync(response, new
            {
                eventType = "error",
                error = safeError,
                timestamp = DateTimeOffset.UtcNow
            }, writeLock, cancellationToken);
        }
        finally
        {
            await WriteDoneEventAsync(response, writeLock, cancellationToken);
            stopwatch.Stop();
        }

        return new StreamChatCompletionResult(
            succeeded,
            fullContent.ToString(),
            providerId,
            modelId,
            finishReason,
            usage,
            (int)stopwatch.ElapsedMilliseconds);
    }

    private static string? NormalizeProviderId(string? providerId) =>
        string.Equals(providerId?.Trim(), "ollama-remote", StringComparison.OrdinalIgnoreCase)
            ? "ollama"
            : providerId?.Trim();
}

// Request DTOs

internal sealed record ClassifyRequest(string Message);

internal sealed record RouteRequest(
    string Message,
    string? BrainId = null,
    string? ConversationId = null,
    string? ProviderId = null,
    string? ModelId = null,
    string? PreviousProviderId = null,
    bool IsPrivate = false,
    bool ForceMultiModel = false);

internal sealed record ChatCompletionRequest(
    IReadOnlyList<ChatMessageDto> Messages,
    string? SystemMessage = null,
    string? BrainId = null,
    string? ConversationId = null,
    string? PreviousProviderId = null,
    string? ProviderId = null,
    string? ModelId = null,
    double? Temperature = null,
    int? MaxTokens = null,
    bool IsPrivate = false,
    bool EnableTools = false,
    IReadOnlyList<string>? EnabledTools = null,
    IReadOnlyList<string>? EnabledCategories = null,
    int? MaxToolIterations = null);

internal sealed record ChatMessageDto(string Role, string Content);

internal sealed record StreamChatCompletionResult(
    bool Succeeded,
    string Content,
    string? ProviderId,
    string? ModelId,
    FinishReason FinishReason,
    TokenUsage Usage,
    int LatencyMs)
{
    public ChatCompletion ToChatCompletion() => new()
    {
        Content = Content,
        Usage = Usage,
        FinishReason = FinishReason,
        Model = ModelId ?? string.Empty
    };
}
