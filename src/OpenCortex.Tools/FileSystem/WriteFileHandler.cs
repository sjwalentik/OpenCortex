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

        if (SensitivePathPolicy.IsSensitive(path))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Access denied for sensitive path: {path}"
            });
        }

        // Use remote execution for container-based workspaces
        if (_workspace.SupportsContainerIsolation)
        {
            return await ExecuteRemoteAsync(context.UserId, path, content, context.Credentials, cancellationToken);
        }

        return await ExecuteLocalAsync(context.UserId, path, content, cancellationToken);
    }

    private async Task<string> ExecuteRemoteAsync(
        Guid userId,
        string path,
        string content,
        IReadOnlyDictionary<string, string>? credentials,
        CancellationToken cancellationToken)
    {
        var resolvedPath = _workspace.ResolvePath(userId, path);

        if (SensitivePathPolicy.IsSensitive(resolvedPath))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Access denied for sensitive path: {path}"
            });
        }

        var directory = Path.GetDirectoryName(resolvedPath)?.Replace('\\', '/');
        var quotedPath = ShellEscaping.SingleQuote(resolvedPath);

        // Ensure parent directory exists
        if (!string.IsNullOrEmpty(directory))
        {
            var quotedDirectory = ShellEscaping.SingleQuote(directory);
            await _workspace.ExecuteCommandAsync(
                userId,
                $"mkdir -p -- {quotedDirectory}",
                null,
                null,
                cancellationToken);
        }

        // Base64 encode content to safely pass through shell
        var base64Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));

        // Use printf and tee to avoid shell interpretation of the file path.
        var writeResult = await _workspace.ExecuteCommandAsync(
            userId,
            $"printf '%s' '{base64Content}' | base64 -d | tee -- {quotedPath} > /dev/null",
            null,
            null,
            cancellationToken);

        if (writeResult.ExitCode != 0)
        {
            var safeError = SensitiveDataRedactor.Redact(writeResult.StandardError, credentials)
                ?? "Command failed.";
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Failed to write file: {safeError}"
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

        if (SensitivePathPolicy.IsSensitive(resolvedPath))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Access denied for sensitive path: {path}"
            });
        }

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
