using OpenCortex.Core.Brains;
using OpenCortex.Indexer.Indexing;

namespace OpenCortex.Indexer.Tests;

public sealed class FileSystemMarkdownDiscoveryTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "opencortex-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void DiscoverFiles_FindsMarkdownFilesAndNormalizesPaths()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "notes", "nested"));
        File.WriteAllText(Path.Combine(_rootPath, "notes", "alpha.md"), "# Alpha");
        File.WriteAllText(Path.Combine(_rootPath, "notes", "nested", "beta.md"), "# Beta");
        File.WriteAllText(Path.Combine(_rootPath, "notes", "nested", "ignore.txt"), "ignored");

        var brain = new BrainDefinition
        {
            BrainId = "sample",
            Name = "Sample",
            Slug = "sample",
            SourceRoots =
            [
                new SourceRootDefinition
                {
                    SourceRootId = "notes",
                    Path = Path.Combine(_rootPath, "notes"),
                },
            ],
        };

        var files = new FileSystemMarkdownDiscovery().DiscoverFiles(brain);

        Assert.Collection(
            files,
            file => Assert.Equal("alpha.md", file.CanonicalPath),
            file => Assert.Equal("nested/beta.md", file.CanonicalPath));
    }

    [Fact]
    public void DiscoverFiles_SkipsExcludedPaths()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "notes", "drafts"));
        File.WriteAllText(Path.Combine(_rootPath, "notes", "drafts", "idea.md"), "# Idea");

        var brain = new BrainDefinition
        {
            BrainId = "sample",
            Name = "Sample",
            Slug = "sample",
            SourceRoots =
            [
                new SourceRootDefinition
                {
                    SourceRootId = "notes",
                    Path = Path.Combine(_rootPath, "notes"),
                    ExcludePatterns = ["drafts/"],
                },
            ],
        };

        var files = new FileSystemMarkdownDiscovery().DiscoverFiles(brain);

        Assert.Empty(files);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
