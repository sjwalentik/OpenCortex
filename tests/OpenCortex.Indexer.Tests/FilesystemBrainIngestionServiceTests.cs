using OpenCortex.Core.Brains;
using OpenCortex.Core.Embeddings;
using OpenCortex.Indexer.Indexing;

namespace OpenCortex.Indexer.Tests;

public sealed class FilesystemBrainIngestionServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "opencortex-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task Ingest_ProducesDocumentsChunksAndWikiEdges()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(
            Path.Combine(_rootPath, "plan.md"),
            """
            ---
            title: Product Plan
            type: roadmap
            ---
            # Product Plan
            See [[Architecture]].

            ## Tasks
            Build the first filesystem indexer.
            """);

        var brain = new BrainDefinition
        {
            BrainId = "sample-team",
            Name = "Sample Team",
            Slug = "sample-team",
            SourceRoots =
            [
                new SourceRootDefinition
                {
                    SourceRootId = "knowledge",
                    Path = _rootPath,
                },
            ],
        };

        var embeddingProvider = new DeterministicEmbeddingProvider(new EmbeddingOptions());
        var batch = await new FilesystemBrainIngestionService(embeddingProvider).IngestAsync(brain);

        var document = Assert.Single(batch.Documents);
        Assert.Equal("sample-team", batch.BrainId);
        Assert.Equal("Product Plan", document.Title);
        Assert.Equal("knowledge", document.SourceRootId);
        Assert.Equal("roadmap", document.DocumentType);
        Assert.Equal("plan.md", document.CanonicalPath);
        Assert.Equal(2, batch.Chunks.Count);
        Assert.Equal(2, batch.Embeddings.Count);
        Assert.All(batch.Embeddings, embedding => Assert.Equal(1536, embedding.Dimensions));

        var edge = Assert.Single(batch.LinkEdges);
        Assert.Equal("Architecture", edge.TargetRef);
        Assert.Equal("wiki", edge.LinkType);
        Assert.Null(edge.ToDocumentId);
    }

    [Fact]
    public async Task Ingest_ResolvesWikiLinksToKnownDocuments()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(
            Path.Combine(_rootPath, "plan.md"),
            """
            # Product Plan
            See [[Architecture]] and [[guides/architecture]].
            """);
        Directory.CreateDirectory(Path.Combine(_rootPath, "guides"));
        File.WriteAllText(
            Path.Combine(_rootPath, "guides", "architecture.md"),
            """
            ---
            title: Architecture
            ---
            # Architecture
            Runtime structure.
            """);

        var brain = new BrainDefinition
        {
            BrainId = "sample-team",
            Name = "Sample Team",
            Slug = "sample-team",
            SourceRoots =
            [
                new SourceRootDefinition
                {
                    SourceRootId = "knowledge",
                    Path = _rootPath,
                },
            ],
        };

        var embeddingProvider = new DeterministicEmbeddingProvider(new EmbeddingOptions());
        var batch = await new FilesystemBrainIngestionService(embeddingProvider).IngestAsync(brain);

        Assert.Equal(2, batch.Documents.Count);

        var architectureDoc = Assert.Single(batch.Documents.Where(document => document.Title == "Architecture"));
        var planDoc = Assert.Single(batch.Documents.Where(document => document.Title == "Product Plan"));
        var planEdges = batch.LinkEdges.Where(edge => edge.FromDocumentId == planDoc.DocumentId).ToArray();

        Assert.Equal(2, planEdges.Length);
        Assert.All(planEdges, edge => Assert.Equal(architectureDoc.DocumentId, edge.ToDocumentId));
    }

    [Fact]
    public async Task Ingest_LeavesUnknownWikiLinksUnresolved()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(
            Path.Combine(_rootPath, "plan.md"),
            """
            # Product Plan
            See [[Missing Doc]].
            """);

        var brain = new BrainDefinition
        {
            BrainId = "sample-team",
            Name = "Sample Team",
            Slug = "sample-team",
            SourceRoots =
            [
                new SourceRootDefinition
                {
                    SourceRootId = "knowledge",
                    Path = _rootPath,
                },
            ],
        };

        var embeddingProvider = new DeterministicEmbeddingProvider(new EmbeddingOptions());
        var batch = await new FilesystemBrainIngestionService(embeddingProvider).IngestAsync(brain);

        var edge = Assert.Single(batch.LinkEdges);
        Assert.Equal("Missing Doc", edge.TargetRef);
        Assert.Null(edge.ToDocumentId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
