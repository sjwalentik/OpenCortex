using System.Diagnostics;
using System.Text.Json;

namespace OpenCortex.Tools.GitHub.Handlers;

/// <summary>
/// Handler for the git_clone tool - clones a repository to the user's workspace.
/// </summary>
public sealed class GitCloneHandler : IToolHandler
{
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

        // Ensure workspace exists
        if (string.IsNullOrEmpty(context.WorkspacePath))
        {
            throw new InvalidOperationException("Workspace path not configured");
        }

        Directory.CreateDirectory(context.WorkspacePath);

        // Determine target directory
        var targetDir = directory;
        if (string.IsNullOrEmpty(targetDir))
        {
            // Extract repo name from URL
            var parts = repoUrl.TrimEnd('/').Split('/');
            targetDir = parts[^1].Replace(".git", "");
        }

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
                        fullTargetPath, $"fetch origin {branch}", token, cancellationToken);
                    var checkoutResult = await RunGitCommandAsync(
                        fullTargetPath, $"checkout {branch}", token, cancellationToken);

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

        // Build clone command
        var cloneArgs = $"clone {repoUrl}";
        if (!string.IsNullOrEmpty(branch))
        {
            cloneArgs += $" --branch {branch}";
        }
        cloneArgs += $" {targetDir}";

        var result = await RunGitCommandAsync(context.WorkspacePath, cloneArgs, token, cancellationToken);

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

    private static async Task<(bool Success, string? Output, string? Error)> RunGitCommandAsync(
        string workingDirectory,
        string arguments,
        string? token,
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

        GitHubGitAuth.Apply(process.StartInfo, token);
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        return (process.ExitCode == 0, output, error);
    }
}
