using System.Text.Json;

namespace OpenCortex.Tools.GitHub.Handlers;

/// <summary>
/// Handler for the github_create_or_update_file tool.
/// </summary>
public sealed class CreateOrUpdateFileHandler : IToolHandler
{
    private readonly IGitHubClient _client;

    public CreateOrUpdateFileHandler(IGitHubClient client)
    {
        _client = client;
    }

    public string ToolName => "github_create_or_update_file";
    public string Category => "github";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var token = context.GetCredential("github")
            ?? throw new InvalidOperationException("GitHub token not configured");

        var owner = arguments.GetProperty("owner").GetString()
            ?? throw new ArgumentException("owner is required");
        var repo = arguments.GetProperty("repo").GetString()
            ?? throw new ArgumentException("repo is required");
        var path = arguments.GetProperty("path").GetString()
            ?? throw new ArgumentException("path is required");
        var content = arguments.GetProperty("content").GetString()
            ?? throw new ArgumentException("content is required");
        var message = arguments.GetProperty("message").GetString()
            ?? throw new ArgumentException("message is required");
        var branch = arguments.GetProperty("branch").GetString()
            ?? throw new ArgumentException("branch is required");

        var sha = arguments.TryGetProperty("sha", out var shaElement)
            ? shaElement.GetString()
            : null;

        var result = await _client.CreateOrUpdateFileAsync(
            token, owner, repo, path, content, message, branch, sha, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            commit = new
            {
                result.Sha,
                result.Message,
                result.HtmlUrl
            }
        });
    }
}
