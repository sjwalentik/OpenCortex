using OpenCortex.Orchestration.Execution;
using OpenCortex.Orchestration.Routing;
using OpenCortex.Providers.Abstractions;

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
            .RequireRateLimiting("tenant-api");

        if (requireAuth)
        {
            chatRoutes.RequireAuthorization();
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
                RequestedProviderId = request.ProviderId,
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
                    RequestedProviderId = request.ProviderId,
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
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers.Connection = "keep-alive";

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
                    RequestedProviderId = request.ProviderId,
                    RequestedModelId = request.ModelId,
                    IsPrivate = request.IsPrivate
                },
                Options = new ChatRequestOptions
                {
                    Temperature = request.Temperature,
                    MaxTokens = request.MaxTokens
                }
            };

            await foreach (var chunk in engine.StreamAsync(orchestrationRequest, cancellationToken))
            {
                var data = System.Text.Json.JsonSerializer.Serialize(new
                {
                    contentDelta = chunk.Chunk.ContentDelta,
                    isComplete = chunk.Chunk.IsComplete,
                    finishReason = chunk.Chunk.FinishReason?.ToString().ToLowerInvariant(),
                    providerId = chunk.ProviderId,
                    routing = chunk.Routing is not null ? new
                    {
                        category = chunk.Routing.Classification.Category.ToString().ToLowerInvariant(),
                        confidence = chunk.Routing.Classification.Confidence
                    } : null
                });

                await response.WriteAsync($"data: {data}\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }

            await response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        });
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
}

// Request DTOs

internal sealed record ClassifyRequest(string Message);

internal sealed record RouteRequest(
    string Message,
    string? ProviderId = null,
    string? ModelId = null,
    bool IsPrivate = false,
    bool ForceMultiModel = false);

internal sealed record ChatCompletionRequest(
    IReadOnlyList<ChatMessageDto> Messages,
    string? SystemMessage = null,
    string? ProviderId = null,
    string? ModelId = null,
    double? Temperature = null,
    int? MaxTokens = null,
    bool IsPrivate = false);

internal sealed record ChatMessageDto(string Role, string Content);
