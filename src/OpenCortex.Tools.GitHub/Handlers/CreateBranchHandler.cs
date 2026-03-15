using System.Text.Json;

namespace OpenCortex.Tools.GitHub.Handlers;

/// <summary>
/// Handler for the github_create_branch tool.
/// </summary>
public sealed class CreateBranchHandler : IToolHandler
{
    private readonly IGitHubClient _client;

    public CreateBranchHandler(IGitHubClient client)
    {
        _client = client;
    }

    public string ToolName => "github_create_branch";
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
        var branchName = arguments.GetProperty("branch_name").GetString()
            ?? throw new ArgumentException("branch_name is required");
        var fromBranch = arguments.GetProperty("from_branch").GetString()
            ?? throw new ArgumentException("from_branch is required");

        var branch = await _client.CreateBranchAsync(
            token, owner, repo, branchName, fromBranch, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            branch = new
            {
                branch.Name,
                branch.Sha,
                message = $"Branch '{branchName}' created from '{fromBranch}'"
            }
        });
    }
}
