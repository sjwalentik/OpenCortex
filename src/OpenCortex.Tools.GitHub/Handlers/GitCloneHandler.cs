using System.Text.Json;
using OpenCortex.Tools;

namespace OpenCortex.Tools.GitHub.Handlers;

/// <summary>
/// Handler for the git_clone tool - clones a repository to the user's workspace.
/// </summary>
public sealed class GitCloneHandler : IToolHandler
{
    private readonly IWorkspaceManager _workspace;

    public GitCloneHandler(IWorkspaceManager workspace)
    {
        _workspace = workspace;
    }

    public string ToolName => "git_clone";
    public string Category => "github";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var token = context.GetCredential("github");

        var repoUrl = arguments.GetProperty("repo_url").GetString()
            ?? throw new ArgumentException("repo_url is required");

        // Optional: specify a directory name
        var directory = arguments.TryGetProperty("directory", out var dirElement)
            ? dirElement.GetString()
            : null;

        // Optional: specify a branch to checkout after clone
        var branch = arguments.TryGetProperty("branch", out var branchElement)
            ? branchElement.GetString()
            : null;

        var workspacePath = await _workspace.GetWorkspacePathAsync(context.UserId, cancellationToken);

        // Determine target directory
        var targetDir = directory;
        if (string.IsNullOrEmpty(targetDir))
        {
            // Extract repo name from URL
            var parts = repoUrl.TrimEnd('/').Split('/');
            targetDir = parts[^1].Replace(".git", "");
        }

        var fullTargetPath = $"{workspacePath}/{targetDir}";

        // Check if directory already exists
        var checkResult = await _workspace.ExecuteCommandAsync(
            context.UserId,
            $"test -d {fullTargetPath} && echo exists || echo notexists",
            null, null, cancellationToken);

        if (checkResult.StandardOutput.Trim() == "exists")
        {
            // Check if it's a git repo
            var gitCheckResult = await _workspace.ExecuteCommandAsync(
                context.UserId,
                $"test -d {fullTargetPath}/.git && echo git || echo notgit",
                null, null, cancellationToken);

            if (gitCheckResult.StandardOutput.Trim() == "git")
            {
                if (!string.IsNullOrEmpty(branch))
                {
                    await _workspace.ExecuteCommandAsync(
                        context.UserId,
                        $"cd {fullTargetPath} && git fetch origin {branch} && git checkout {branch}",
                        null, null, cancellationToken);

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        action = "checkout",
                        directory = fullTargetPath,
                        branch,
                        message = $"Repository already exists. Checked out branch '{branch}'."
                    });
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    action = "exists",
                    directory = fullTargetPath,
                    message = "Repository already cloned."
                });
            }

            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Directory '{targetDir}' already exists and is not a git repository"
            });
        }

        // Build authenticated clone URL if token provided
        var cloneUrl = repoUrl;
        if (!string.IsNullOrEmpty(token) && repoUrl.StartsWith("https://github.com/"))
        {
            cloneUrl = repoUrl.Replace("https://github.com/", $"https://{token}@github.com/");
        }

        // Build clone command
        var cloneCmd = $"git clone {cloneUrl}";
        if (!string.IsNullOrEmpty(branch))
        {
            cloneCmd += $" --branch {branch}";
        }
        cloneCmd += $" {fullTargetPath}";

        var result = await _workspace.ExecuteCommandAsync(
            context.UserId, cloneCmd, null, null, cancellationToken);

        if (result.ExitCode != 0)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Git clone failed: {result.StandardError}"
            });
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            action = "cloned",
            directory = fullTargetPath,
            branch = branch ?? "default",
            message = $"Repository cloned to '{targetDir}'" +
                      (branch != null ? $" on branch '{branch}'" : "")
        });
    }
}
