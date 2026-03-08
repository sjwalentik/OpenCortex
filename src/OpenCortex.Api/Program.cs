using OpenCortex.Core.Configuration;
using OpenCortex.Core.Embeddings;
using OpenCortex.Indexer.Indexing;
using OpenCortex.Persistence.Postgres;
using OpenCortex.Retrieval.Execution;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection(OpenCortexOptions.SectionName).Get<OpenCortexOptions>() ?? new OpenCortexOptions();
var validationErrors = new OpenCortexOptionsValidator().Validate(options);
var connectionFactory = new PostgresConnectionFactory(new PostgresConnectionSettings
{
    ConnectionString = options.Database.ConnectionString,
});
var brainCatalogStore = new PostgresBrainCatalogStore(connectionFactory);
var embeddingProvider = EmbeddingProviderFactory.Create(options.Embeddings);
var indexRunStore = new PostgresIndexRunStore(connectionFactory);

var app = builder.Build();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (string.Equals(path, "/admin", StringComparison.Ordinal))
    {
        context.Response.Redirect("/admin/", permanent: false);
        return;
    }

    if (string.Equals(path, "/browse", StringComparison.Ordinal))
    {
        context.Response.Redirect("/browse/", permanent: false);
        return;
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/", () => Results.Ok(new
{
    service = "OpenCortex.Api",
    status = validationErrors.Count == 0 ? "ready" : "configuration-invalid",
}));

app.MapGet("/health", () => Results.Ok(new
{
    service = "OpenCortex.Api",
    validationErrors,
}));

app.MapGet("/brains", async (CancellationToken cancellationToken) =>
{
    await brainCatalogStore.UpsertBrainsAsync(options.Brains, cancellationToken);
    var brains = await brainCatalogStore.ListBrainsAsync(cancellationToken);
    return Results.Ok(brains);
});

