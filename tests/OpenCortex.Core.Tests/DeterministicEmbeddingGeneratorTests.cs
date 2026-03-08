using OpenCortex.Core.Embeddings;

namespace OpenCortex.Core.Tests;

public sealed class DeterministicEmbeddingProviderTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsStableNormalizedVector()
    {
        var provider = new DeterministicEmbeddingProvider(new EmbeddingOptions
        {
            Provider = "deterministic",
            Model = "deterministic-test",
            Dimensions = 16,
        });

        var first = await provider.GenerateAsync("hello roadmap memory runtime");
        var second = await provider.GenerateAsync("hello roadmap memory runtime");

        Assert.Equal(16, first.Vector.Count);
        Assert.Equal(first.Vector, second.Vector);
        Assert.Contains(first.Vector, value => Math.Abs(value) > 0);
    }
}
