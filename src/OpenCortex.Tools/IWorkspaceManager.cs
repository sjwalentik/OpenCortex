namespace OpenCortex.Tools;

/// <summary>
/// Manages user workspace directories for tool execution.
/// Provides sandboxing and isolation for file operations.
/// </summary>
public interface IWorkspaceManager
{
    /// <summary>
    /// Whether this manager supports container-based isolation.
    /// </summary>
    bool SupportsContainerIsolation { get; }

    /// <summary>
    /// Get the workspace directory path for a user.
    /// Creates the directory if it doesn't exist.
    /// For container-based managers, this returns the path inside the container.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <returns>Absolute path to the user's workspace.</returns>
    Task<string> GetWorkspacePathAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate that a path is within the user's workspace (sandbox check).
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="path">Path to validate (can be relative or absolute).</param>
    /// <returns>True if path is within the sandbox.</returns>
    bool IsPathAllowed(Guid userId, string path);

    /// <summary>
    /// Resolve a relative path to an absolute path within the workspace.
    /// Throws if the resolved path escapes the sandbox.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="relativePath">Relative path within workspace.</param>
    /// <returns>Absolute path.</returns>
    string ResolvePath(Guid userId, string relativePath);

    /// <summary>
    /// Clean up old/expired workspaces.
    /// </summary>
    Task CleanupExpiredWorkspacesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a user's workspace entirely.
    /// </summary>
    Task DeleteWorkspaceAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensure the workspace container/pod is running.
    /// For local manager, this is a no-op.
    /// For container managers, creates and starts the container if needed.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="credentials">
    /// Optional request-scoped credentials associated with the workspace session.
    /// Implementations may use these during startup, but should avoid persisting them.
    /// </param>
    /// <returns>Workspace status information.</returns>
    Task<WorkspaceStatus> EnsureRunningAsync(
        Guid userId,
        IReadOnlyDictionary<string, string>? credentials = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a command in the user's workspace.
    /// For local manager, executes directly.
    /// For container managers, executes via docker exec or kubectl exec.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="command">Command to execute.</param>
    /// <param name="workingDirectory">Working directory relative to workspace root.</param>
    /// <param name="environmentVariables">Optional environment variables to set for the command.</param>
    /// <param name="argumentList">Command arguments. Each entry is passed as a separate, shell-escaped argument.</param>
    /// <param name="standardInput">Optional text to write to the process stdin.</param>
    /// <returns>Command execution result.</returns>
    Task<CommandResult> ExecuteCommandAsync(
        Guid userId,
        string command,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        IReadOnlyList<string>? argumentList = null,
        string? standardInput = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current status of a user's workspace.
    /// </summary>
    Task<WorkspaceStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the workspace container/pod (but preserve storage).
    /// For local manager, this is a no-op.
    /// </summary>
    Task StopAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Status of a user's workspace.
/// </summary>
public sealed class WorkspaceStatus
{
    public required Guid UserId { get; init; }
    public required WorkspaceState State { get; init; }
    public string? ContainerId { get; init; }
    public string? PodName { get; init; }
    public string? WorkspacePath { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? LastActivityAt { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Workspace states.
/// </summary>
public enum WorkspaceState
{
    /// <summary>No container/pod exists for this user.</summary>
    NotExists,

    /// <summary>Container/pod is being created.</summary>
    Creating,

    /// <summary>Container/pod is starting up.</summary>
    Starting,

    /// <summary>Container/pod is running and ready.</summary>
    Running,

    /// <summary>Container/pod is stopping.</summary>
    Stopping,

    /// <summary>Container/pod is stopped (storage may persist).</summary>
    Stopped,

    /// <summary>Container/pod failed to start or crashed.</summary>
    Failed
}

/// <summary>
/// Result of executing a command in a workspace.
/// </summary>
public sealed class CommandResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required TimeSpan Duration { get; init; }

    public bool Success => ExitCode == 0;
}
