namespace OpenCortex.Providers.Abstractions;

/// <summary>
/// Unified interface for LLM providers (OpenAI, Anthropic, Ollama, etc.)
/// </summary>
public interface IModelProvider
{
    /// <summary>
    /// Unique identifier for this provider instance.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Human-readable name for this provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Provider type (e.g., "openai", "anthropic", "ollama").
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// Capabilities supported by this provider.
    /// </summary>
    ProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Execute a chat completion request and return the full response.
    /// </summary>
    Task<ChatCompletion> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a chat completion request and stream the response.
    /// </summary>
    IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the provider is healthy and reachable.
    /// </summary>
    Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// List available models from this provider.
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);
}
