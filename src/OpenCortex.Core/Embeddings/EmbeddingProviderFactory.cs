namespace OpenCortex.Core.Embeddings;

public static class EmbeddingProviderFactory
{
    public static IEmbeddingProvider Create(EmbeddingOptions options, HttpClient? httpClient = null)
    {
        return options.Provider.ToLowerInvariant() switch
        {
            "openai-compatible" => new OpenAiCompatibleEmbeddingProvider(httpClient ?? new HttpClient(), options),
            _ => new DeterministicEmbeddingProvider(options),
        };
    }
}
