using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenCortex.Core.Embeddings;

public sealed partial class DeterministicEmbeddingProvider : IEmbeddingProvider
{
    private readonly EmbeddingOptions _options;

    public DeterministicEmbeddingProvider(EmbeddingOptions options)
    {
        _options = options;
    }

    public Task<EmbeddingResponse> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var vector = new float[_options.Dimensions];
        var tokens = TokenRegex().Matches(text.ToLowerInvariant()).Select(match => match.Value).ToArray();

        if (tokens.Length > 0)
        {
            foreach (var token in tokens)
            {
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
                var index = BitConverter.ToInt32(hash, 0) & int.MaxValue;
                var sign = (hash[4] & 1) == 0 ? 1f : -1f;
                var weight = 1f + (hash[5] / 255f);
                vector[index % _options.Dimensions] += sign * weight;
            }

            Normalize(vector);
        }

        return Task.FromResult(new EmbeddingResponse(_options.Model, _options.Dimensions, vector));
    }

    private static void Normalize(float[] vector)
    {
        double magnitude = 0;
        foreach (var value in vector)
        {
            magnitude += value * value;
        }

        if (magnitude <= 0)
        {
            return;
        }

        var scale = 1d / Math.Sqrt(magnitude);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] * scale);
        }
    }

    [GeneratedRegex("[a-z0-9_/-]+")]
    private static partial Regex TokenRegex();
}
