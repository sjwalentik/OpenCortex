using System.Text.Json;

namespace OpenCortex.Tools.GitHub.Handlers;

/// <summary>
/// Handler for the github_create_pull_request tool.
/// </summary>
public sealed class CreatePullRequestHandler : IToolHandler
{
    private readonly IGitHubClient _client;

    public CreatePullRequestHandler(IGitHubClient client)
    {
        _client = client;
    }

    public string ToolName => "github_create_pull_request";
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
        var title = arguments.GetProperty("title").GetString()
            ?? throw new ArgumentException("title is required");
        var body = arguments.GetProperty("body").GetString()
            ?? throw new ArgumentException("body is required");
        var head = arguments.GetProperty("head").GetString()
            ?? throw new ArgumentException("head is required");
        var @base = arguments.GetProperty("base").GetString()
            ?? throw new ArgumentException("base is required");

        var pr = await _client.CreatePullRequestAsync(
            token, owner, repo, title, body, head, @base, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            pull_request = new
            {
                pr.Number,
                pr.Title,
                pr.State,
                pr.HtmlUrl,
                pr.Head,
                pr.Base
            }
        });
    }
}
