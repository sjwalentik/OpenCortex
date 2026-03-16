using System.Diagnostics;
using System.Text.Json;

namespace OpenCortex.Tools.GitHub.Handlers;

/// <summary>
/// Handler for the git_checkout tool - checkout a branch in a cloned repository.
/// </summary>
public sealed class GitCheckoutHandler : IToolHandler
{
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

        // Option to create branch if it doesn't exist
        var createIfNotExists = arguments.TryGetProperty("create", out var createElement)
            && createElement.GetBoolean();

        // Option to specify source branch when creating
        var fromBranch = arguments.TryGetProperty("from_branch", out var fromElement)
            ? fromElement.GetString()
            : null;

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

        // Fetch latest from remote first
        await RunGitCommandAsync(repoPath, "fetch --all", cancellationToken);

        // Check if branch exists locally or remotely
        var branchExistsResult = await RunGitCommandAsync(
            repoPath, $"rev-parse --verify {branch}", cancellationToken);
        var remoteBranchExistsResult = await RunGitCommandAsync(
            repoPath, $"rev-parse --verify origin/{branch}", cancellationToken);

        var branchExists = branchExistsResult.Success || remoteBranchExistsResult.Success;

        if (!branchExists && !createIfNotExists)
        {
            // List available branches for helpful error
            var listResult = await RunGitCommandAsync(repoPath, "branch -a", cancellationToken);
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
                await RunGitCommandAsync(repoPath, $"checkout {fromBranch}", cancellationToken);
                await RunGitCommandAsync(repoPath, "pull", cancellationToken);
            }

            checkoutCommand = $"checkout -b {branch}";
            action = "created";
        }
        else if (remoteBranchExistsResult.Success && !branchExistsResult.Success)
        {
            // Track remote branch
            checkoutCommand = $"checkout -t origin/{branch}";
            action = "tracked";
        }
        else
        {
            // Checkout existing branch
            checkoutCommand = $"checkout {branch}";
            action = "switched";
        }

        var result = await RunGitCommandAsync(repoPath, checkoutCommand, cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException($"Git checkout failed: {result.Error}");
        }

        // Get current branch to confirm
        var currentBranchResult = await RunGitCommandAsync(
            repoPath, "branch --show-current", cancellationToken);

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
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        return (process.ExitCode == 0, output, error);
    }
}
