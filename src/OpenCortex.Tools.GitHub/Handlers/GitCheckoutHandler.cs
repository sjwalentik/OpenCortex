using System.Text.Json;
using OpenCortex.Tools;

namespace OpenCortex.Tools.GitHub.Handlers;

/// <summary>
/// Handler for the git_checkout tool - checkout a branch in a cloned repository.
/// </summary>
public sealed class GitCheckoutHandler : IToolHandler
{
    private readonly IWorkspaceManager _workspace;

    public GitCheckoutHandler(IWorkspaceManager workspace)
    {
        _workspace = workspace;
    }

    public string ToolName => "git_checkout";
    public string Category => "github";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var branch = arguments.GetProperty("branch").GetString()
            ?? throw new ArgumentException("branch is required");

        var directory = arguments.TryGetProperty("directory", out var dirElement)
            ? dirElement.GetString()
            : null;

        var createIfNotExists = arguments.TryGetProperty("create", out var createElement)
            && createElement.GetBoolean();

        var fromBranch = arguments.TryGetProperty("from_branch", out var fromElement)
            ? fromElement.GetString()
            : null;

        var workspacePath = await _workspace.GetWorkspacePathAsync(context.UserId, cancellationToken);

        // Determine the repo directory
        string repoPath;
        if (!string.IsNullOrEmpty(directory))
        {
            repoPath = directory.StartsWith('/') ? directory : $"{workspacePath}/{directory}";
        }
        else
        {
            // Find git repos in workspace
            var findResult = await _workspace.ExecuteCommandAsync(
                context.UserId,
                $"find {workspacePath} -maxdepth 2 -type d -name .git 2>/dev/null | head -5",
                null, null, cancellationToken);

            var gitDirs = findResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => Path.GetDirectoryName(p)!)
                .ToList();

            if (gitDirs.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "No git repository found in workspace. Clone a repository first."
                });
            }

            if (gitDirs.Count > 1)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Multiple repositories found. Specify 'directory': {string.Join(", ", gitDirs.Select(Path.GetFileName))}"
                });
            }

            repoPath = gitDirs[0];
        }

        // Fetch latest
        await _workspace.ExecuteCommandAsync(
            context.UserId, $"cd {repoPath} && git fetch --all", null, null, cancellationToken);

        // Check if branch exists
        var branchCheckResult = await _workspace.ExecuteCommandAsync(
            context.UserId,
            $"cd {repoPath} && git rev-parse --verify {branch} 2>/dev/null && echo local || " +
            $"git rev-parse --verify origin/{branch} 2>/dev/null && echo remote || echo none",
            null, null, cancellationToken);

        var branchStatus = branchCheckResult.StandardOutput.Trim().Split('\n').Last();
        var branchExists = branchStatus != "none";

        if (!branchExists && !createIfNotExists)
        {
            var listResult = await _workspace.ExecuteCommandAsync(
                context.UserId, $"cd {repoPath} && git branch -a", null, null, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Branch '{branch}' not found. Use 'create: true' to create it. Available branches:\n{listResult.StandardOutput}"
            });
        }

        string checkoutCommand;
        string action;

        if (!branchExists && createIfNotExists)
        {
            if (!string.IsNullOrEmpty(fromBranch))
            {
                await _workspace.ExecuteCommandAsync(
                    context.UserId, $"cd {repoPath} && git checkout {fromBranch} && git pull",
                    null, null, cancellationToken);
            }
            checkoutCommand = $"checkout -b {branch}";
            action = "created";
        }
        else if (branchStatus == "remote")
        {
            checkoutCommand = $"checkout -t origin/{branch}";
            action = "tracked";
        }
        else
        {
            checkoutCommand = $"checkout {branch}";
            action = "switched";
        }

        var result = await _workspace.ExecuteCommandAsync(
            context.UserId, $"cd {repoPath} && git {checkoutCommand}", null, null, cancellationToken);

        if (result.ExitCode != 0)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Git checkout failed: {result.StandardError}"
            });
        }

        var currentBranchResult = await _workspace.ExecuteCommandAsync(
            context.UserId, $"cd {repoPath} && git branch --show-current", null, null, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            action,
            branch,
            directory = repoPath,
            currentBranch = currentBranchResult.StandardOutput.Trim(),
            message = action switch
            {
                "created" => $"Created and checked out new branch '{branch}'" +
                            (fromBranch != null ? $" from '{fromBranch}'" : ""),
                "tracked" => $"Checked out remote branch '{branch}'",
                _ => $"Switched to branch '{branch}'"
            }
        });
    }
}
