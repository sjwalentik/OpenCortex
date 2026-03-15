using System.Text.Json;

namespace OpenCortex.Tools.GitHub.Handlers;

/// <summary>
/// Handler for the github_list_branches tool.
/// </summary>
public sealed class ListBranchesHandler : IToolHandler
{
    private readonly IGitHubClient _client;

    public ListBranchesHandler(IGitHubClient client)
    {
        _client = client;
    }

    public string ToolName => "github_list_branches";
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

        var branches = await _client.ListBranchesAsync(token, owner, repo, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            branches = branches.Select(b => new
            {
                b.Name,
                b.Sha
            })
        });
    }
}
