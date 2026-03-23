using System.Text.Json;

namespace OpenCortex.Tools.GitHub.Handlers;

/// <summary>
/// Handler for the github_get_repository tool.
/// </summary>
public sealed class GetRepositoryHandler : IToolHandler
{
    private readonly IGitHubClient _client;

    public GetRepositoryHandler(IGitHubClient client)
    {
        _client = client;
    }

    public string ToolName => "github_get_repository";
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

        var repository = await _client.GetRepositoryAsync(token, owner, repo, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            repository = new
            {
                repository.Name,
                repository.FullName,
                repository.Description,
                repository.DefaultBranch,
                repository.HtmlUrl,
                repository.Private
            }
        });
    }
}
