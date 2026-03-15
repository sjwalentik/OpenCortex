using System.Text.Json;

namespace OpenCortex.Tools.FileSystem;

/// <summary>
/// Handler for the read_file tool.
/// </summary>
public sealed class ReadFileHandler : IToolHandler
{
    private readonly IWorkspaceManager _workspace;

    public ReadFileHandler(IWorkspaceManager workspace)
    {
        _workspace = workspace;
    }

    public string ToolName => "read_file";
    public string Category => "filesystem";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var path = arguments.GetProperty("path").GetString()
            ?? throw new ArgumentException("path is required");

        var resolvedPath = _workspace.ResolvePath(context.UserId, path);

        if (!File.Exists(resolvedPath))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"File not found: {path}"
            });
        }

        var content = await File.ReadAllTextAsync(resolvedPath, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            path,
            content,
            size = new FileInfo(resolvedPath).Length
        });
    }
}
