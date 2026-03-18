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

        // Use remote execution for container-based workspaces
        if (_workspace.SupportsContainerIsolation)
        {
            return await ExecuteRemoteAsync(context.UserId, path, cancellationToken);
        }

        return await ExecuteLocalAsync(context.UserId, path, cancellationToken);
    }

    private async Task<string> ExecuteRemoteAsync(
        Guid userId,
        string path,
        CancellationToken cancellationToken)
    {
        var resolvedPath = _workspace.ResolvePath(userId, path);

        // Check if file exists and get its size
        // Use wc -c for size to avoid quoting issues with stat -c '%s'
        var statResult = await _workspace.ExecuteCommandAsync(
            userId,
            $"wc -c < {resolvedPath} 2>/dev/null",
            null,
            null,
            cancellationToken);

        if (statResult.ExitCode != 0 || string.IsNullOrWhiteSpace(statResult.StandardOutput))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"File not found: {path}"
            });
        }

        var size = long.TryParse(statResult.StandardOutput.Trim(), out var s) ? s : 0;

        // Read file content
        var catResult = await _workspace.ExecuteCommandAsync(
            userId,
            $"cat {resolvedPath}",
            null,
            null,
            cancellationToken);

        if (catResult.ExitCode != 0)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Failed to read file: {catResult.StandardError}"
            });
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            path,
            content = catResult.StandardOutput,
            size
        });
    }

    private async Task<string> ExecuteLocalAsync(
        Guid userId,
        string path,
        CancellationToken cancellationToken)
    {
        var resolvedPath = _workspace.ResolvePath(userId, path);

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