app.MapGet("/admin/brains/health", async (CancellationToken cancellationToken) =>
{
    await brainCatalogStore.UpsertBrainsAsync(options.Brains, cancellationToken);
    var brains = await brainCatalogStore.ListBrainsAsync(cancellationToken);
    var recentRuns = await indexRunStore.ListIndexRunsAsync(limit: 200, cancellationToken: cancellationToken);

    var configuredBrainIds = options.Brains
        .Select(b => b.BrainId)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var summaries = brains.Select(brain =>
    {
        var brainRuns = recentRuns
            .Where(run => string.Equals(run.BrainId, brain.BrainId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(run => run.StartedAt)
            .ToList();
        var latestRun = brainRuns.FirstOrDefault();
        var failedRunCount = brainRuns.Count(run => string.Equals(run.Status, "failed", StringComparison.OrdinalIgnoreCase));
        var runningRunCount = brainRuns.Count(run => string.Equals(run.Status, "running", StringComparison.OrdinalIgnoreCase));
        var completedRunCount = brainRuns.Count(run => string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase));

        return new BrainHealthSummary(
            brain.BrainId,
            brain.Name,
            brain.Slug,
            brain.Mode,
            brain.Status,
            brain.SourceRootCount,
            configuredBrainIds.Contains(brain.BrainId),
            latestRun?.Status ?? "never-run",
            latestRun?.StartedAt,
            latestRun?.CompletedAt,
            latestRun?.DocumentsSeen,
            latestRun?.DocumentsIndexed,
            latestRun?.DocumentsFailed,
            latestRun is not null && string.Equals(latestRun.Status, "running", StringComparison.OrdinalIgnoreCase),
            failedRunCount,
            runningRunCount,
            completedRunCount,
            latestRun?.ErrorSummary);
    });

    return Results.Ok(summaries);
});

// Admin CRUD: brains

app.MapGet("/admin/brains/{brainId}", async (string brainId, CancellationToken cancellationToken) =>
{
    var brain = await brainCatalogStore.GetBrainAsync(brainId, cancellationToken);
    return brain is null
        ? Results.NotFound(new { message = $"Brain '{brainId}' was not found." })
        : Results.Ok(brain);
});

app.MapPost("/admin/brains", async (CreateBrainRequest request, CancellationToken cancellationToken) =>
{
    var definition = new OpenCortex.Core.Brains.BrainDefinition
    {
        BrainId = request.BrainId,
        Name = request.Name,
        Slug = request.Slug,
        Mode = Enum.TryParse<OpenCortex.Core.Brains.BrainMode>(request.Mode, ignoreCase: true, out var mode)
            ? mode
            : OpenCortex.Core.Brains.BrainMode.Filesystem,
        CustomerId = request.CustomerId,
        Status = request.Status ?? "active",
    };

    var brain = await brainCatalogStore.CreateBrainAsync(definition, cancellationToken);
    return Results.Created($"/admin/brains/{brain.BrainId}", brain);
});

app.MapPut("/admin/brains/{brainId}", async (string brainId, UpdateBrainRequest request, CancellationToken cancellationToken) =>
{
    var brain = await brainCatalogStore.UpdateBrainAsync(
        brainId,
        request.Name,
        request.Slug,
        request.Mode,
        request.Status,
        request.Description,
        cancellationToken);

    return brain is null
        ? Results.NotFound(new { message = $"Brain '{brainId}' was not found." })
        : Results.Ok(brain);
});

app.MapDelete("/admin/brains/{brainId}", async (string brainId, CancellationToken cancellationToken) =>
{
    var retired = await brainCatalogStore.RetireBrainAsync(brainId, cancellationToken);
    return retired
        ? Results.Ok(new { message = $"Brain '{brainId}' has been retired." })
        : Results.NotFound(new { message = $"Brain '{brainId}' was not found or is already retired." });
});

// Admin CRUD: source roots

app.MapPost("/admin/brains/{brainId}/source-roots", async (string brainId, AddSourceRootRequest request, CancellationToken cancellationToken) =>
{
    var brain = await brainCatalogStore.GetBrainAsync(brainId, cancellationToken);
    if (brain is null)
    {
        return Results.NotFound(new { message = $"Brain '{brainId}' was not found." });
    }

    var definition = new OpenCortex.Core.Brains.SourceRootDefinition
    {
        SourceRootId = request.SourceRootId,
        Path = request.Path,
        PathType = request.PathType ?? "local",
        IsWritable = request.IsWritable,
        IncludePatterns = request.IncludePatterns ?? ["**/*.md"],
        ExcludePatterns = request.ExcludePatterns ?? [],
        WatchMode = request.WatchMode ?? "scheduled",
    };

    var sourceRoot = await brainCatalogStore.AddSourceRootAsync(brainId, definition, cancellationToken);
    return Results.Created($"/admin/brains/{brainId}/source-roots/{sourceRoot.SourceRootId}", sourceRoot);
});

app.MapPut("/admin/brains/{brainId}/source-roots/{sourceRootId}", async (string brainId, string sourceRootId, UpdateSourceRootRequest request, CancellationToken cancellationToken) =>
{
    var sourceRoot = await brainCatalogStore.UpdateSourceRootAsync(
        brainId,
        sourceRootId,
        request.Path,
        request.PathType ?? "local",
        request.IsWritable,
        request.IncludePatterns ?? ["**/*.md"],
        request.ExcludePatterns ?? [],
        request.WatchMode ?? "scheduled",
        cancellationToken);

    return sourceRoot is null
        ? Results.NotFound(new { message = $"Source root '{sourceRootId}' was not found on brain '{brainId}'." })
        : Results.Ok(sourceRoot);
});

app.MapDelete("/admin/brains/{brainId}/source-roots/{sourceRootId}", async (string brainId, string sourceRootId, CancellationToken cancellationToken) =>
{
    var removed = await brainCatalogStore.RemoveSourceRootAsync(brainId, sourceRootId, cancellationToken);
    return removed
        ? Results.Ok(new { message = $"Source root '{sourceRootId}' has been removed from brain '{brainId}'." })
        : Results.NotFound(new { message = $"Source root '{sourceRootId}' was not found on brain '{brainId}'." });
});

app.MapGet("/indexing/plans", () => Results.Ok(new BrainIndexingPlanner().BuildPlans(options)));

app.MapGet("/indexing/preview/{brainId}", async (string brainId) =>
{
    var brain = options.Brains.FirstOrDefault(candidate => string.Equals(candidate.BrainId, brainId, StringComparison.OrdinalIgnoreCase));

    if (brain is null)
    {
        return Results.NotFound(new { message = $"Brain '{brainId}' was not found." });
    }

    var batch = await new FilesystemBrainIngestionService(embeddingProvider).IngestAsync(brain);

    return Results.Ok(new
    {
        batch.BrainId,
        documentCount = batch.Documents.Count,
        chunkCount = batch.Chunks.Count,
        linkEdgeCount = batch.LinkEdges.Count,
        documents = batch.Documents.Select(document => new
        {
            document.DocumentId,
            document.CanonicalPath,
            document.Title,
            document.DocumentType,
        }),
    });
});

app.MapPost("/indexing/run/{brainId}", async (string brainId, CancellationToken cancellationToken) =>
{
    var brain = options.Brains.FirstOrDefault(candidate => string.Equals(candidate.BrainId, brainId, StringComparison.OrdinalIgnoreCase));

    if (brain is null)
    {
        return Results.NotFound(new { message = $"Brain '{brainId}' was not found." });
    }

    var coordinator = new BrainIngestionPersistenceCoordinator(
        new PostgresDocumentCatalogStore(connectionFactory),
        new PostgresChunkStore(connectionFactory),
        new PostgresLinkGraphStore(connectionFactory),
        indexRunStore,
        new PostgresEmbeddingStore(connectionFactory));
    await brainCatalogStore.UpsertBrainsAsync(options.Brains, cancellationToken);
    var batch = await new FilesystemBrainIngestionService(embeddingProvider).IngestAsync(brain, cancellationToken);
    var indexRun = await coordinator.PersistAsync(batch, "manual-api", cancellationToken);

    return Results.Ok(new
    {
        indexRun.IndexRunId,
        indexRun.Status,
        batch.BrainId,
        documentCount = batch.Documents.Count,
        chunkCount = batch.Chunks.Count,
        linkEdgeCount = batch.LinkEdges.Count,
    });
});

app.MapGet("/indexing/runs", async (string? brainId, int? limit, CancellationToken cancellationToken) =>
{
    var runs = await indexRunStore.ListIndexRunsAsync(brainId, limit ?? 20, cancellationToken);
    return Results.Ok(runs);
});

app.MapGet("/indexing/runs/{indexRunId}", async (string indexRunId, CancellationToken cancellationToken) =>
{
    var run = await indexRunStore.GetIndexRunAsync(indexRunId, cancellationToken);

    return run is null
        ? Results.NotFound(new { message = $"Index run '{indexRunId}' was not found." })
        : Results.Ok(run);
});

app.MapGet("/indexing/runs/{indexRunId}/errors", async (string indexRunId, CancellationToken cancellationToken) =>
{
    var run = await indexRunStore.GetIndexRunAsync(indexRunId, cancellationToken);

    if (run is null)
    {
        return Results.NotFound(new { message = $"Index run '{indexRunId}' was not found." });
    }

    var errors = await indexRunStore.ListIndexRunErrorsAsync(indexRunId, cancellationToken);
    return Results.Ok(errors);
});

app.MapPost("/query", async (OqlQueryRequest request, CancellationToken cancellationToken) =>
{
    var executor = new OqlQueryExecutor(new PostgresDocumentQueryStore(connectionFactory, embeddingProvider));
    var result = await executor.ExecuteAsync(request.Oql, cancellationToken);
    return Results.Ok(result);
});

// ---------------------------------------------------------------------------
// Browse API — document listing for authoring surface
// ---------------------------------------------------------------------------

app.MapGet("/browse/brains/{brainId}/documents", async (
    string brainId,
    string? sourceRootId,
    string? pathPrefix,
    int? limit,
    CancellationToken cancellationToken) =>
{
    var documentCatalogStore = new PostgresDocumentCatalogStore(connectionFactory);
    var documents = await documentCatalogStore.ListDocumentsAsync(
        brainId,
        sourceRootId,
        pathPrefix,
        limit ?? 200,
        cancellationToken);
    return Results.Ok(new { brainId, count = documents.Count, documents });
});

app.Run();

internal sealed record OqlQueryRequest(string Oql);

internal sealed record CreateBrainRequest(
    string BrainId,
    string Name,
    string Slug,
    string Mode,
    string? CustomerId,
    string? Status);

internal sealed record UpdateBrainRequest(
    string Name,
    string Slug,
    string Mode,
    string Status,
    string? Description);

internal sealed record AddSourceRootRequest(
    string SourceRootId,
    string Path,
    string? PathType,
    bool IsWritable,
    string[]? IncludePatterns,
    string[]? ExcludePatterns,
    string? WatchMode);

internal sealed record UpdateSourceRootRequest(
    string Path,
    string? PathType,
    bool IsWritable,
    string[]? IncludePatterns,
    string[]? ExcludePatterns,
    string? WatchMode);

internal sealed record BrainHealthSummary(
    string BrainId,
    string Name,
    string Slug,
    string Mode,
    string Status,
    int SourceRootCount,
    bool IsConfigured,
    string LatestRunStatus,
    DateTimeOffset? LatestRunStartedAt,
    DateTimeOffset? LatestRunCompletedAt,
    int? LatestDocumentsSeen,
    int? LatestDocumentsIndexed,
    int? LatestDocumentsFailed,
    bool IsLatestRunActive,
    int FailedRunCount,
    int RunningRunCount,
    int CompletedRunCount,
    string? LatestErrorSummary);
