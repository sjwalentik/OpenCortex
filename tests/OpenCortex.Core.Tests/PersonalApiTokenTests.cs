using OpenCortex.Core.Security;

namespace OpenCortex.Core.Tests;

public sealed class PersonalApiTokenTests
{
    [Fact]
    public void Generate_ReturnsExpectedShape()
    {
        var token = PersonalApiToken.Generate();

        Assert.StartsWith("oct_", token.RawToken, StringComparison.Ordinal);
        Assert.Equal(8, token.TokenPrefix.Length);
        Assert.Equal(token.RawToken[..8], token.TokenPrefix);
        Assert.Equal(64, token.TokenHash.Length);
        Assert.Equal(PersonalApiToken.ComputeHash(token.RawToken), token.TokenHash);
    }

    [Fact]
    public void IsValidFormat_ReturnsTrueForGeneratedToken()
    {
        var token = PersonalApiToken.Generate();

        Assert.True(PersonalApiToken.IsValidFormat(token.RawToken));
        Assert.False(PersonalApiToken.IsValidFormat("invalid"));
    }

    [Fact]
    public void Generate_RequiresAtLeastThirtyTwoRandomBytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PersonalApiToken.Generate(16));
    }
}
