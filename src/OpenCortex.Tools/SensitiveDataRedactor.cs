using System.Text.RegularExpressions;

namespace OpenCortex.Tools;

internal static partial class SensitiveDataRedactor
{
    private static readonly Regex UrlUserInfoRegex = UrlUserInfoPattern();

    public static string? Redact(string? value, IReadOnlyDictionary<string, string>? credentials)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = value;

        if (credentials is not null)
        {
            foreach (var secret in credentials.Values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .OrderByDescending(v => v.Length))
            {
                redacted = redacted.Replace(secret, "***", StringComparison.Ordinal);
            }
        }

        return UrlUserInfoRegex.Replace(redacted, "${scheme}***@");
    }

    [GeneratedRegex(@"(?<scheme>https?://)(?:[^/\s:@]+(?::[^/\s@]*)?@)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlUserInfoPattern();
}
