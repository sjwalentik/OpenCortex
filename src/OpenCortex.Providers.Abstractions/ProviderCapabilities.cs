namespace OpenCortex.Providers.Abstractions;

/// <summary>
/// Capabilities supported by a model provider.
/// </summary>
public sealed record ProviderCapabilities
{
    /// <summary>
    /// Provider supports standard chat completions.
    /// </summary>
    public bool SupportsChat { get; init; } = true;

    /// <summary>
    /// Provider is optimized for code generation and editing.
    /// </summary>
    public bool SupportsCode { get; init; }

    /// <summary>
    /// Provider can process images in messages.
    /// </summary>
    public bool SupportsVision { get; init; }

    /// <summary>
    /// Provider supports tool/function calling.
    /// </summary>
    public bool SupportsTools { get; init; }

    /// <summary>
    /// Provider supports streaming responses.
    /// </summary>
    public bool SupportsStreaming { get; init; } = true;

    /// <summary>
    /// Maximum context window size in tokens.
    /// </summary>
    public int MaxContextTokens { get; init; }

    /// <summary>
    /// Maximum output tokens for a single completion.
    /// </summary>
    public int MaxOutputTokens { get; init; }

    /// <summary>
    /// Default capabilities for unknown providers.
    /// </summary>
    public static ProviderCapabilities Default => new()
    {
        SupportsChat = true,
        SupportsStreaming = true,
        MaxContextTokens = 4096,
        MaxOutputTokens = 4096
    };
}

/// <summary>
/// Information about an available model.
/// </summary>
public sealed record ModelInfo
{
    /// <summary>
    /// Model identifier used in API requests.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable name for the model.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Owner or organization that created the model.
    /// </summary>
    public string? OwnedBy { get; init; }

    /// <summary>
    /// Maximum context window in tokens.
    /// </summary>
    public int? ContextWindow { get; init; }

    /// <summary>
    /// Capabilities specific to this model.
    /// </summary>
    public ProviderCapabilities? Capabilities { get; init; }
}

/// <summary>
/// Result of a provider health check.
/// </summary>
public sealed record ProviderHealthResult
{
    /// <summary>
    /// Whether the provider is healthy and operational.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Latency of the health check in milliseconds.
    /// </summary>
    public int? LatencyMs { get; init; }

    /// <summary>
    /// Error message if unhealthy.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// When this health check was performed.
    /// </summary>
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Create a healthy result.
    /// </summary>
    public static ProviderHealthResult Healthy(int? latencyMs = null) =>
        new() { IsHealthy = true, LatencyMs = latencyMs };

    /// <summary>
    /// Create an unhealthy result.
    /// </summary>
    public static ProviderHealthResult Unhealthy(string error) =>
        new() { IsHealthy = false, Error = error };
}

/// <summary>
/// Cost profile for a provider, used in routing decisions.
/// </summary>
public enum CostProfile
{
    /// <summary>
    /// No cost (local models).
    /// </summary>
    Free,

    /// <summary>
    /// Low cost per request.
    /// </summary>
    Low,

    /// <summary>
    /// Medium cost per request.
    /// </summary>
    Medium,

    /// <summary>
    /// High cost per request.
    /// </summary>
    High
}
