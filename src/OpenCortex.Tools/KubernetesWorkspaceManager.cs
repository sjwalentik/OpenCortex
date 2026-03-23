using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenCortex.Tools;

/// <summary>
/// Kubernetes-based workspace manager.
/// Each user gets an isolated pod with a PersistentVolumeClaim for persistence.
/// Pods scale to zero after idle timeout.
/// </summary>
public sealed class KubernetesWorkspaceManager : IWorkspaceManager, IDisposable
{
    private readonly ToolsOptions _options;
    private readonly ILogger<KubernetesWorkspaceManager> _logger;
    private readonly ConcurrentDictionary<Guid, PodInfo> _pods = new();
    private readonly Timer _cleanupTimer;
    private readonly SemaphoreSlim _podLock = new(1, 1);
    private bool _disposed;

    private const string WorkspacePathInPod = "/workspace";
    private const string PodPrefix = "agent-";
    private const string PvcPrefix = "workspace-";
    private const string AppLabel = "opencortex-agent";

    public bool SupportsContainerIsolation => true;

    public KubernetesWorkspaceManager(
        IOptions<ToolsOptions> options,
        ILogger<KubernetesWorkspaceManager> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Start cleanup timer (runs every minute)
        _cleanupTimer = new Timer(
            _ => _ = CleanupIdlePodsAsync(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));

        // Ensure namespace exists
        _ = EnsureNamespaceExistsAsync();
    }

