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

            return IsWithinWorkspace(normalizedWorkspace, normalizedPath);
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
        if (!IsWithinWorkspace(normalizedWorkspace, resolvedPath))
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
        SyncCodexAuthState(workspacePath, credentials);
        SyncClaudeAuthState(workspacePath, credentials);

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
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        IReadOnlyList<string>? argumentList = null,
        string? standardInput = null,
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
                WorkingDirectory = workDir,
                RedirectStandardInput = standardInput is not null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (argumentList is { Count: > 0 })
        {
            foreach (var argument in argumentList)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }
        }

        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                process.StartInfo.Environment[key] = value;
            }
        }

        try
        {
            process.Start();

            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
                process.StandardInput.Close();
            }

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
            _logger.LogWarning(ex, "Local workspace command execution failed for user {UserId}", userId);
            stopwatch.Stop();
            return new CommandResult
            {
                ExitCode = -1,
                StandardOutput = string.Empty,
                StandardError = "Command execution failed.",
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

    private static bool IsWithinWorkspace(string workspaceRoot, string candidatePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(workspaceRoot);
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(candidatePath);

        return string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.AltDirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void SyncCodexAuthState(
        string workspacePath,
        IReadOnlyDictionary<string, string>? credentials)
    {
        var authFilePath = WorkspaceRuntimePaths.GetCodexAuthFilePath(
            supportsContainerIsolation: false,
            workspacePath);
        var authDirectory = Path.GetDirectoryName(authFilePath);
        var sessionJson = credentials?.GetValueOrDefault(WorkspaceRuntimePaths.CodexProviderId);

        if (string.IsNullOrWhiteSpace(sessionJson))
        {
            if (File.Exists(authFilePath))
            {
                File.Delete(authFilePath);
            }

            if (!string.IsNullOrWhiteSpace(authDirectory) && Directory.Exists(authDirectory))
            {
                TryDeleteEmptyDirectory(authDirectory);
                var codexRoot = Directory.GetParent(authDirectory)?.FullName;
                if (!string.IsNullOrWhiteSpace(codexRoot))
                {
                    TryDeleteEmptyDirectory(codexRoot);
                }
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(authDirectory))
        {
            return;
        }

        Directory.CreateDirectory(authDirectory);
        File.WriteAllText(authFilePath, sessionJson);
    }

    private static void SyncClaudeAuthState(
        string workspacePath,
        IReadOnlyDictionary<string, string>? credentials)
    {
        var credentialsFilePath = WorkspaceRuntimePaths.GetClaudeCredentialsFilePath(
            supportsContainerIsolation: false,
            workspacePath);
        var credentialsDirectory = Path.GetDirectoryName(credentialsFilePath);
        var credentialsJson = credentials?.GetValueOrDefault(WorkspaceRuntimePaths.ClaudeCliProviderId);

        if (string.IsNullOrWhiteSpace(credentialsJson))
        {
            if (File.Exists(credentialsFilePath))
            {
                File.Delete(credentialsFilePath);
            }

            if (!string.IsNullOrWhiteSpace(credentialsDirectory) && Directory.Exists(credentialsDirectory))
            {
                TryDeleteEmptyDirectory(credentialsDirectory);
                var claudeRoot = Directory.GetParent(credentialsDirectory)?.FullName;
                if (!string.IsNullOrWhiteSpace(claudeRoot))
                {
                    TryDeleteEmptyDirectory(claudeRoot);
                }
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(credentialsDirectory))
            {
                Directory.CreateDirectory(credentialsDirectory);
                File.WriteAllText(credentialsFilePath, credentialsJson);
            }
        }

        // Write ~/.claude/settings.json with MCP server configuration when a token is available
        var mcpToken = credentials?.GetValueOrDefault(WorkspaceRuntimePaths.ClaudeMcpTokenKey);
        var mcpServerUrl = credentials?.GetValueOrDefault(WorkspaceRuntimePaths.ClaudeMcpServerUrlKey);
        if (!string.IsNullOrWhiteSpace(mcpToken) && !string.IsNullOrWhiteSpace(mcpServerUrl))
        {
            var settingsFilePath = WorkspaceRuntimePaths.GetClaudeGlobalSettingsPath(
                supportsContainerIsolation: false,
                workspacePath);
            var settingsDirectory = Path.GetDirectoryName(settingsFilePath);
            if (!string.IsNullOrWhiteSpace(settingsDirectory))
            {
                Directory.CreateDirectory(settingsDirectory);
                File.WriteAllText(settingsFilePath, BuildClaudeMcpSettingsJson(mcpServerUrl, mcpToken));
            }
        }
    }

    private static string BuildClaudeMcpSettingsJson(string mcpServerUrl, string mcpToken)
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["OpenCortex"] = new
                {
                    type = "http",
                    url = mcpServerUrl,
                    headers = new Dictionary<string, string>
                    {
                        ["Authorization"] = $"Bearer {mcpToken}"
                    }
                }
            },
            permissions = new
            {
                allow = new[] { "mcp__OpenCortex__*" }
            }
        });
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)
                && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, recursive: false);
            }
        }
        catch
        {
        }
    }
}
