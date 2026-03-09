using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace OpenCortex.Core.Security;

public static class PersonalApiToken
{
    private const string Prefix = "oct_";
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static GeneratedPersonalApiToken Generate(int randomByteCount = 32)
    {
        if (randomByteCount < 32)
        {
            throw new ArgumentOutOfRangeException(nameof(randomByteCount), "API tokens require at least 32 random bytes.");
        }

        var randomBytes = RandomNumberGenerator.GetBytes(randomByteCount);
        var encoded = EncodeBase62(randomBytes);
        var rawToken = $"{Prefix}{encoded}";

        return new GeneratedPersonalApiToken(
            rawToken,
            ComputeHash(rawToken),
            rawToken[..Math.Min(8, rawToken.Length)]);
    }

    public static bool IsValidFormat(string? rawToken) =>
        !string.IsNullOrWhiteSpace(rawToken)
        && rawToken.StartsWith(Prefix, StringComparison.Ordinal)
        && rawToken.Length > Prefix.Length;

    public static string ComputeHash(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken.Trim()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string EncodeBase62(byte[] bytes)
    {
        var value = new BigInteger([.. bytes, 0]);
        if (value.IsZero)
        {
            return "0";
        }

        var chars = new List<char>();
        while (value > BigInteger.Zero)
        {
            value = BigInteger.DivRem(value, 62, out var remainder);
            chars.Add(Alphabet[(int)remainder]);
        }

        chars.Reverse();
        return new string(chars.ToArray());
    }
}

public sealed record GeneratedPersonalApiToken(
    string RawToken,
    string TokenHash,
    string TokenPrefix);
