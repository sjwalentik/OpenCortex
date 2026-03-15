using System.Text.Json;

namespace OpenCortex.Tools.GitHub.Handlers;

/// <summary>
/// Handler for the github_get_pull_request tool.
/// </summary>
public sealed class GetPullRequestHandler : IToolHandler
{
    private readonly IGitHubClient _client;

    public GetPullRequestHandler(IGitHubClient client)
    {
        _client = client;
    }

    public string ToolName => "github_get_pull_request";
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
        var number = arguments.GetProperty("number").GetInt32();

        var pr = await _client.GetPullRequestAsync(
            token, owner, repo, number, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            pull_request = new
            {
                pr.Number,
                pr.Title,
                pr.Body,
                pr.State,
                pr.HtmlUrl,
                pr.Head,
                pr.Base,
                pr.User
            }
        });
    }
}
