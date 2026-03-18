using System.Text;
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

        // Use remote execution for container-based workspaces
        if (_workspace.SupportsContainerIsolation)
        {
            return await ExecuteRemoteAsync(context.UserId, path, content, cancellationToken);
        }

        return await ExecuteLocalAsync(context.UserId, path, content, cancellationToken);
    }

    private async Task<string> ExecuteRemoteAsync(
        Guid userId,
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var resolvedPath = _workspace.ResolvePath(userId, path);
        var directory = Path.GetDirectoryName(resolvedPath)?.Replace('\\', '/');

        // Ensure parent directory exists
        if (!string.IsNullOrEmpty(directory))
        {
            await _workspace.ExecuteCommandAsync(
                userId,
                "/bin/sh",
                $"-c \"mkdir -p '{directory}'\"",
                null,
                cancellationToken);
        }

        // Base64 encode content to safely pass through shell
        var base64Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));

        // Write file using base64 decode to handle special characters
        var writeResult = await _workspace.ExecuteCommandAsync(
            userId,
            "/bin/sh",
            $"-c \"echo '{base64Content}' | base64 -d > '{resolvedPath}'\"",
            null,
            cancellationToken);

        if (writeResult.ExitCode != 0)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Failed to write file: {writeResult.StandardError}"
            });
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            path,
            bytesWritten = content.Length
        });
    }

    private async Task<string> ExecuteLocalAsync(
        Guid userId,
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var resolvedPath = _workspace.ResolvePath(userId, path);

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
