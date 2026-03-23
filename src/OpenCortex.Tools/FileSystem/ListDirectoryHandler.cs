using System.Text.RegularExpressions;
using System.Text.Json;

using OpenCortex.Tools;

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
        var quotedPath = ShellEscaping.SingleQuote(resolvedPath);

        // Use ls for simplicity, but still quote the path so file names cannot escape the shell.
        var lsFlags = recursive ? "-laR" : "-la";
        var result = await _workspace.ExecuteCommandAsync(
            userId,
            "/bin/sh",
            argumentList:
            [
                "-c",
                $"ls {lsFlags} -- {quotedPath} 2>/dev/null"
            ],
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Directory not found or not accessible: {path}"
            });
        }

        var entries = new List<object>();
        var currentRelativeDirectory = path == "." ? string.Empty : path;

        // Parse ls -la output: permissions links owner group size month day time name
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmedLine = line.Trim();

            if (recursive && trimmedLine.EndsWith(':'))
            {
                var headerPath = trimmedLine.TrimEnd(':');
                currentRelativeDirectory = ResolveRemoteRelativeDirectory(path, resolvedPath, headerPath);
                continue;
            }

            // Skip "total" line and . / .. entries
            if (trimmedLine.StartsWith("total ") || trimmedLine.EndsWith(" .") || trimmedLine.EndsWith(" .."))
                continue;

            // Split by whitespace, then join the tail back together in case the file name contains spaces.
            var parts = Regex.Split(trimmedLine, @"\s+");
            if (parts.Length < 9) continue;

            var permissions = parts[0];
            var size = long.TryParse(parts[4], out var s) ? s : 0;
            var name = string.Join(' ', parts.Skip(8));

            // Skip hidden files starting with .
            if (name.StartsWith('.')) continue;

            var isDirectory = permissions.StartsWith('d');
            var relativePath = string.IsNullOrEmpty(currentRelativeDirectory)
                ? name
                : $"{currentRelativeDirectory}/{name}";

            if (SensitivePathPolicy.IsSensitive(relativePath))
            {
                continue;
            }

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

    private static string ResolveRemoteRelativeDirectory(string requestedPath, string resolvedPath, string headerPath)
    {
        var normalizedHeader = headerPath.Replace('\\', '/').TrimEnd('/');
        var normalizedResolved = resolvedPath.Replace('\\', '/').TrimEnd('/');

        if (string.Equals(normalizedHeader, normalizedResolved, StringComparison.Ordinal))
        {
            return requestedPath == "." ? string.Empty : requestedPath;
        }

        if (normalizedHeader.StartsWith(normalizedResolved + "/", StringComparison.Ordinal))
        {
            var suffix = normalizedHeader[(normalizedResolved.Length + 1)..];
            if (string.IsNullOrEmpty(suffix))
            {
                return requestedPath == "." ? string.Empty : requestedPath;
            }

            return requestedPath == "."
                ? suffix
                : $"{requestedPath}/{suffix}";
        }

        return requestedPath == "." ? string.Empty : requestedPath;
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
            if (SensitivePathPolicy.IsSensitive(relativePath))
            {
                continue;
            }
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
            if (SensitivePathPolicy.IsSensitive(relativePath))
            {
                continue;
            }
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
