using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenCortex.Tools;

/// <summary>
/// Local filesystem-based workspace manager.
/// Provides sandboxed directories for each user.
/// </summary>
public sealed class LocalWorkspaceManager : IWorkspaceManager
{
    private readonly ToolsOptions _options;
    private readonly ILogger<LocalWorkspaceManager> _logger;
    private readonly Dictionary<Guid, string> _workspaceCache = new();
    private readonly Dictionary<Guid, DateTime> _lastActivity = new();
    private readonly object _lock = new();

    public bool SupportsContainerIsolation => false;

    public LocalWorkspaceManager(
        IOptions<ToolsOptions> options,
        ILogger<LocalWorkspaceManager> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Ensure base path exists
        var basePath = GetAbsoluteBasePath();
        if (!Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
            _logger.LogInformation("Created workspace base directory: {BasePath}", basePath);
        }
    }

    public Task<string> GetWorkspacePathAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_workspaceCache.TryGetValue(userId, out var cached))
            {
                return Task.FromResult(cached);
            }

            var workspacePath = Path.Combine(GetAbsoluteBasePath(), userId.ToString("N"));

            if (!Directory.Exists(workspacePath))
            {
                Directory.CreateDirectory(workspacePath);
                _logger.LogInformation("Created workspace for user {UserId}: {Path}", userId, workspacePath);
            }

            _workspaceCache[userId] = workspacePath;
            return Task.FromResult(workspacePath);
        }
    }

    public bool IsPathAllowed(Guid userId, string path)
    {
        try
        {
            var workspacePath = Path.Combine(GetAbsoluteBasePath(), userId.ToString("N"));
            var normalizedWorkspace = Path.GetFullPath(workspacePath);
            var normalizedPath = Path.GetFullPath(Path.Combine(normalizedWorkspace, path));

            // Check that the normalized path starts with the workspace path
            return normalizedPath.StartsWith(normalizedWorkspace, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public string ResolvePath(Guid userId, string relativePath)
    {
        var workspacePath = Path.Combine(GetAbsoluteBasePath(), userId.ToString("N"));
        var normalizedWorkspace = Path.GetFullPath(workspacePath);
        var resolvedPath = Path.GetFullPath(Path.Combine(normalizedWorkspace, relativePath));

        // Security check: ensure path doesn't escape workspace
        if (!resolvedPath.StartsWith(normalizedWorkspace, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Path '{relativePath}' resolves outside the workspace. Access denied.");
        }

        return resolvedPath;
    }

    public async Task CleanupExpiredWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        var basePath = GetAbsoluteBasePath();
        if (!Directory.Exists(basePath))
        {
            return;
        }

        var expiration = TimeSpan.FromMinutes(_options.SessionTimeoutMinutes);
        var cutoff = DateTime.UtcNow - expiration;

        foreach (var dir in Directory.EnumerateDirectories(basePath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lastWrite = Directory.GetLastWriteTimeUtc(dir);
            if (lastWrite < cutoff)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogInformation("Cleaned up expired workspace: {Path}", dir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup workspace: {Path}", dir);
                }
            }
        }

        await Task.CompletedTask;
    }

    public Task DeleteWorkspaceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _workspaceCache.Remove(userId);
            _lastActivity.Remove(userId);
        }

        var workspacePath = Path.Combine(GetAbsoluteBasePath(), userId.ToString("N"));

        if (Directory.Exists(workspacePath))
        {
            Directory.Delete(workspacePath, recursive: true);
            _logger.LogInformation("Deleted workspace for user {UserId}", userId);
        }

        return Task.CompletedTask;
    }

    public async Task<WorkspaceStatus> EnsureRunningAsync(
        Guid userId,
        IReadOnlyDictionary<string, string>? credentials = null,
        CancellationToken cancellationToken = default)
    {
        // For local mode, just ensure the directory exists
        var workspacePath = await GetWorkspacePathAsync(userId, cancellationToken);

        lock (_lock)
        {
            _lastActivity[userId] = DateTime.UtcNow;
        }

        return new WorkspaceStatus
        {
            UserId = userId,
            State = WorkspaceState.Running,
            WorkspacePath = workspacePath,
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            Message = "Local workspace ready"
        };
    }

    public async Task<CommandResult> ExecuteCommandAsync(
        Guid userId,
        string command,
        string? arguments = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = await GetWorkspacePathAsync(userId, cancellationToken);

        // Determine working directory
        var workDir = workspacePath;
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            workDir = ResolvePath(userId, workingDirectory);
        }

        lock (_lock)
        {
            _lastActivity[userId] = DateTime.UtcNow;
        }

        var stopwatch = Stopwatch.StartNew();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            stopwatch.Stop();

            return new CommandResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = await outputTask,
                StandardError = await errorTask,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new CommandResult
            {
                ExitCode = -1,
                StandardOutput = string.Empty,
                StandardError = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    public Task<WorkspaceStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var workspacePath = Path.Combine(GetAbsoluteBasePath(), userId.ToString("N"));
        var exists = Directory.Exists(workspacePath);

        DateTime? lastActivity = null;
        lock (_lock)
        {
            if (_lastActivity.TryGetValue(userId, out var activity))
            {
                lastActivity = activity;
            }
        }

        return Task.FromResult(new WorkspaceStatus
        {
            UserId = userId,
            State = exists ? WorkspaceState.Running : WorkspaceState.NotExists,
            WorkspacePath = exists ? workspacePath : null,
            LastActivityAt = lastActivity,
            Message = exists ? "Local workspace exists" : "No workspace"
        });
    }

    public Task StopAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // No-op for local mode - workspaces are just directories
        _logger.LogDebug("StopAsync called for local workspace (no-op): {UserId}", userId);
        return Task.CompletedTask;
    }

    private string GetAbsoluteBasePath()
    {
        var basePath = _options.WorkspaceBasePath;

        if (Path.IsPathRooted(basePath))
        {
            return basePath;
        }

        // Relative paths are relative to the current directory
        return Path.GetFullPath(basePath);
    }
}
