using System.Diagnostics;
using System.Text.Json;

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
        var token = context.GetCredential("github");

        var branch = arguments.GetProperty("branch").GetString()
            ?? throw new ArgumentException("branch is required");

        var directory = arguments.TryGetProperty("directory", out var dirElement)
            ? dirElement.GetString()
            : null;

        // Option to create branch if it doesn't exist
        var createIfNotExists = arguments.TryGetProperty("create", out var createElement)
            && createElement.GetBoolean();

        // Option to specify source branch when creating
        var fromBranch = arguments.TryGetProperty("from_branch", out var fromElement)
            ? fromElement.GetString()
            : null;

        if (_workspace.SupportsContainerIsolation)
        {
            return await ExecuteRemoteAsync(
                context.UserId,
                branch,
                directory,
                createIfNotExists,
                fromBranch,
                token,
                cancellationToken);
        }

        if (string.IsNullOrEmpty(context.WorkspacePath))
        {
            throw new InvalidOperationException("Workspace path not configured");
        }

        // Determine the repo directory
        string repoPath;
        if (!string.IsNullOrEmpty(directory))
        {
            repoPath = Path.IsPathRooted(directory)
                ? directory
                : Path.Combine(context.WorkspacePath, directory);
        }
        else
        {
            // Try to find a git repo in the workspace
            var gitDirs = Directory.GetDirectories(context.WorkspacePath)
                .Where(d => Directory.Exists(Path.Combine(d, ".git")))
                .ToList();

            if (gitDirs.Count == 0)
            {
                throw new InvalidOperationException(
                    "No git repository found in workspace. Clone a repository first.");
            }

            if (gitDirs.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple repositories found. Specify 'directory': {string.Join(", ", gitDirs.Select(Path.GetFileName))}");
            }

            repoPath = gitDirs[0];
        }

        if (!Directory.Exists(Path.Combine(repoPath, ".git")))
        {
            throw new InvalidOperationException($"'{repoPath}' is not a git repository");
        }

        await SanitizeOriginUrlAsync(repoPath, token, cancellationToken);

        // Fetch latest from remote first
        await RunGitCommandAsync(repoPath, token, cancellationToken, "fetch", "--all");

        // Check if branch exists locally or remotely
        var branchExistsResult = await RunGitCommandAsync(
            repoPath, token, cancellationToken, "rev-parse", "--verify", branch);
        var remoteBranchExistsResult = await RunGitCommandAsync(
            repoPath, token, cancellationToken, "rev-parse", "--verify", $"origin/{branch}");

        var branchExists = branchExistsResult.Success || remoteBranchExistsResult.Success;

        if (!branchExists && !createIfNotExists)
        {
            // List available branches for helpful error
            var listResult = await RunGitCommandAsync(repoPath, token, cancellationToken, "branch", "-a");
            throw new InvalidOperationException(
                $"Branch '{branch}' not found. Use 'create: true' to create it. " +
                $"Available branches:\n{listResult.Output}");
        }

        string checkoutCommand;
        string action;

        if (!branchExists && createIfNotExists)
        {
            // Create and checkout new branch
            if (!string.IsNullOrEmpty(fromBranch))
            {
                // First checkout the source branch
                await RunGitCommandAsync(repoPath, token, cancellationToken, "checkout", fromBranch);
                await RunGitCommandAsync(repoPath, token, cancellationToken, "pull");
            }

            checkoutCommand = $"-b {branch}";
            action = "created";
        }
        else if (remoteBranchExistsResult.Success && !branchExistsResult.Success)
        {
            // Track remote branch
            checkoutCommand = $"-t origin/{branch}";
            action = "tracked";
        }
        else
        {
            // Checkout existing branch
            checkoutCommand = branch;
            action = "switched";
        }

        var result = !branchExists && createIfNotExists
            ? await RunGitCommandAsync(repoPath, token, cancellationToken, "checkout", "-b", branch)
            : remoteBranchExistsResult.Success && !branchExistsResult.Success
                ? await RunGitCommandAsync(repoPath, token, cancellationToken, "checkout", "-t", $"origin/{branch}")
                : await RunGitCommandAsync(repoPath, token, cancellationToken, "checkout", branch);

        if (!result.Success)
        {
            throw new InvalidOperationException($"Git checkout failed: {result.Error}");
        }

        // Get current branch to confirm
        var currentBranchResult = await RunGitCommandAsync(
            repoPath, token, cancellationToken, "branch", "--show-current");

        return JsonSerializer.Serialize(new
        {
            success = true,
            action,
            branch,
            directory = repoPath,
            currentBranch = currentBranchResult.Output?.Trim(),
            message = action switch
            {
                "created" => $"Created and checked out new branch '{branch}'" +
                            (fromBranch != null ? $" from '{fromBranch}'" : ""),
                "tracked" => $"Checked out remote branch '{branch}'",
                _ => $"Switched to branch '{branch}'"
            }
        });
    }

    private async Task<string> ExecuteRemoteAsync(
        Guid userId,
        string branch,
        string? directory,
        bool createIfNotExists,
        string? fromBranch,
        string? token,
        CancellationToken cancellationToken)
    {
        var workspacePath = await _workspace.GetWorkspacePathAsync(userId, cancellationToken);

        string repoPath;
        if (!string.IsNullOrEmpty(directory))
        {
            repoPath = _workspace.ResolvePath(userId, directory);
        }
        else
        {
            var gitDirsResult = await _workspace.ExecuteCommandAsync(
                userId,
                $"find {GitHubGitAuth.SingleQuote(workspacePath)} -mindepth 2 -maxdepth 2 -type d -name .git -exec dirname {{}} \\;",
                null,
                null,
                cancellationToken);

            var gitDirs = gitDirsResult.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (gitDirs.Count == 0)
            {
                throw new InvalidOperationException(
                    "No git repository found in workspace. Clone a repository first.");
            }

            if (gitDirs.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple repositories found. Specify 'directory': {string.Join(", ", gitDirs.Select(Path.GetFileName))}");
            }

            repoPath = gitDirs[0];
        }

        var repoCheckResult = await RunRemoteGitCommandAsync(
            userId,
            token,
            null,
            cancellationToken,
            "-C",
            repoPath,
            "rev-parse",
            "--is-inside-work-tree");

        if (!repoCheckResult.Success)
        {
            throw new InvalidOperationException($"'{repoPath}' is not a git repository");
        }

        await SanitizeOriginUrlRemoteAsync(userId, repoPath, token, cancellationToken);
        await RunRemoteGitCommandAsync(userId, token, null, cancellationToken, "-C", repoPath, "fetch", "--all");

        var branchExistsResult = await RunRemoteGitCommandAsync(
            userId, token, null, cancellationToken, "-C", repoPath, "rev-parse", "--verify", branch);
        var remoteBranchExistsResult = await RunRemoteGitCommandAsync(
            userId, token, null, cancellationToken, "-C", repoPath, "rev-parse", "--verify", $"origin/{branch}");

        var branchExists = branchExistsResult.Success || remoteBranchExistsResult.Success;

        if (!branchExists && !createIfNotExists)
        {
            var listResult = await RunRemoteGitCommandAsync(
                userId, token, null, cancellationToken, "-C", repoPath, "branch", "-a");
            throw new InvalidOperationException(
                $"Branch '{branch}' not found. Use 'create: true' to create it. " +
                $"Available branches:\n{listResult.Output}");
        }

        string action;
        (bool Success, string? Output, string? Error) result;

        if (!branchExists && createIfNotExists)
        {
            if (!string.IsNullOrEmpty(fromBranch))
            {
                await RunRemoteGitCommandAsync(
                    userId, token, null, cancellationToken, "-C", repoPath, "checkout", fromBranch);
                await RunRemoteGitCommandAsync(
                    userId, token, null, cancellationToken, "-C", repoPath, "pull");
            }

            result = await RunRemoteGitCommandAsync(
                userId, token, null, cancellationToken, "-C", repoPath, "checkout", "-b", branch);
            action = "created";
        }
        else if (remoteBranchExistsResult.Success && !branchExistsResult.Success)
        {
            result = await RunRemoteGitCommandAsync(
                userId, token, null, cancellationToken, "-C", repoPath, "checkout", "-t", $"origin/{branch}");
            action = "tracked";
        }
        else
        {
            result = await RunRemoteGitCommandAsync(
                userId, token, null, cancellationToken, "-C", repoPath, "checkout", branch);
            action = "switched";
        }

        if (!result.Success)
        {
            throw new InvalidOperationException($"Git checkout failed: {result.Error}");
        }

        var currentBranchResult = await RunRemoteGitCommandAsync(
            userId, token, null, cancellationToken, "-C", repoPath, "branch", "--show-current");

        return JsonSerializer.Serialize(new
        {
            success = true,
            action,
            branch,
            directory = repoPath,
            currentBranch = currentBranchResult.Output?.Trim(),
            message = action switch
            {
                "created" => $"Created and checked out new branch '{branch}'" +
                            (fromBranch != null ? $" from '{fromBranch}'" : ""),
                "tracked" => $"Checked out remote branch '{branch}'",
                _ => $"Switched to branch '{branch}'"
            }
        });
    }

    private static async Task<(bool Success, string? Output, string? Error)> RunGitCommandAsync(
        string workingDirectory,
        string? token,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        GitHubGitAuth.Apply(process.StartInfo, token);
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        return (process.ExitCode == 0, output, error);
    }

    private async Task<(bool Success, string? Output, string? Error)> RunRemoteGitCommandAsync(
        Guid userId,
        string? token,
        string? workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var shellCommand = GitHubGitAuth.BuildShellCommand(token, arguments);
        var result = await _workspace.ExecuteCommandAsync(
            userId,
            shellCommand,
            null,
            workingDirectory,
            cancellationToken);

        return (result.ExitCode == 0, result.StandardOutput, result.StandardError);
    }

    private static async Task SanitizeOriginUrlAsync(
        string repoPath,
        string? token,
        CancellationToken cancellationToken)
    {
        var originUrlResult = await RunGitCommandAsync(
            repoPath,
            token,
            cancellationToken,
            "remote",
            "get-url",
            "origin");

        if (!originUrlResult.Success || string.IsNullOrWhiteSpace(originUrlResult.Output))
        {
            return;
        }

        var originUrl = originUrlResult.Output.Trim();
        var sanitizedUrl = GitHubGitAuth.SanitizeRemoteUrl(originUrl);

        if (string.Equals(originUrl, sanitizedUrl, StringComparison.Ordinal))
        {
            return;
        }

        var setUrlResult = await RunGitCommandAsync(
            repoPath,
            token,
            cancellationToken,
            "remote",
            "set-url",
            "origin",
            sanitizedUrl);

        if (!setUrlResult.Success)
        {
            throw new InvalidOperationException($"Failed to sanitize repository remote: {setUrlResult.Error}");
        }
    }

    private async Task SanitizeOriginUrlRemoteAsync(
        Guid userId,
        string repoPath,
        string? token,
        CancellationToken cancellationToken)
    {
        var originUrlResult = await RunRemoteGitCommandAsync(
            userId,
            token,
            null,
            cancellationToken,
            "-C",
            repoPath,
            "remote",
            "get-url",
            "origin");

        if (!originUrlResult.Success || string.IsNullOrWhiteSpace(originUrlResult.Output))
        {
            return;
        }

        var originUrl = originUrlResult.Output.Trim();
        var sanitizedUrl = GitHubGitAuth.SanitizeRemoteUrl(originUrl);

        if (string.Equals(originUrl, sanitizedUrl, StringComparison.Ordinal))
        {
            return;
        }

        var setUrlResult = await RunRemoteGitCommandAsync(
            userId,
            token,
            null,
            cancellationToken,
            "-C",
            repoPath,
            "remote",
            "set-url",
            "origin",
            sanitizedUrl);

        if (!setUrlResult.Success)
        {
            throw new InvalidOperationException($"Failed to sanitize repository remote: {setUrlResult.Error}");
        }
    }
}
