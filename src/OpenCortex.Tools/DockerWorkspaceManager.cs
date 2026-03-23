using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenCortex.Tools;

/// <summary>
/// Docker-based workspace manager.
/// Each user gets an isolated container with a persistent volume.
/// </summary>
public sealed class DockerWorkspaceManager : IWorkspaceManager, IDisposable
{
    private readonly ToolsOptions _options;
    private readonly ILogger<DockerWorkspaceManager> _logger;
    private readonly ConcurrentDictionary<Guid, ContainerInfo> _containers = new();
    private readonly Timer _cleanupTimer;
    private readonly SemaphoreSlim _containerLock = new(1, 1);
    private bool _disposed;

    private const string WorkspacePathInContainer = "/workspace";
    private const string ContainerPrefix = "opencortex-agent-";

    public bool SupportsContainerIsolation => true;

    public DockerWorkspaceManager(
        IOptions<ToolsOptions> options,
        ILogger<DockerWorkspaceManager> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Start cleanup timer (runs every minute)
        _cleanupTimer = new Timer(
            _ => _ = CleanupIdleContainersAsync(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));

        // Ensure docker network exists
        _ = EnsureNetworkExistsAsync();
    }

    public Task<string> GetWorkspacePathAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Return the path inside the container
        return Task.FromResult(WorkspacePathInContainer);
    }

    public bool IsPathAllowed(Guid userId, string path)
    {
        // All paths inside container are relative to /workspace
        // Basic path traversal check
        var normalized = Path.GetFullPath(Path.Combine(WorkspacePathInContainer, path));
        return IsWithinWorkspace(normalized);
    }

    public string ResolvePath(Guid userId, string relativePath)
    {
        var resolved = Path.Combine(WorkspacePathInContainer, relativePath).Replace('\\', '/');
        var normalized = Path.GetFullPath(resolved).Replace('\\', '/');

        if (!IsWithinWorkspace(normalized))
        {
            throw new UnauthorizedAccessException(
                $"Path '{relativePath}' resolves outside the workspace. Access denied.");
        }

        return normalized;
    }

    public async Task CleanupExpiredWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        await CleanupIdleContainersAsync(cancellationToken);
    }

    public async Task DeleteWorkspaceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var containerName = GetContainerName(userId);
        var volumeName = GetVolumeName(userId);

        // Stop and remove container
        await StopAsync(userId, cancellationToken);

        // Remove volume (deletes all data)
        var result = await RunDockerCommandAsync($"volume rm {volumeName}", cancellationToken);
        if (result.Success)
        {
            _logger.LogInformation("Deleted volume for user {UserId}", userId);
        }
    }

    public async Task<WorkspaceStatus> EnsureRunningAsync(
        Guid userId,
        IReadOnlyDictionary<string, string>? credentials = null,
        CancellationToken cancellationToken = default)
    {
        var containerName = GetContainerName(userId);

        await _containerLock.WaitAsync(cancellationToken);
        try
        {
            // Check if container already exists and is running
            var existingStatus = await GetContainerStatusAsync(containerName, cancellationToken);

            if (existingStatus == "running")
            {
                await SyncCodexAuthStateAsync(userId, credentials, cancellationToken);
                UpdateLastActivity(userId);
                return new WorkspaceStatus
                {
                    UserId = userId,
                    State = WorkspaceState.Running,
                    ContainerId = containerName,
                    WorkspacePath = WorkspacePathInContainer,
                    LastActivityAt = DateTime.UtcNow,
                    Message = "Container already running"
                };
            }

            if (existingStatus == "exited" || existingStatus == "created")
            {
                // Start existing container
                var startResult = await RunDockerCommandAsync($"start {containerName}", cancellationToken);
                if (startResult.Success)
                {
                    await SyncCodexAuthStateAsync(userId, credentials, cancellationToken);
                    UpdateLastActivity(userId);
                    return new WorkspaceStatus
                    {
                        UserId = userId,
                        State = WorkspaceState.Running,
                        ContainerId = containerName,
                        WorkspacePath = WorkspacePathInContainer,
                        StartedAt = DateTime.UtcNow,
                        LastActivityAt = DateTime.UtcNow,
                        Message = "Container started"
                    };
                }
            }

            // Create new container
            return await CreateContainerAsync(userId, credentials, cancellationToken);
        }
        finally
        {
            _containerLock.Release();
        }
    }

    public async Task<CommandResult> ExecuteCommandAsync(
        Guid userId,
        string command,
        string? arguments = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        IReadOnlyList<string>? argumentList = null,
        string? standardInput = null,
        CancellationToken cancellationToken = default)
    {
        var containerName = GetContainerName(userId);

        // Ensure container is running
        var status = await GetContainerStatusAsync(containerName, cancellationToken);
        if (status != "running")
        {
            await EnsureRunningAsync(userId, null, cancellationToken);
        }

        UpdateLastActivity(userId);

        // Build exec command
        var workDir = string.IsNullOrEmpty(workingDirectory)
            ? WorkspacePathInContainer
            : ResolvePath(userId, workingDirectory);

        var fullCommand = BuildShellCommand(command, arguments, argumentList, environmentVariables);
        var shellScript = $"cd -- {ShellEscaping.SingleQuote(workDir)} && {fullCommand}";

        var stopwatch = Stopwatch.StartNew();
        var result = await RunDockerExecAsync(containerName, shellScript, standardInput, cancellationToken);
        stopwatch.Stop();

        return new CommandResult
        {
            ExitCode = result.ExitCode,
            StandardOutput = result.Output ?? string.Empty,
            StandardError = result.Error ?? string.Empty,
            Duration = stopwatch.Elapsed
        };
    }

    public async Task<WorkspaceStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var containerName = GetContainerName(userId);
        var status = await GetContainerStatusAsync(containerName, cancellationToken);

        DateTime? lastActivity = null;
        if (_containers.TryGetValue(userId, out var info))
        {
            lastActivity = info.LastActivityAt;
        }

        var state = status switch
        {
            "running" => WorkspaceState.Running,
            "created" => WorkspaceState.Stopped,
            "exited" => WorkspaceState.Stopped,
            "paused" => WorkspaceState.Stopped,
            "restarting" => WorkspaceState.Starting,
            null => WorkspaceState.NotExists,
            _ => WorkspaceState.Failed
        };

        return new WorkspaceStatus
        {
            UserId = userId,
            State = state,
            ContainerId = status != null ? containerName : null,
            WorkspacePath = status == "running" ? WorkspacePathInContainer : null,
            LastActivityAt = lastActivity,
            Message = status ?? "No container"
        };
    }

    public async Task StopAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var containerName = GetContainerName(userId);

        var result = await RunDockerCommandAsync($"stop {containerName}", cancellationToken, timeoutSeconds: 30);
        if (result.Success)
        {
            _containers.TryRemove(userId, out _);
            _logger.LogInformation("Stopped container for user {UserId}", userId);
        }
    }

    private async Task<WorkspaceStatus> CreateContainerAsync(
        Guid userId,
        IReadOnlyDictionary<string, string>? credentials,
        CancellationToken cancellationToken)
    {
        var containerName = GetContainerName(userId);
        var volumeName = GetVolumeName(userId);

        // Ensure volume exists
        await RunDockerCommandAsync($"volume create {volumeName}", cancellationToken);

        // Build run command
        var runCommand = new StringBuilder();
        runCommand.Append($"run -d --name {containerName} ");
        runCommand.Append($"--network {_options.DockerNetwork} ");
        runCommand.Append($"-v {volumeName}:{WorkspacePathInContainer} ");
        runCommand.Append($"--memory={_options.MemoryLimit} ");
        runCommand.Append($"--cpus={_options.CpuLimit} ");
        runCommand.Append("--security-opt=no-new-privileges:true ");
        runCommand.Append("--cap-drop=ALL ");
        runCommand.Append("--user=1000:1000 ");
        runCommand.Append(_options.ContainerImage);

        var result = await RunDockerCommandAsync(runCommand.ToString(), cancellationToken);

        if (!result.Success)
        {
            var safeError = SensitiveDataRedactor.Redact(result.Error, credentials)
                ?? "Container creation failed.";
            _logger.LogError("Failed to create container for user {UserId}: {Error}", userId, safeError);
            return new WorkspaceStatus
            {
                UserId = userId,
                State = WorkspaceState.Failed,
                Message = $"Failed to create container: {safeError}"
            };
        }

        var containerId = result.Output?.Trim();
        _containers[userId] = new ContainerInfo
        {
            ContainerId = containerId ?? containerName,
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        _logger.LogInformation("Created container {ContainerName} for user {UserId}", containerName, userId);

        await SyncCodexAuthStateAsync(userId, credentials, cancellationToken);

        return new WorkspaceStatus
        {
            UserId = userId,
            State = WorkspaceState.Running,
            ContainerId = containerId,
            WorkspacePath = WorkspacePathInContainer,
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            Message = "Container created and started"
        };
    }

    private async Task SyncCodexAuthStateAsync(
        Guid userId,
        IReadOnlyDictionary<string, string>? credentials,
        CancellationToken cancellationToken)
    {
        var sessionJson = credentials?.GetValueOrDefault(WorkspaceRuntimePaths.CodexProviderId);
        var authFilePath = WorkspaceRuntimePaths.GetCodexAuthFilePath(
            supportsContainerIsolation: true,
            WorkspacePathInContainer);
        var authDirectory = Path.GetDirectoryName(authFilePath)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(authDirectory))
        {
            return;
        }

        var containerName = GetContainerName(userId);
        var syncScript = string.IsNullOrWhiteSpace(sessionJson)
            ? $"rm -f {ShellEscaping.SingleQuote(authFilePath)}"
            : BuildCodexAuthWriteScript(authDirectory, authFilePath, sessionJson);

        await RunDockerExecAsync(containerName, syncScript, null, cancellationToken);
    }

    private static string BuildCodexAuthWriteScript(
        string authDirectory,
        string authFilePath,
        string sessionJson)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(sessionJson));
        return
            $"mkdir -p {ShellEscaping.SingleQuote(authDirectory)} " +
            $"&& printf %s {ShellEscaping.SingleQuote(encoded)} | base64 -d > {ShellEscaping.SingleQuote(authFilePath)} " +
            $"&& chmod 600 {ShellEscaping.SingleQuote(authFilePath)}";
    }

    private static string BuildShellCommand(
        string command,
        string? arguments,
        IReadOnlyList<string>? argumentList,
        IReadOnlyDictionary<string, string>? environmentVariables)
    {
        var builder = new StringBuilder();

        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                builder.Append(key)
                    .Append('=')
                    .Append(ShellEscaping.SingleQuote(value))
                    .Append(' ');
            }
        }

        builder.Append(ShellEscaping.SingleQuote(command));

        if (argumentList is { Count: > 0 })
        {
            foreach (var argument in argumentList)
            {
                builder.Append(' ').Append(ShellEscaping.SingleQuote(argument));
            }
        }
        else if (!string.IsNullOrWhiteSpace(arguments))
        {
            builder.Append(' ').Append(arguments);
        }

        return builder.ToString();
    }

    private static bool IsWithinWorkspace(string candidatePath)
    {
        var normalizedRoot = WorkspacePathInContainer.TrimEnd('/');
        var normalizedCandidate = candidatePath.Replace('\\', '/').TrimEnd('/');

        return string.Equals(normalizedCandidate, normalizedRoot, StringComparison.Ordinal)
            || normalizedCandidate.StartsWith(normalizedRoot + "/", StringComparison.Ordinal);
    }

    private async Task<string?> GetContainerStatusAsync(string containerName, CancellationToken cancellationToken)
    {
        var result = await RunDockerCommandAsync(
            $"inspect --format \"{{{{.State.Status}}}}\" {containerName}",
            cancellationToken);

        if (!result.Success)
        {
            return null;
        }

        return result.Output?.Trim().Trim('"');
    }

    private async Task EnsureNetworkExistsAsync()
    {
        var result = await RunDockerCommandAsync(
            $"network inspect {_options.DockerNetwork}",
            CancellationToken.None);

        if (!result.Success)
        {
            // Create network
            await RunDockerCommandAsync(
                $"network create {_options.DockerNetwork}",
                CancellationToken.None);
            _logger.LogInformation("Created Docker network: {Network}", _options.DockerNetwork);
        }
    }

    private async Task CleanupIdleContainersAsync(CancellationToken cancellationToken = default)
    {
        var idleTimeout = TimeSpan.FromMinutes(_options.SessionTimeoutMinutes);
        var cutoff = DateTime.UtcNow - idleTimeout;

        foreach (var (userId, info) in _containers)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (info.LastActivityAt < cutoff)
            {
                _logger.LogInformation(
                    "Stopping idle container for user {UserId} (idle since {IdleSince})",
                    userId, info.LastActivityAt);

                await StopAsync(userId, cancellationToken);
            }
        }
    }

    private void UpdateLastActivity(Guid userId)
    {
        _containers.AddOrUpdate(
            userId,
            _ => new ContainerInfo
            {
                ContainerId = GetContainerName(userId),
                StartedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            },
            (_, existing) =>
            {
                existing.LastActivityAt = DateTime.UtcNow;
                return existing;
            });
    }

    private static string GetContainerName(Guid userId) => $"{ContainerPrefix}{userId:N}";
    private static string GetVolumeName(Guid userId) => $"opencortex-workspace-{userId:N}";

    private static async Task<(bool Success, int ExitCode, string? Output, string? Error)> RunDockerCommandAsync(
        string arguments,
        CancellationToken cancellationToken,
        int timeoutSeconds = 60)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            var output = await outputTask;
            var error = await errorTask;

            return (process.ExitCode == 0, process.ExitCode, output, error);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill();
            }
            catch { }

            return (false, -1, null, "Command timed out");
        }
    }

    private static async Task<(bool Success, int ExitCode, string? Output, string? Error)> RunDockerExecAsync(
        string containerName,
        string shellCommand,
        string? standardInput,
        CancellationToken cancellationToken,
        int timeoutSeconds = 60)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardInput = standardInput is not null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("exec");
        if (standardInput is not null)
        {
            process.StartInfo.ArgumentList.Add("-i");
        }
        process.StartInfo.ArgumentList.Add(containerName);
        process.StartInfo.ArgumentList.Add("/bin/sh");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(shellCommand);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            process.Start();

            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cts.Token);
                process.StandardInput.Close();
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            var output = await outputTask;
            var error = await errorTask;

            return (process.ExitCode == 0, process.ExitCode, output, error);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill();
            }
            catch { }

            return (false, -1, null, "Command timed out");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cleanupTimer.Dispose();
        _containerLock.Dispose();
    }

    private sealed class ContainerInfo
    {
        public required string ContainerId { get; init; }
        public required DateTime StartedAt { get; init; }
        public DateTime LastActivityAt { get; set; }
    }
}
