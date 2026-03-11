using OpenCortex.Persistence.Postgres;

namespace OpenCortex.Api.Tests;

public sealed class PostgresEmbeddingSchemaValidatorTests
{
    [Theory]
    [InlineData("vector(1536)", 1536)]
    [InlineData("VECTOR(768)", 768)]
    public void TryParseVectorDimensions_ParsesExpectedPgvectorDefinition(string formattedType, int expectedDimensions)
    {
        var parsed = PostgresEmbeddingSchemaValidator.TryParseVectorDimensions(formattedType, out var dimensions);

        Assert.True(parsed);
        Assert.Equal(expectedDimensions, dimensions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("vector")]
    [InlineData("jsonb")]
    [InlineData("vector(foo)")]
    public void TryParseVectorDimensions_RejectsUnsupportedDefinitions(string formattedType)
    {
        var parsed = PostgresEmbeddingSchemaValidator.TryParseVectorDimensions(formattedType, out var dimensions);

        Assert.False(parsed);
        Assert.Equal(0, dimensions);
    }
}
