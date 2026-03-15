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

        var workspacePath = await _workspace.GetWorkspacePathAsync(context.UserId, cancellationToken);
        var resolvedPath = _workspace.ResolvePath(context.UserId, path);

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
