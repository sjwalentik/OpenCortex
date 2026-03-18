using System.Text.Json;

namespace OpenCortex.Tools.FileSystem;

/// <summary>
/// Handler for the list_directory tool.
/// </summary>
public sealed class ListDirectoryHandler : IToolHandler
{
    private readonly IWorkspaceManager _workspace;

    public ListDirectoryHandler(IWorkspaceManager workspace)
    {
        _workspace = workspace;
    }

    public string ToolName => "list_directory";
    public string Category => "filesystem";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var path = arguments.TryGetProperty("path", out var pathElement)
            ? pathElement.GetString() ?? "."
            : ".";

        var recursive = arguments.TryGetProperty("recursive", out var recursiveElement)
            && recursiveElement.GetBoolean();

        // Use remote execution for container-based workspaces
        if (_workspace.SupportsContainerIsolation)
        {
            return await ExecuteRemoteAsync(context.UserId, path, recursive, cancellationToken);
        }

        return await ExecuteLocalAsync(context.UserId, path, recursive, cancellationToken);
    }

    private async Task<string> ExecuteRemoteAsync(
        Guid userId,
        string path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        var resolvedPath = _workspace.ResolvePath(userId, path);
        var depthArg = recursive ? "" : "-maxdepth 1";

        // Use find command to list directory contents with file info
        // ExecuteCommandAsync wraps in sh -c, so pass the pipeline directly
        var command = $"find {resolvedPath} {depthArg} -printf '%y|%p|%s|%T@\\n' 2>/dev/null | sort";

        var result = await _workspace.ExecuteCommandAsync(
            userId,
            command,
            null,
            null,
            cancellationToken);

        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            // Directory might not exist
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Directory not found or not accessible: {path}"
            });
        }

        var entries = new List<object>();
        var workspacePath = await _workspace.GetWorkspacePathAsync(userId, cancellationToken);

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length < 4) continue;

            var type = parts[0];
            var fullPath = parts[1];
            var size = long.TryParse(parts[2], out var s) ? s : 0;
            var modified = double.TryParse(parts[3], out var m)
                ? DateTimeOffset.FromUnixTimeSeconds((long)m).UtcDateTime
                : DateTime.UtcNow;

            // Skip the root directory itself
            if (fullPath == resolvedPath) continue;

            var relativePath = fullPath.StartsWith(workspacePath)
                ? fullPath[(workspacePath.Length + 1)..]
                : fullPath;

            if (type == "d")
            {
                entries.Add(new
                {
                    type = "directory",
                    path = relativePath,
                    name = Path.GetFileName(fullPath)
                });
            }
            else if (type == "f")
            {
                entries.Add(new
                {
                    type = "file",
                    path = relativePath,
                    name = Path.GetFileName(fullPath),
                    size,
                    modified
                });
            }
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            path,
            entries
        });
    }

    private async Task<string> ExecuteLocalAsync(
        Guid userId,
        string path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        var workspacePath = await _workspace.GetWorkspacePathAsync(userId, cancellationToken);
        var resolvedPath = _workspace.ResolvePath(userId, path);

        if (!Directory.Exists(resolvedPath))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Directory not found: {path}"
            });
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var entries = new List<object>();

        foreach (var dir in Directory.EnumerateDirectories(resolvedPath, "*", searchOption))
        {
            var relativePath = Path.GetRelativePath(workspacePath, dir).Replace('\\', '/');
            entries.Add(new
            {
                type = "directory",
                path = relativePath,
                name = Path.GetFileName(dir)
            });
        }

        foreach (var file in Directory.EnumerateFiles(resolvedPath, "*", searchOption))
        {
            var fileInfo = new FileInfo(file);
            var relativePath = Path.GetRelativePath(workspacePath, file).Replace('\\', '/');
            entries.Add(new
            {
                type = "file",
                path = relativePath,
                name = Path.GetFileName(file),
                size = fileInfo.Length,
                modified = fileInfo.LastWriteTimeUtc
            });
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            path,
            entries
        });
    }
}
