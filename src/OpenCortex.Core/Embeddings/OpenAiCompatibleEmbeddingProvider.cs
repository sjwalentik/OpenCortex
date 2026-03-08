using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace OpenCortex.Core.Embeddings;

public sealed class OpenAiCompatibleEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly EmbeddingOptions _options;

    public OpenAiCompatibleEmbeddingProvider(HttpClient httpClient, EmbeddingOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<EmbeddingResponse> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        using var request = new HttpRequestMessage(HttpMethod.Post, ResolveEndpoint())
        {
            Content = JsonContent.Create(new OpenAiEmbeddingRequest(_options.Model, text)),
        };

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Embedding provider returned an empty response.");

        var vector = payload.Data.FirstOrDefault()?.Embedding
            ?? throw new InvalidOperationException("Embedding provider did not return embedding data.");

        return new EmbeddingResponse(
            payload.Model ?? _options.Model,
            vector.Count,
            vector);
    }

    private string ResolveEndpoint()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            throw new InvalidOperationException("OpenAI-compatible embedding provider requires Embeddings:Endpoint.");
        }

        return _options.Endpoint;
    }

    private sealed record OpenAiEmbeddingRequest(string Model, string Input);

    private sealed record OpenAiEmbeddingResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("data")] IReadOnlyList<OpenAiEmbeddingItem> Data);

    private sealed record OpenAiEmbeddingItem(
        [property: JsonPropertyName("embedding")] IReadOnlyList<float> Embedding);
}
