using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCortex.Orchestration.Configuration;
using OpenCortex.Orchestration.Routing;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Orchestration.Execution;

/// <summary>
/// Coordinates chat requests across model providers.
/// </summary>
public interface IOrchestrationEngine
{
    /// <summary>
    /// Execute a chat request and return the completion.
    /// </summary>
    Task<OrchestrationResult> ExecuteAsync(
        OrchestrationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a chat request and stream the response.
    /// </summary>
    IAsyncEnumerable<OrchestrationStreamChunk> StreamAsync(
        OrchestrationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to the orchestration engine.
/// </summary>
public sealed record OrchestrationRequest
{
    /// <summary>
    /// Messages in the conversation.
    /// </summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>
    /// Tools available for the model to call.
    /// </summary>
    public IReadOnlyList<ToolDefinition>? Tools { get; init; }

    /// <summary>
    /// Routing context for provider selection.
    /// </summary>
    public RoutingContext? RoutingContext { get; init; }

    /// <summary>
    /// Additional request options.
    /// </summary>
    public ChatRequestOptions? Options { get; init; }

    /// <summary>
    /// System message to prepend (if not already in messages).
    /// </summary>
    public string? SystemMessage { get; init; }

    /// <summary>
    /// Memory context to inject.
    /// </summary>
    public IReadOnlyList<string>? MemoryContext { get; init; }
}

/// <summary>
/// Result from the orchestration engine.
/// </summary>
public sealed record OrchestrationResult
{
    /// <summary>
    /// The chat completion from the selected provider.
    /// </summary>
    public required ChatCompletion Completion { get; init; }

    /// <summary>
    /// Routing decision that was made.
    /// </summary>
    public required RoutingDecision Routing { get; init; }

    /// <summary>
    /// Provider that handled the request.
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// Model that was used.
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// Execution time in milliseconds.
    /// </summary>
    public int LatencyMs { get; init; }

    /// <summary>
    /// Whether a fallback was used.
    /// </summary>
    public bool UsedFallback { get; init; }

    /// <summary>
    /// Error message if request failed.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Streaming chunk from the orchestration engine.
/// </summary>
public sealed record OrchestrationStreamChunk
{
    /// <summary>
    /// The underlying stream chunk.
    /// </summary>
    public required StreamChunk Chunk { get; init; }

    /// <summary>
    /// Provider that is streaming.
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// Routing decision (available in first chunk).
    /// </summary>
    public RoutingDecision? Routing { get; init; }
}

/// <summary>
/// Default orchestration engine implementation.
/// </summary>
public sealed class OrchestrationEngine : IOrchestrationEngine
{
    private readonly IModelRouter _router;
    private readonly OrchestrationOptions _options;
    private readonly ILogger<OrchestrationEngine> _logger;

    public OrchestrationEngine(
        IModelRouter router,
        IOptions<OrchestrationOptions> options,
        ILogger<OrchestrationEngine> logger)
    {
        _router = router;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OrchestrationResult> ExecuteAsync(
        OrchestrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Get user message for routing
        var userMessage = request.Messages
            .LastOrDefault(m => m.Role == ChatRole.User)?.Content ?? "";

        // Route to provider
        var routing = await _router.RouteAsync(userMessage, request.RoutingContext, cancellationToken);

        _logger.LogInformation(
            "Routing to provider {ProviderId} for category {Category}",
            routing.ProviderId,
            routing.Classification.Category);

        // Build the chat request
        var chatRequest = BuildChatRequest(request, routing);

        // Execute with retry and fallback
        var (completion, usedFallback, actualProviderId) = await ExecuteWithFallbackAsync(
            chatRequest, routing, cancellationToken);

        stopwatch.Stop();

        return new OrchestrationResult
        {
            Completion = completion,
            Routing = routing,
            ProviderId = actualProviderId,
            ModelId = completion.Model,
            LatencyMs = (int)stopwatch.ElapsedMilliseconds,
            UsedFallback = usedFallback
        };
    }

    public async IAsyncEnumerable<OrchestrationStreamChunk> StreamAsync(
        OrchestrationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Get user message for routing
        var userMessage = request.Messages
            .LastOrDefault(m => m.Role == ChatRole.User)?.Content ?? "";

        // Route to provider
        var routing = await _router.RouteAsync(userMessage, request.RoutingContext, cancellationToken);

        _logger.LogInformation(
            "Streaming from provider {ProviderId} for category {Category}",
            routing.ProviderId,
            routing.Classification.Category);

        var provider = _router.GetProvider(routing.ProviderId);
        if (provider is null)
        {
            _logger.LogError("Provider {ProviderId} not found", routing.ProviderId);
            yield break;
        }

        var chatRequest = BuildChatRequest(request, routing);
        var isFirst = true;

        yield return new OrchestrationStreamChunk
        {
            Chunk = new StreamChunk
            {
                IsComplete = false
            },
            ProviderId = routing.ProviderId,
            Routing = routing
        };
        isFirst = false;

        await foreach (var chunk in provider.StreamAsync(chatRequest, cancellationToken))
        {
            yield return new OrchestrationStreamChunk
            {
                Chunk = chunk,
                ProviderId = routing.ProviderId,
                Routing = isFirst ? routing : null
            };
        }
    }

    private ChatRequest BuildChatRequest(OrchestrationRequest request, RoutingDecision routing)
    {
        var messages = new List<ChatMessage>();

        // Add system message if provided and not already present
        if (!string.IsNullOrEmpty(request.SystemMessage) &&
            !request.Messages.Any(m => m.Role == ChatRole.System))
        {
            messages.Add(ChatMessage.System(request.SystemMessage));
        }

        // Inject memory context if available
        if (request.MemoryContext?.Count > 0)
        {
            var memoryContent = string.Join("\n\n", request.MemoryContext);
            messages.Add(ChatMessage.System($"Relevant context from memory:\n\n{memoryContent}"));
        }

        // Add original messages
        messages.AddRange(request.Messages);

        return new ChatRequest
        {
            Model = routing.ModelId ?? "", // Provider will use default if empty
            Messages = messages,
            Tools = request.Tools,
            Options = request.Options
        };
    }

    private async Task<(ChatCompletion, bool usedFallback, string providerId)> ExecuteWithFallbackAsync(
        ChatRequest request,
        RoutingDecision routing,
        CancellationToken cancellationToken)
    {
        var providerId = routing.ProviderId;
        var provider = _router.GetProvider(providerId);

        if (provider is null)
        {
            throw new InvalidOperationException($"Provider {providerId} not found");
        }

        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                var completion = await provider.CompleteAsync(request, cancellationToken);
                return (completion, attempt > 0 || providerId != routing.ProviderId, providerId);
            }
            catch (Exception ex) when (attempt < _options.MaxRetries)
            {
                _logger.LogWarning(ex,
                    "Request to {ProviderId} failed (attempt {Attempt}), retrying",
                    providerId, attempt + 1);

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
            catch (Exception ex) when (!string.IsNullOrEmpty(routing.FallbackProviderId))
            {
                _logger.LogWarning(ex,
                    "Request to {ProviderId} failed, trying fallback {FallbackProviderId}",
                    providerId, routing.FallbackProviderId);

                var fallbackProvider = _router.GetProvider(routing.FallbackProviderId);
                if (fallbackProvider is not null)
                {
                    provider = fallbackProvider;
                    providerId = routing.FallbackProviderId;
                    // Continue to retry with fallback
                }
                else
                {
                    throw;
                }
            }
        }

        throw new InvalidOperationException($"All retry attempts exhausted for provider {providerId}");
    }
}
