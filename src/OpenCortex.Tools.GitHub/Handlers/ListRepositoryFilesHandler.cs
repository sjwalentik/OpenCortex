using System.Text.Json;

namespace OpenCortex.Tools.GitHub.Handlers;

/// <summary>
/// Handler for the github_list_repository_files tool.
/// </summary>
public sealed class ListRepositoryFilesHandler : IToolHandler
{
    private readonly IGitHubClient _client;

    public ListRepositoryFilesHandler(IGitHubClient client)
    {
        _client = client;
    }

    public string ToolName => "github_list_repository_files";
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

        var path = arguments.TryGetProperty("path", out var pathElement)
            ? pathElement.GetString()
            : null;

        var @ref = arguments.TryGetProperty("ref", out var refElement)
            ? refElement.GetString()
            : null;

        var files = await _client.ListRepositoryFilesAsync(
            token, owner, repo, path, @ref, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            path = path ?? "/",
            files = files.Select(f => new
            {
                f.Name,
                f.Path,
                f.Type,
                f.Size
            })
        });
    }
}
