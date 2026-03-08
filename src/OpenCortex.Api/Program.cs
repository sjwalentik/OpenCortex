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

app.MapPost("/query", async (OqlQueryRequest request, CancellationToken cancellationToken) =>
{
    var executor = new OqlQueryExecutor(new PostgresDocumentQueryStore(connectionFactory, embeddingProvider));
    var result = await executor.ExecuteAsync(request.Oql, cancellationToken);
    return Results.Ok(result);
});

app.Run();

internal sealed record OqlQueryRequest(string Oql);
