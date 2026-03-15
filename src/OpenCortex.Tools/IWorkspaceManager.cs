namespace OpenCortex.Tools;

/// <summary>
/// Manages user workspace directories for tool execution.
/// Provides sandboxing and isolation for file operations.
/// </summary>
public interface IWorkspaceManager
{
    /// <summary>
    /// Get the workspace directory path for a user.
    /// Creates the directory if it doesn't exist.
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
}
