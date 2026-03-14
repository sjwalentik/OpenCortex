namespace OpenCortex.Providers.Abstractions;

/// <summary>
/// Base configuration options for a model provider.
/// </summary>
public abstract record ProviderOptions
{
    /// <summary>
    /// Unique identifier for this provider instance.
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// Human-readable name for this provider.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// API endpoint URL.
    /// </summary>
    public required string Endpoint { get; init; }

    /// <summary>
    /// API key or reference to secret store.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Default model to use when not specified in requests.
    /// </summary>
    public required string DefaultModel { get; init; }

    /// <summary>
    /// Whether this provider is enabled.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Cost profile for routing decisions.
    /// </summary>
    public CostProfile CostProfile { get; init; } = CostProfile.Medium;

    /// <summary>
    /// Request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 60;
}