    public Task<string> GetWorkspacePathAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(WorkspacePathInPod);
    }

    public bool IsPathAllowed(Guid userId, string path)
    {
        var normalized = Path.GetFullPath(Path.Combine(WorkspacePathInPod, path)).Replace('\\', '/');
        return normalized.StartsWith(WorkspacePathInPod, StringComparison.Ordinal);
    }

    public string ResolvePath(Guid userId, string relativePath)
    {
        var resolved = Path.Combine(WorkspacePathInPod, relativePath).Replace('\\', '/');
        var normalized = Path.GetFullPath(resolved).Replace('\\', '/');

        if (!normalized.StartsWith(WorkspacePathInPod, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"Path '{relativePath}' resolves outside the workspace. Access denied.");
        }

        return normalized;
    }

    public async Task CleanupExpiredWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        await CleanupIdlePodsAsync(cancellationToken);
    }

    public async Task DeleteWorkspaceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var podName = GetPodName(userId);
        var pvcName = GetPvcName(userId);
        var ns = _options.KubernetesNamespace;

        // Delete pod
        await StopAsync(userId, cancellationToken);

        // Delete PVC (removes all user data)
        var result = await RunKubectlAsync($"delete pvc {pvcName} -n {ns} --ignore-not-found", cancellationToken);
        if (result.Success)
        {
            _logger.LogInformation("Deleted PVC for user {UserId}", userId);
        }
    }

    public async Task<WorkspaceStatus> EnsureRunningAsync(
        Guid userId,
        IReadOnlyDictionary<string, string>? credentials = null,
        CancellationToken cancellationToken = default)
    {
        var podName = GetPodName(userId);
        var ns = _options.KubernetesNamespace;

        await _podLock.WaitAsync(cancellationToken);
        try
        {
            // Check if pod exists and is running
            var existingStatus = await GetPodPhaseAsync(podName, ns, cancellationToken);

            if (existingStatus == "Running")
            {
                await SyncCodexAuthStateAsync(userId, credentials, cancellationToken);
                UpdateLastActivity(userId);
                return new WorkspaceStatus
                {
                    UserId = userId,
                    State = WorkspaceState.Running,
                    PodName = podName,
                    WorkspacePath = WorkspacePathInPod,
                    LastActivityAt = DateTime.UtcNow,
                    Message = "Pod already running"
                };
            }

            if (existingStatus == "Pending")
            {
                // Wait for it to be ready
                var readyStatus = await WaitForPodReadyAsync(userId, podName, ns, cancellationToken);
                if (readyStatus.State == WorkspaceState.Running)
                {
                    await SyncCodexAuthStateAsync(userId, credentials, cancellationToken);
                }

                return readyStatus;
            }

            // Need to create pod (and possibly PVC)
            return await CreatePodAsync(userId, credentials, cancellationToken);
        }
        finally
        {
            _podLock.Release();
        }
    }

    public async Task<CommandResult> ExecuteCommandAsync(
        Guid userId,
        string command,
        string? arguments = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        IReadOnlyList<string>? argumentList = null,
        CancellationToken cancellationToken = default)
    {
        var podName = GetPodName(userId);
        var ns = _options.KubernetesNamespace;

        // Ensure pod is running
        var status = await GetPodPhaseAsync(podName, ns, cancellationToken);
        if (status != "Running")
        {
            await EnsureRunningAsync(userId, null, cancellationToken);
        }

        UpdateLastActivity(userId);

        // Build exec command
        var workDir = string.IsNullOrEmpty(workingDirectory)
            ? WorkspacePathInPod
            : ResolvePath(userId, workingDirectory);

        var fullCommand = BuildShellCommand(command, arguments, argumentList, environmentVariables);
        var shellScript = $"cd -- {ShellEscaping.SingleQuote(workDir)} && {fullCommand}";

        var stopwatch = Stopwatch.StartNew();
        var result = await RunKubectlExecAsync(ns, podName, shellScript, cancellationToken);
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
        var podName = GetPodName(userId);
        var ns = _options.KubernetesNamespace;

        var phase = await GetPodPhaseAsync(podName, ns, cancellationToken);

        DateTime? lastActivity = null;
        if (_pods.TryGetValue(userId, out var info))
        {
            lastActivity = info.LastActivityAt;
        }

        var state = phase switch
        {
            "Running" => WorkspaceState.Running,
            "Pending" => WorkspaceState.Starting,
            "Succeeded" => WorkspaceState.Stopped,
            "Failed" => WorkspaceState.Failed,
            null => WorkspaceState.NotExists,
            _ => WorkspaceState.Failed
        };

        return new WorkspaceStatus
        {
            UserId = userId,
            State = state,
            PodName = phase != null ? podName : null,
            WorkspacePath = phase == "Running" ? WorkspacePathInPod : null,
            LastActivityAt = lastActivity,
            Message = phase ?? "No pod"
        };
    }

    public async Task StopAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var podName = GetPodName(userId);
        var ns = _options.KubernetesNamespace;

        var result = await RunKubectlAsync($"delete pod {podName} -n {ns} --ignore-not-found", cancellationToken);
        if (result.Success)
        {
            _pods.TryRemove(userId, out _);
            _logger.LogInformation("Deleted pod for user {UserId} (PVC preserved)", userId);
        }
    }

    private async Task<WorkspaceStatus> CreatePodAsync(
        Guid userId,
        IReadOnlyDictionary<string, string>? credentials,
        CancellationToken cancellationToken)
    {
        var podName = GetPodName(userId);
        var pvcName = GetPvcName(userId);
        var ns = _options.KubernetesNamespace;

        // Ensure PVC exists
        await EnsurePvcExistsAsync(userId, pvcName, ns, cancellationToken);

        // Build pod manifest
        var podYaml = BuildPodManifest(userId, podName, pvcName);

        // Apply pod
        var result = await RunKubectlWithStdinAsync("apply -f -", podYaml, cancellationToken);

        if (!result.Success)
        {
            var safeError = SensitiveDataRedactor.Redact(result.Error, credentials)
                ?? "Pod creation failed.";
            _logger.LogError("Failed to create pod for user {UserId}: {Error}", userId, safeError);
            return new WorkspaceStatus
            {
                UserId = userId,
                State = WorkspaceState.Failed,
                Message = $"Failed to create pod: {safeError}"
            };
        }

        // Wait for pod to be ready
        var workspaceStatus = await WaitForPodReadyAsync(userId, podName, ns, cancellationToken);
        if (workspaceStatus.State == WorkspaceState.Running)
        {
            await SyncCodexAuthStateAsync(userId, credentials, cancellationToken);
        }

        return workspaceStatus;
    }

    private async Task SyncCodexAuthStateAsync(
        Guid userId,
        IReadOnlyDictionary<string, string>? credentials,
        CancellationToken cancellationToken)
    {
        var sessionJson = credentials?.GetValueOrDefault(WorkspaceRuntimePaths.CodexProviderId);
        var authFilePath = WorkspaceRuntimePaths.GetCodexAuthFilePath(
            supportsContainerIsolation: true,
            WorkspacePathInPod);
        var authDirectory = Path.GetDirectoryName(authFilePath)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(authDirectory))
        {
            return;
        }

        var podName = GetPodName(userId);
        var ns = _options.KubernetesNamespace;
        var syncScript = string.IsNullOrWhiteSpace(sessionJson)
            ? $"rm -f {ShellEscaping.SingleQuote(authFilePath)}"
            : BuildCodexAuthWriteScript(authDirectory, authFilePath, sessionJson);

        await RunKubectlExecAsync(ns, podName, syncScript, cancellationToken);
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

    private async Task<WorkspaceStatus> WaitForPodReadyAsync(
        Guid userId,
        string podName,
        string ns,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(_options.PodStartupTimeoutSeconds);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var phase = await GetPodPhaseAsync(podName, ns, cancellationToken);

            if (phase == "Running")
            {
                _pods[userId] = new PodInfo
                {
                    PodName = podName,
                    StartedAt = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow
                };

                _logger.LogInformation("Pod {PodName} is ready for user {UserId}", podName, userId);

                return new WorkspaceStatus
                {
                    UserId = userId,
                    State = WorkspaceState.Running,
                    PodName = podName,
                    WorkspacePath = WorkspacePathInPod,
                    StartedAt = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow,
                    Message = "Pod ready"
                };
            }

            if (phase == "Failed")
            {
                return new WorkspaceStatus
                {
                    UserId = userId,
                    State = WorkspaceState.Failed,
                    PodName = podName,
                    Message = "Pod failed to start"
                };
            }

            await Task.Delay(1000, cancellationToken);
        }

        return new WorkspaceStatus
        {
            UserId = userId,
            State = WorkspaceState.Failed,
            PodName = podName,
            Message = $"Pod startup timed out after {timeout.TotalSeconds}s"
        };
    }

    private async Task EnsurePvcExistsAsync(
        Guid userId,
        string pvcName,
        string ns,
        CancellationToken cancellationToken)
    {
        // Check if PVC exists
        var checkResult = await RunKubectlAsync(
            $"get pvc {pvcName} -n {ns} --ignore-not-found -o name",
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(checkResult.Output))
        {
            return; // PVC exists
        }

        // Create PVC
        var pvcYaml = $@"
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: {pvcName}
  namespace: {ns}
  labels:
    app: {AppLabel}
    user-id: ""{userId:N}""
spec:
  accessModes:
    - ReadWriteOnce
  storageClassName: {_options.StorageClassName}
  resources:
    requests:
      storage: {_options.PvcSize}
";

        var result = await RunKubectlWithStdinAsync("apply -f -", pvcYaml, cancellationToken);
        if (result.Success)
        {
            _logger.LogInformation("Created PVC {PvcName} for user {UserId}", pvcName, userId);
        }
    }

    private string BuildPodManifest(
        Guid userId,
        string podName,
        string pvcName)
    {
        var imagePullSecrets = !string.IsNullOrEmpty(_options.ImagePullSecretName)
            ? $@"
  imagePullSecrets:
    - name: {_options.ImagePullSecretName}"
            : "";

        return $@"
apiVersion: v1
kind: Pod
metadata:
  name: {podName}
  namespace: {_options.KubernetesNamespace}
  labels:
    app: {AppLabel}
    user-id: ""{userId:N}""
spec:
  restartPolicy: Never{imagePullSecrets}
  securityContext:
    runAsNonRoot: true
    runAsUser: 1000
    runAsGroup: 1000
    fsGroup: 1000
    seccompProfile:
      type: RuntimeDefault
  containers:
    - name: agent
      image: {_options.ContainerImage}
      workingDir: {WorkspacePathInPod}
      resources:
        requests:
          cpu: {_options.CpuRequest}
          memory: {_options.MemoryRequest}
        limits:
          cpu: {_options.CpuLimit}
          memory: {_options.MemoryLimit}
      securityContext:
        allowPrivilegeEscalation: false
        readOnlyRootFilesystem: false
        capabilities:
          drop:
            - ALL
      volumeMounts:
        - name: workspace
          mountPath: {WorkspacePathInPod}
  volumes:
    - name: workspace
      persistentVolumeClaim:
        claimName: {pvcName}
";
    }

    private async Task<string?> GetPodPhaseAsync(string podName, string ns, CancellationToken cancellationToken)
    {
        var result = await RunKubectlAsync(
            $"get pod {podName} -n {ns} -o jsonpath='{{.status.phase}}' --ignore-not-found",
            cancellationToken);

        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            return null;
        }

        return result.Output.Trim().Trim('\'');
    }

    private async Task EnsureNamespaceExistsAsync()
    {
        var ns = _options.KubernetesNamespace;

        var result = await RunKubectlAsync($"get namespace {ns} --ignore-not-found -o name", CancellationToken.None);

        if (string.IsNullOrWhiteSpace(result.Output))
        {
            await RunKubectlAsync($"create namespace {ns}", CancellationToken.None);
            _logger.LogInformation("Created namespace: {Namespace}", ns);
        }
    }

    private async Task CleanupIdlePodsAsync(CancellationToken cancellationToken = default)
    {
        var idleTimeout = TimeSpan.FromMinutes(_options.SessionTimeoutMinutes);
        var cutoff = DateTime.UtcNow - idleTimeout;

        foreach (var (userId, info) in _pods)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (info.LastActivityAt < cutoff)
            {
                _logger.LogInformation(
                    "Stopping idle pod for user {UserId} (idle since {IdleSince})",
                    userId, info.LastActivityAt);

                await StopAsync(userId, cancellationToken);
            }
        }
    }

    private void UpdateLastActivity(Guid userId)
    {
        _pods.AddOrUpdate(
            userId,
            _ => new PodInfo
            {
                PodName = GetPodName(userId),
                StartedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            },
            (_, existing) =>
            {
                existing.LastActivityAt = DateTime.UtcNow;
                return existing;
            });
    }

    private static string GetPodName(Guid userId) => $"{PodPrefix}{userId:N}";
    private static string GetPvcName(Guid userId) => $"{PvcPrefix}{userId:N}";

    private static async Task<(bool Success, int ExitCode, string? Output, string? Error)> RunKubectlAsync(
        string arguments,
        CancellationToken cancellationToken,
        int timeoutSeconds = 60)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "kubectl",
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

            return (process.ExitCode == 0, process.ExitCode, await outputTask, await errorTask);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { }
            return (false, -1, null, "Command timed out");
        }
    }

    /// <summary>
    /// Execute a shell command inside a pod using ArgumentList to preserve argument boundaries.
    /// </summary>
    private static async Task<(bool Success, int ExitCode, string? Output, string? Error)> RunKubectlExecAsync(
        string ns,
        string podName,
        string shellCommand,
        CancellationToken cancellationToken,
        int timeoutSeconds = 60)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "kubectl",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        // Use ArgumentList to preserve each argument correctly
        process.StartInfo.ArgumentList.Add("exec");
        process.StartInfo.ArgumentList.Add("-n");
        process.StartInfo.ArgumentList.Add(ns);
        process.StartInfo.ArgumentList.Add(podName);
        process.StartInfo.ArgumentList.Add("--");
        process.StartInfo.ArgumentList.Add("/bin/sh");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(shellCommand); // Entire command as single argument

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            return (process.ExitCode == 0, process.ExitCode, await outputTask, await errorTask);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { }
            return (false, -1, null, "Command timed out");
        }
    }

    private static async Task<(bool Success, int ExitCode, string? Output, string? Error)> RunKubectlWithStdinAsync(
        string arguments,
        string stdin,
        CancellationToken cancellationToken,
        int timeoutSeconds = 60)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "kubectl",
                Arguments = arguments,
                RedirectStandardInput = true,
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

            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            return (process.ExitCode == 0, process.ExitCode, await outputTask, await errorTask);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { }
            return (false, -1, null, "Command timed out");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cleanupTimer.Dispose();
        _podLock.Dispose();
    }

    private sealed class PodInfo
    {
        public required string PodName { get; init; }
        public required DateTime StartedAt { get; init; }
        public DateTime LastActivityAt { get; set; }
    }
}
