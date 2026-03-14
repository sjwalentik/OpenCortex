namespace OpenCortex.Orchestration.Routing;

/// <summary>
/// A rule that determines how to route a task to a provider.
/// </summary>
public sealed record RoutingRule
{
    /// <summary>
    /// Unique identifier for this rule.
    /// </summary>
    public required string RuleId { get; init; }

    /// <summary>
    /// Human-readable name for this rule.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Priority of this rule (lower = higher priority).
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Task category this rule applies to.
    /// </summary>
    public TaskCategory? Category { get; init; }

    /// <summary>
    /// Custom condition expression (for future use).
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>
    /// Provider ID to route to.
    /// </summary>
    public required string TargetProviderId { get; init; }

    /// <summary>
    /// Specific model to use (overrides provider default).
    /// </summary>
    public string? TargetModelId { get; init; }

    /// <summary>
    /// Fallback provider if primary fails.
    /// </summary>
    public string? FallbackProviderId { get; init; }

    /// <summary>
    /// Whether this rule is enabled.
    /// </summary>
    public bool IsEnabled { get; init; } = true;
}

/// <summary>
/// Result of routing decision.
/// </summary>
public sealed record RoutingDecision
{
    /// <summary>
    /// Selected provider ID.
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// Model to use (may be null to use provider default).
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Fallback provider if primary fails.
    /// </summary>
    public string? FallbackProviderId { get; init; }

    /// <summary>
    /// The rule that matched (null if using default).
    /// </summary>
    public RoutingRule? MatchedRule { get; init; }

    /// <summary>
    /// Task classification that informed this decision.
    /// </summary>
    public required TaskClassification Classification { get; init; }

    /// <summary>
    /// Whether to use multi-model execution.
    /// </summary>
    public bool UseMultiModel { get; init; }

    /// <summary>
    /// Additional providers for multi-model execution.
    /// </summary>
    public IReadOnlyList<string>? AdditionalProviderIds { get; init; }
}
