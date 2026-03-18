using System.Text.RegularExpressions;

namespace OpenCortex.Api;

internal static partial class ErrorMessages
{
    private static readonly Regex UrlUserInfoRegex = UrlUserInfoPattern();

    public static string ForExternalFailure(string fallback, string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return fallback;
        }

        return UrlUserInfoRegex.Replace(detail, "${scheme}***@");
    }

    [GeneratedRegex(@"(?<scheme>https?://)(?:[^/\s:@]+(?::[^/\s@]*)?@)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlUserInfoPattern();
}
