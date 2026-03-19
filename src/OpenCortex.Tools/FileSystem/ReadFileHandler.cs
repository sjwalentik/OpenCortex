using System.Text;
using System.Text.Json;

using OpenCortex.Tools;

namespace OpenCortex.Tools.FileSystem;

/// <summary>
/// Handler for the read_file tool.
/// </summary>
public sealed class ReadFileHandler : IToolHandler
{
    private const int MaxReadBytes = 256 * 1024;
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
            return await ExecuteRemoteAsync(context.UserId, path, context.Credentials, cancellationToken);
        }

        return await ExecuteLocalAsync(context.UserId, path, cancellationToken);
    }

    private async Task<string> ExecuteRemoteAsync(
        Guid userId,
        string path,
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

        var quotedPath = ShellEscaping.SingleQuote(resolvedPath);

        // Check if file exists and get its size
        var statResult = await _workspace.ExecuteCommandAsync(
            userId,
            $"wc -c < {quotedPath} 2>/dev/null",
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
        var truncated = size > MaxReadBytes;

        // Read at most MaxReadBytes so tools do not dump arbitrarily large files into chat.
        var catResult = await _workspace.ExecuteCommandAsync(
            userId,
            $"head -c {MaxReadBytes} -- {quotedPath}",
            null,
            null,
            cancellationToken);

        if (catResult.ExitCode != 0)
        {
            var safeError = SensitiveDataRedactor.Redact(catResult.StandardError, credentials)
                ?? "Command failed.";
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Failed to read file: {safeError}"
            });
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            path,
            content = catResult.StandardOutput,
            size,
            truncated,
            maxBytes = MaxReadBytes
        });
    }

    private async Task<string> ExecuteLocalAsync(
        Guid userId,
        string path,
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

        if (!File.Exists(resolvedPath))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"File not found: {path}"
            });
        }

        var fileInfo = new FileInfo(resolvedPath);
        var size = fileInfo.Length;
        var truncated = size > MaxReadBytes;

        string content;
        if (truncated)
        {
            using var stream = File.OpenRead(resolvedPath);
            var buffer = new byte[MaxReadBytes];
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            content = Encoding.UTF8.GetString(buffer, 0, read);
        }
        else
        {
            content = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            path,
            content,
            size,
            truncated,
            maxBytes = MaxReadBytes
        });
    }
}
