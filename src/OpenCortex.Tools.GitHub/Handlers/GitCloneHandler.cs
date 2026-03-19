using System.Diagnostics;
using System.Text.Json;

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

        // Determine target directory
        var targetDir = directory;
        if (string.IsNullOrEmpty(targetDir))
        {
            // Extract repo name from URL
            var parts = repoUrl.TrimEnd('/').Split('/');
            targetDir = parts[^1].Replace(".git", "");
        }

        if (_workspace.SupportsContainerIsolation)
        {
            return await ExecuteRemoteAsync(
                context.UserId,
                targetDir,
                repoUrl,
                branch,
                token,
                cancellationToken);
        }

        if (string.IsNullOrEmpty(context.WorkspacePath))
        {
            throw new InvalidOperationException("Workspace path not configured");
        }

        if (!string.IsNullOrEmpty(directory) && Path.IsPathRooted(directory))
        {
            throw new InvalidOperationException(
                "directory must be relative to the workspace in local mode");
        }

        Directory.CreateDirectory(context.WorkspacePath);

        var fullTargetPath = Path.Combine(context.WorkspacePath, targetDir);

        // Check if already cloned
        if (Directory.Exists(fullTargetPath))
        {
            // If it's a git repo, just fetch and checkout
            if (Directory.Exists(Path.Combine(fullTargetPath, ".git")))
            {
                if (!string.IsNullOrEmpty(branch))
                {
                    var fetchResult = await RunGitCommandAsync(
                        fullTargetPath, token, cancellationToken, "fetch", "origin", branch);
                    var checkoutResult = await RunGitCommandAsync(
                        fullTargetPath, token, cancellationToken, "checkout", branch);

                    if (!fetchResult.Success)
                    {
                        throw new InvalidOperationException($"Git fetch failed: {fetchResult.Error}");
                    }

                    if (!checkoutResult.Success)
                    {
                        throw new InvalidOperationException($"Git checkout failed: {checkoutResult.Error}");
                    }

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

            throw new InvalidOperationException(
                $"Directory '{targetDir}' already exists and is not a git repository");
        }

        var cloneArguments = new List<string> { "clone", repoUrl };
        if (!string.IsNullOrEmpty(branch))
        {
            cloneArguments.Add("--branch");
            cloneArguments.Add(branch);
        }

        cloneArguments.Add(targetDir);

        var result = await RunGitCommandAsync(
            context.WorkspacePath,
            token,
            cancellationToken,
            cloneArguments.ToArray());

        if (!result.Success)
        {
            throw new InvalidOperationException($"Git clone failed: {result.Error}");
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

    private async Task<string> ExecuteRemoteAsync(
        Guid userId,
        string targetDir,
        string repoUrl,
        string? branch,
        string? token,
        CancellationToken cancellationToken)
    {
        var workspacePath = await _workspace.GetWorkspacePathAsync(userId, cancellationToken);
        var fullTargetPath = _workspace.ResolvePath(userId, targetDir);

        var pathExistsResult = await _workspace.ExecuteCommandAsync(
            userId,
            $"test -e {GitHubGitAuth.SingleQuote(fullTargetPath)}",
            null,
            null,
            cancellationToken);

        if (pathExistsResult.ExitCode == 0)
        {
            var isRepoResult = await RunRemoteGitCommandAsync(
                userId,
                token,
                null,
                cancellationToken,
                "-C",
                fullTargetPath,
                "rev-parse",
                "--is-inside-work-tree");

            if (isRepoResult.Success)
            {
                if (!string.IsNullOrEmpty(branch))
                {
                    var fetchResult = await RunRemoteGitCommandAsync(
                        userId,
                        token,
                        null,
                        cancellationToken,
                        "-C",
                        fullTargetPath,
                        "fetch",
                        "origin",
                        branch);

                    if (!fetchResult.Success)
                    {
                        throw new InvalidOperationException($"Git fetch failed: {fetchResult.Error}");
                    }

                    var checkoutResult = await RunRemoteGitCommandAsync(
                        userId,
                        token,
                        null,
                        cancellationToken,
                        "-C",
                        fullTargetPath,
                        "checkout",
                        branch);

                    if (!checkoutResult.Success)
                    {
                        throw new InvalidOperationException($"Git checkout failed: {checkoutResult.Error}");
                    }

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

            throw new InvalidOperationException(
                $"Directory '{targetDir}' already exists and is not a git repository");
        }

        var gitArgs = new List<string> { "clone", repoUrl };
        if (!string.IsNullOrEmpty(branch))
        {
            gitArgs.Add("--branch");
            gitArgs.Add(branch);
        }

        gitArgs.Add(targetDir);

        var cloneResult = await RunRemoteGitCommandAsync(
            userId,
            token,
            workspacePath,
            cancellationToken,
            gitArgs.ToArray());

        if (!cloneResult.Success)
        {
            throw new InvalidOperationException($"Git clone failed: {cloneResult.Error}");
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
}
