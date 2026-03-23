using System.Text.Json;

namespace OpenCortex.Tools.GitHub.Handlers;

/// <summary>
/// Handler for the github_get_file_content tool.
/// </summary>
public sealed class GetFileContentHandler : IToolHandler
{
    private readonly IGitHubClient _client;

    public GetFileContentHandler(IGitHubClient client)
    {
        _client = client;
    }

    public string ToolName => "github_get_file_content";
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

        var @ref = arguments.TryGetProperty("ref", out var refElement)
            ? refElement.GetString()
            : null;

        var content = await _client.GetFileContentAsync(
            token, owner, repo, path, @ref, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            file = new
            {
                content.Name,
                content.Path,
                content.Sha,
                content.Content
            }
        });
    }
}
