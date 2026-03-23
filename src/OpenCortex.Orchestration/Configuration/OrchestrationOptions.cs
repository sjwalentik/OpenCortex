using OpenCortex.Orchestration.Routing;

namespace OpenCortex.Orchestration.Configuration;

/// <summary>
/// Configuration options for the orchestration engine.
/// </summary>
public sealed record OrchestrationOptions
{
    /// <summary>
    /// Default provider to use when no rule matches.
    /// </summary>
    public string? DefaultProvider { get; init; } = "anthropic";

    /// <summary>
    /// Enable multi-model execution for high-stakes tasks.
    /// </summary>
    public bool EnableMultiModel { get; init; }

    /// <summary>
    /// Model to use for task classification (if using ML classifier).
    /// </summary>
    public string? TaskClassifierModel { get; init; }

    /// <summary>
    /// Maximum number of retry attempts for failed requests.
    /// </summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>
    /// Timeout for individual provider requests in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; init; } = 120;

    /// <summary>
    /// Whether to inject memory context automatically.
    /// </summary>
    public bool AutoInjectMemory { get; init; } = true;

    /// <summary>
    /// Maximum number of memory items to inject.
    /// </summary>
    public int MaxMemoryItems { get; init; } = 5;

    /// <summary>
    /// Routing rules for task-to-provider mapping.
    /// </summary>
    public List<RoutingRule>? RoutingRules { get; init; }
}
