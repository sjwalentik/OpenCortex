using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Providers.Ollama;

/// <summary>
/// Configuration options for the Ollama provider.
/// </summary>
public sealed record OllamaOptions : ProviderOptions
{
    /// <summary>
    /// Whether to keep the model loaded in memory between requests.
    /// </summary>
    public bool KeepAlive { get; init; } = true;

    /// <summary>
    /// Number of tokens to predict (-1 for unlimited).
    /// </summary>
    public int NumPredict { get; init; } = -1;

    /// <summary>
    /// Create default options for local Ollama instance.
    /// </summary>
    public static OllamaOptions CreateDefault() => new()
    {
        ProviderId = "ollama",
        Name = "Ollama (Local)",
        Endpoint = "http://localhost:11434",
        DefaultModel = "llama3.2",
        CostProfile = CostProfile.Free,
        TimeoutSeconds = 120 // Local models can be slow on first load
    };

    /// <summary>
    /// Create options for remote Ollama instance.
    /// </summary>
    public static OllamaOptions CreateRemote(string endpoint, string? apiKey = null) => new()
    {
        ProviderId = "ollama-remote",
        Name = "Ollama (Remote)",
        Endpoint = endpoint,
        ApiKey = apiKey,
        DefaultModel = "llama3.2",
        CostProfile = CostProfile.Low,
        TimeoutSeconds = 120
    };
}
