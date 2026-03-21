using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Orchestration.Routing;

/// <summary>
/// Routes tasks to appropriate model providers.
/// </summary>
public interface IModelRouter
{
    /// <summary>
    /// Determine which provider should handle this message.
    /// </summary>
    Task<RoutingDecision> RouteAsync(
        string message,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the provider for a routing decision.
    /// </summary>
    IModelProvider? GetProvider(string providerId);

    /// <summary>
    /// Get all available providers.
    /// </summary>
    IReadOnlyList<IModelProvider> GetProviders();
}

/// <summary>
/// Additional context for routing decisions.
/// </summary>
public sealed record RoutingContext
{
    /// <summary>
    /// Customer/tenant ID for tenant-scoped tools and routing.
    /// </summary>
    public string? CustomerId { get; init; }

    /// <summary>
    /// User ID for personalized routing.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Brain ID for memory context.
    /// </summary>
    public string? BrainId { get; init; }

    /// <summary>
    /// Conversation ID for continuity.
    /// </summary>
    public string? ConversationId { get; init; }

    /// <summary>
    /// Explicitly requested provider (overrides routing).
    /// </summary>
    public string? RequestedProviderId { get; init; }

    /// <summary>
    /// Explicitly requested model (overrides routing).
    /// </summary>
    public string? RequestedModelId { get; init; }

    /// <summary>
    /// Force multi-model execution.
    /// </summary>
    public bool ForceMultiModel { get; init; }

    /// <summary>
    /// Mark content as private (route to local model).
    /// </summary>
    public bool IsPrivate { get; init; }

    /// <summary>
    /// Previous provider used in this conversation (for continuity).
    /// </summary>
    public string? PreviousProviderId { get; init; }
}
