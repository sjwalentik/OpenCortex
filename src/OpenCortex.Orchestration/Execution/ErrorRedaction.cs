using System.Text.RegularExpressions;

namespace OpenCortex.Orchestration.Execution;

internal static partial class ErrorRedaction
{
    private static readonly Regex UrlUserInfoRegex = UrlUserInfoPattern();

    public static string Sanitize(string fallback, string? detail, IReadOnlyDictionary<string, string>? credentials)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return fallback;
        }

        var redacted = detail;

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
