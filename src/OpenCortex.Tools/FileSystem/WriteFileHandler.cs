using System.Text.Json;

namespace OpenCortex.Tools.FileSystem;

/// <summary>
/// Handler for the write_file tool.
/// </summary>
public sealed class WriteFileHandler : IToolHandler
{
    private readonly IWorkspaceManager _workspace;

    public WriteFileHandler(IWorkspaceManager workspace)
    {
        _workspace = workspace;
    }

    public string ToolName => "write_file";
    public string Category => "filesystem";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var path = arguments.GetProperty("path").GetString()
            ?? throw new ArgumentException("path is required");

        var content = arguments.GetProperty("content").GetString()
            ?? throw new ArgumentException("content is required");

        var resolvedPath = _workspace.ResolvePath(context.UserId, path);

        // Ensure parent directory exists
        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(resolvedPath, content, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            path,
            bytesWritten = content.Length
        });
    }
}
