using OpenCortex.Core.Brains;
using OpenCortex.Core.Embeddings;
using OpenCortex.Core.Persistence;
using OpenCortex.Indexer.Indexing;
using OpenCortex.Retrieval.Execution;

namespace OpenCortex.Integration.Tests;

/// <summary>
/// End-to-end integration tests covering the full pipeline:
/// filesystem discovery → Markdown parsing → embedding → ingestion batch
/// → in-memory retrieval → OQL query execution → ranked results with score breakdown.
///
/// No database is required. InMemoryDocumentQueryStore replays the ingested
/// batch data using the same keyword / semantic / graph scoring signals as the
/// Postgres implementation.
/// </summary>
public sealed class IndexingAndRetrievalIntegrationTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(), "opencortex-integration", Guid.NewGuid().ToString("n"));

    private readonly IEmbeddingProvider _embeddingProvider =
        new DeterministicEmbeddingProvider(new EmbeddingOptions());

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private BrainDefinition MakeBrain(string brainId = "test-brain") => new()
    {
        BrainId = brainId,
        Name = "Test Brain",
        Slug = brainId,
        Mode = BrainMode.Filesystem,
        SourceRoots =
        [
            new SourceRootDefinition
            {
                SourceRootId = "root-1",
                Path = _rootPath,
            },
        ],
    };

    private async Task<BrainIngestionBatch> IngestAsync(BrainDefinition brain)
    {
        var service = new FilesystemBrainIngestionService(_embeddingProvider);
        return await service.IngestAsync(brain);
    }

    private static OqlQueryExecutor MakeExecutor(BrainIngestionBatch batch) =>
        new(new InMemoryDocumentQueryStore(batch));

    // -------------------------------------------------------------------------
    // Ingestion shape tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Ingest_ThenQuery_ProducesNonEmptyBatch()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, "overview.md"), """
            ---
            title: Overview
            type: guide
            ---
            # Overview
            This document introduces the system.
            """);

        var batch = await IngestAsync(MakeBrain());

        Assert.Single(batch.Documents);
        Assert.NotEmpty(batch.Chunks);
        Assert.NotEmpty(batch.Embeddings);
        Assert.All(batch.Embeddings, e => Assert.Equal(1536, e.Dimensions));
    }

    [Fact]
    public async Task Ingest_MultipleDocuments_AllIndexed()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, "alpha.md"), "# Alpha\nFirst document.");
        File.WriteAllText(Path.Combine(_rootPath, "beta.md"), "# Beta\nSecond document.");
        File.WriteAllText(Path.Combine(_rootPath, "gamma.md"), "# Gamma\nThird document.");

        var batch = await IngestAsync(MakeBrain());

        Assert.Equal(3, batch.Documents.Count);
        Assert.Equal(3, batch.Chunks.Count);
        Assert.Equal(3, batch.Embeddings.Count);
    }

    // -------------------------------------------------------------------------
    // Keyword retrieval
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Query_KeywordRank_ReturnsDocumentsMatchingSearchText()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, "retention.md"), "# Retention Strategy\nHow we retain customers.");
        File.WriteAllText(Path.Combine(_rootPath, "pricing.md"), "# Pricing Model\nHow we charge customers.");

        var batch = await IngestAsync(MakeBrain());
        var executor = MakeExecutor(batch);

        var result = await executor.ExecuteAsync("""
            FROM brain("test-brain")
            SEARCH "retention"
            RANK keyword
            LIMIT 10
            """);

        Assert.NotEmpty(result.Results);
        var top = result.Results[0];
        Assert.Contains("retention", top.Title, StringComparison.OrdinalIgnoreCase);
        Assert.True(top.Breakdown.KeywordScore > 0, "Top keyword result should have a positive keyword score.");
        Assert.Equal(0.0, top.Breakdown.SemanticScore);
    }

    [Fact]
    public async Task Query_KeywordRank_TitleMatchScoresHigherThanContentMatch()
    {
        Directory.CreateDirectory(_rootPath);
        // "architecture" in the title → score 2.0
        File.WriteAllText(Path.Combine(_rootPath, "architecture.md"), "# Architecture\nSystem design overview.");
        // "architecture" only in the body → score 1.0
        File.WriteAllText(Path.Combine(_rootPath, "intro.md"), "# Introduction\nSee our architecture section.");

        var batch = await IngestAsync(MakeBrain());
        var executor = MakeExecutor(batch);

        var result = await executor.ExecuteAsync("""
            FROM brain("test-brain")
            SEARCH "architecture"
            RANK keyword
            LIMIT 10
            """);

        Assert.True(result.Results.Count >= 2);
        var titleMatch  = result.Results.First(r => r.Title.Contains("Architecture", StringComparison.OrdinalIgnoreCase));
        var contentMatch = result.Results.First(r => r.Title.Contains("Introduction", StringComparison.OrdinalIgnoreCase));
        Assert.True(titleMatch.Breakdown.KeywordScore > contentMatch.Breakdown.KeywordScore,
            "Title match should outrank content-only match.");
    }

    // -------------------------------------------------------------------------
    // Semantic retrieval
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Query_SemanticRank_PopulatesSemanticScoreComponent()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, "roadmap.md"), "# Roadmap\nPlanned milestones and deliverables.");

        var batch = await IngestAsync(MakeBrain());
        var executor = MakeExecutor(batch);

        var result = await executor.ExecuteAsync("""
            FROM brain("test-brain")
            SEARCH "milestones"
            RANK semantic
            LIMIT 5
            """);

        Assert.NotEmpty(result.Results);
        Assert.All(result.Results, r => Assert.Equal(0.0, r.Breakdown.KeywordScore));
        // At least one result should have a non-zero semantic score given the shared tokens.
        Assert.Contains(result.Results, r => r.Breakdown.SemanticScore > 0);
    }

    // -------------------------------------------------------------------------
    // Hybrid retrieval
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Query_HybridRank_CombinesKeywordAndSemanticSignals()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, "context.md"), """
            # Context Window
            Managing context windows for language models.
            """);

        var batch = await IngestAsync(MakeBrain());
        var executor = MakeExecutor(batch);

        var result = await executor.ExecuteAsync("""
            FROM brain("test-brain")
            SEARCH "context"
            RANK hybrid
            LIMIT 5
            """);

        Assert.NotEmpty(result.Results);
        var top = result.Results[0];
        // Hybrid should produce a keyword score (title contains "context")
        Assert.True(top.Breakdown.KeywordScore > 0);
        // Total score should be the sum of components
        var expectedTotal = top.Breakdown.KeywordScore + top.Breakdown.SemanticScore + top.Breakdown.GraphScore;
        Assert.Equal(expectedTotal, top.Score, precision: 6);
    }

    // -------------------------------------------------------------------------
    // Graph boost
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Query_DocumentWithInboundLinks_ReceivesGraphBoost()
    {
        Directory.CreateDirectory(_rootPath);
        // "hub" is linked from two other documents
        File.WriteAllText(Path.Combine(_rootPath, "hub.md"), "# Hub\nCentral knowledge node.");
        File.WriteAllText(Path.Combine(_rootPath, "doc-a.md"), "# Doc A\nSee [[Hub]] for details.");
        File.WriteAllText(Path.Combine(_rootPath, "doc-b.md"), "# Doc B\nAlso refer to [[Hub]].");

        var batch = await IngestAsync(MakeBrain());
        var executor = MakeExecutor(batch);

        var result = await executor.ExecuteAsync("""
            FROM brain("test-brain")
            RANK keyword
            LIMIT 10
            """);

        var hubResult = result.Results.FirstOrDefault(r => r.Title == "Hub");
        Assert.NotNull(hubResult);
        Assert.True(hubResult.Breakdown.GraphScore > 0,
            "Hub document linked from two others should have a positive graph boost.");
    }

    // -------------------------------------------------------------------------
    // Metadata filters
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Query_TypeFilter_ReturnsOnlyMatchingDocuments()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, "plan.md"), """
            ---
            type: roadmap
            ---
            # Plan
            Quarterly roadmap.
            """);
        File.WriteAllText(Path.Combine(_rootPath, "notes.md"), """
            ---
            type: notes
            ---
            # Notes
            Meeting notes.
            """);

        var batch = await IngestAsync(MakeBrain());
        var executor = MakeExecutor(batch);

        var result = await executor.ExecuteAsync("""
            FROM brain("test-brain")
            WHERE type = "roadmap"
            LIMIT 10
            """);

        Assert.All(result.Results, r =>
        {
            var doc = batch.Documents.Single(d => d.DocumentId == r.DocumentId);
            Assert.Equal("roadmap", doc.DocumentType);
        });
    }

    [Fact]
    public async Task Query_TagFilter_ReturnsOnlyTaggedDocuments()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, "tagged.md"), """
            ---
            tag: featured
            ---
            # Featured
            This is featured content.
            """);
        File.WriteAllText(Path.Combine(_rootPath, "plain.md"), "# Plain\nNo tag here.");

        var batch = await IngestAsync(MakeBrain());
        var executor = MakeExecutor(batch);

        var result = await executor.ExecuteAsync("""
            FROM brain("test-brain")
            WHERE tag = "featured"
            LIMIT 10
            """);

        Assert.Single(result.Results);
        Assert.Equal("Featured", result.Results[0].Title);
    }

    // -------------------------------------------------------------------------
    // Score breakdown and reason string
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Query_HybridRank_ReasonStringContainsActiveSignals()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, "guide.md"), "# Guide\nStep-by-step walkthrough.");

        var batch = await IngestAsync(MakeBrain());
        var executor = MakeExecutor(batch);

        var result = await executor.ExecuteAsync("""
            FROM brain("test-brain")
            SEARCH "guide"
            RANK hybrid
            LIMIT 5
            """);

        Assert.NotEmpty(result.Results);
        var top = result.Results[0];
        // Reason should mention the keyword signal since "Guide" matches the title.
        Assert.False(string.IsNullOrWhiteSpace(top.Reason));
        Assert.Contains("match", top.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Query_ExecutionSummary_ReflectsResultSignalCounts()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, "alpha.md"), "# Alpha\nContent about alpha.");
        File.WriteAllText(Path.Combine(_rootPath, "beta.md"), "# Beta\nContent about beta.");

        var batch = await IngestAsync(MakeBrain());
        var executor = MakeExecutor(batch);

        var result = await executor.ExecuteAsync("""
            FROM brain("test-brain")
            SEARCH "alpha"
            RANK keyword
            LIMIT 10
            """);

        Assert.True(result.Summary.TotalResults > 0);
        Assert.True(result.Summary.MaxScore >= result.Summary.MinScore);
        // At least one result should carry a keyword signal.
        Assert.True(result.Summary.ResultsWithKeywordSignal > 0);
    }

    // -------------------------------------------------------------------------
    // Limit and multi-brain isolation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Query_Limit_CapsTotalResults()
    {
        Directory.CreateDirectory(_rootPath);
        for (var i = 0; i < 10; i++)
        {
            File.WriteAllText(Path.Combine(_rootPath, $"doc-{i}.md"), $"# Doc {i}\nContent for document {i}.");
        }

        var batch = await IngestAsync(MakeBrain());
        var executor = MakeExecutor(batch);

        var result = await executor.ExecuteAsync("""
            FROM brain("test-brain")
            RANK keyword
            LIMIT 3
            """);

        Assert.True(result.Results.Count <= 3, "Result count should be capped by LIMIT.");
    }

    [Fact]
    public async Task Query_WrongBrainId_ReturnsNoResults()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, "doc.md"), "# Doc\nSome content.");

        var batch = await IngestAsync(MakeBrain("brain-a"));
        var executor = MakeExecutor(batch);

        var result = await executor.ExecuteAsync("""
            FROM brain("brain-b")
            SEARCH "content"
            RANK keyword
            LIMIT 10
            """);

        Assert.Empty(result.Results);
    }

    // -------------------------------------------------------------------------
    // Wiki-link resolution end to end
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Ingest_WikiLinks_ResolvedAndGraphBoostApplied()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, "target.md"), "# Target\nThe linked document.");
        File.WriteAllText(Path.Combine(_rootPath, "source.md"), "# Source\nSee [[Target]] for more.");

        var batch = await IngestAsync(MakeBrain());

        var targetDoc = batch.Documents.Single(d => d.Title == "Target");
        var edge = Assert.Single(batch.LinkEdges);
        Assert.Equal(targetDoc.DocumentId, edge.ToDocumentId);
        Assert.Equal("Target", edge.TargetRef);

        // Run a query and verify the target gets a graph boost.
        var executor = MakeExecutor(batch);
        var result = await executor.ExecuteAsync("""
            FROM brain("test-brain")
            RANK keyword
            LIMIT 10
            """);

        var targetResult = result.Results.Single(r => r.Title == "Target");
        Assert.True(targetResult.Breakdown.GraphScore > 0,
            "Target document linked from Source should receive a graph boost.");
    }

    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
            Directory.Delete(_rootPath, recursive: true);
    }
}
