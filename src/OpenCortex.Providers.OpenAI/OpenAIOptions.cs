using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Providers.OpenAI;

/// <summary>
/// Configuration options for the OpenAI provider.
/// </summary>
public sealed record OpenAIOptions : ProviderOptions
{
    /// <summary>
    /// Organization ID for API requests.
    /// </summary>
    public string? OrganizationId { get; init; }

    /// <summary>
    /// Create default options for OpenAI API.
    /// </summary>
    public static OpenAIOptions CreateDefault(string apiKey) => new()
    {
        ProviderId = "openai",
        Name = "OpenAI",
        Endpoint = "https://api.openai.com/v1",
        ApiKey = apiKey,
        DefaultModel = "gpt-4o",
        CostProfile = CostProfile.High
    };

    /// <summary>
    /// Create options for Azure OpenAI.
    /// </summary>
    public static OpenAIOptions CreateAzure(string endpoint, string apiKey, string deploymentName) => new()
    {
        ProviderId = "azure-openai",
        Name = "Azure OpenAI",
        Endpoint = endpoint,
        ApiKey = apiKey,
        DefaultModel = deploymentName,
        CostProfile = CostProfile.High
    };
}
