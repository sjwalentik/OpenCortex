using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Providers.Anthropic;

/// <summary>
/// Configuration options for the Anthropic provider.
/// </summary>
public sealed record AnthropicOptions : ProviderOptions
{
    /// <summary>
    /// API version header value (e.g., "2023-06-01").
    /// </summary>
    public string ApiVersion { get; init; } = "2023-06-01";

    /// <summary>
    /// Maximum tokens to generate by default.
    /// </summary>
    public int DefaultMaxTokens { get; init; } = 4096;

    /// <summary>
    /// Create default options for Anthropic API.
    /// </summary>
    public static AnthropicOptions CreateDefault(string apiKey) => new()
    {
        ProviderId = "anthropic",
        Name = "Anthropic Claude",
        Endpoint = "https://api.anthropic.com",
        ApiKey = apiKey,
        DefaultModel = "claude-sonnet-4-20250514",
        CostProfile = CostProfile.High
    };
}
