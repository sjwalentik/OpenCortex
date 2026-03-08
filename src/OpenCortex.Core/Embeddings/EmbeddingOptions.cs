namespace OpenCortex.Core.Embeddings;

public sealed class EmbeddingOptions
{
    public string Provider { get; init; } = "deterministic";

    public string Model { get; init; } = "opencortex-deterministic-v1";

    public int Dimensions { get; init; } = 1536;

    public string Endpoint { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;
}
