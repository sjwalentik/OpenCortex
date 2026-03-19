using System.Diagnostics;
using System.Text;
using OpenCortex.Tools;

namespace OpenCortex.Tools.GitHub.Handlers;

internal static class GitHubGitAuth
{
    private const string GitHubHost = "github.com";

    public static void Apply(ProcessStartInfo startInfo, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token}"));
        startInfo.Environment["GIT_CONFIG_COUNT"] = "1";
        startInfo.Environment["GIT_CONFIG_KEY_0"] = $"http.https://{GitHubHost}/.extraheader";
        startInfo.Environment["GIT_CONFIG_VALUE_0"] = $"AUTHORIZATION: basic {basicAuth}";
    }

    public static string SanitizeRemoteUrl(string remoteUrl)
    {
        if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, GitHubHost, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(uri.UserInfo))
        {
            return remoteUrl;
        }

        return new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty
        }.Uri.ToString();
    }

    public static string BuildShellCommand(string? token, params string[] gitArgs)
    {
        var commandParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(token))
        {
            var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token}"));
            commandParts.Add("GIT_CONFIG_COUNT=1");
            commandParts.Add($"GIT_CONFIG_KEY_0={ShellEscaping.SingleQuote($"http.https://{GitHubHost}/.extraheader")}");
            commandParts.Add($"GIT_CONFIG_VALUE_0={ShellEscaping.SingleQuote($"AUTHORIZATION: basic {basicAuth}")}");
        }

        commandParts.Add("git");
        commandParts.AddRange(gitArgs.Select(ShellEscaping.SingleQuote));

        return string.Join(" ", commandParts);
    }
}
