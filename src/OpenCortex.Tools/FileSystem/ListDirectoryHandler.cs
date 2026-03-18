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

        // Use ls for simplicity - avoids shell quoting issues with find -printf
        var lsFlags = recursive ? "-laR" : "-la";
        var result = await _workspace.ExecuteCommandAsync(
            userId,
            $"ls {lsFlags} {resolvedPath} 2>/dev/null",
            null,
            null,
            cancellationToken);

        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Directory not found or not accessible: {path}"
            });
        }

        var entries = new List<object>();

        // Parse ls -la output: permissions links owner group size month day time name
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Skip "total" line and . / .. entries
            if (line.StartsWith("total ") || line.EndsWith(" .") || line.EndsWith(" .."))
                continue;

            // Split by whitespace, but name might have spaces so limit to 9 parts
            var parts = line.Split(' ', 9, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9) continue;

            var permissions = parts[0];
            var size = long.TryParse(parts[4], out var s) ? s : 0;
            var name = parts[8];

            // Skip hidden files starting with .
            if (name.StartsWith('.')) continue;

            var isDirectory = permissions.StartsWith('d');
            var relativePath = path == "." ? name : $"{path}/{name}";

            if (isDirectory)
            {
                entries.Add(new
                {
                    type = "directory",
                    path = relativePath,
                    name
                });
            }
            else
            {
                entries.Add(new
                {
                    type = "file",
                    path = relativePath,
                    name,
                    size
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
