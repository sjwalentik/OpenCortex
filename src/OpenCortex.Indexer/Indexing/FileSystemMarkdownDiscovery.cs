using OpenCortex.Core.Brains;

namespace OpenCortex.Indexer.Indexing;

public sealed class FileSystemMarkdownDiscovery
{
    public IReadOnlyList<DiscoveredMarkdownFile> DiscoverFiles(BrainDefinition brain)
    {
        ArgumentNullException.ThrowIfNull(brain);

        if (brain.Mode != BrainMode.Filesystem)
        {
            return [];
        }

        var files = new List<DiscoveredMarkdownFile>();

        foreach (var sourceRoot in brain.SourceRoots)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot.Path) || !Directory.Exists(sourceRoot.Path))
            {
                continue;
            }

            foreach (var absolutePath in Directory.EnumerateFiles(sourceRoot.Path, "*.md", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceRoot.Path, absolutePath);
                var canonicalPath = NormalizePath(relativePath);

                if (IsExcluded(canonicalPath, sourceRoot.ExcludePatterns))
                {
                    continue;
                }

                files.Add(new DiscoveredMarkdownFile(
                    sourceRoot.SourceRootId,
                    Path.GetFullPath(absolutePath),
                    canonicalPath,
                    File.GetLastWriteTimeUtc(absolutePath)));
            }
        }

        return files
            .OrderBy(file => file.CanonicalPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsExcluded(string canonicalPath, IReadOnlyList<string> excludePatterns)
    {
        foreach (var pattern in excludePatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            var trimmed = NormalizePath(pattern)
                .Replace("**/", string.Empty, StringComparison.Ordinal)
                .TrimStart('/');

            if (canonicalPath.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
