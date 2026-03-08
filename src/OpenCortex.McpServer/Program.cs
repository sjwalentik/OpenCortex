using OpenCortex.Core.Configuration;
using OpenCortex.Core.Embeddings;
using OpenCortex.Persistence.Postgres;
using OpenCortex.Retrieval.Execution;
using OpenCortex.Retrieval.Planning;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection(OpenCortexOptions.SectionName).Get<OpenCortexOptions>() ?? new OpenCortexOptions();
var validationErrors = new OpenCortexOptionsValidator().Validate(options);
var planner = new OqlRetrievalPlanner();
var connectionFactory = new PostgresConnectionFactory(new PostgresConnectionSettings
{
    ConnectionString = options.Database.ConnectionString,
});
var brainCatalogStore = new PostgresBrainCatalogStore(connectionFactory);
var embeddingProvider = EmbeddingProviderFactory.Create(options.Embeddings);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "OpenCortex.McpServer",
    protocol = "mcp",
    status = validationErrors.Count == 0 ? "ready" : "configuration-invalid",
}));

app.MapGet("/health", () => Results.Ok(new
{
    service = "OpenCortex.McpServer",
    validationErrors,
}));

app.MapGet("/tools", () => Results.Ok(new[]
{
    "list_brains",
    "query_brain",
    "get_document",
    "get_related_documents",
    "build_context_pack",
}));

app.MapGet("/brains", async (CancellationToken cancellationToken) =>
{
    await brainCatalogStore.UpsertBrainsAsync(options.Brains, cancellationToken);
    var brains = await brainCatalogStore.ListBrainsAsync(cancellationToken);
    return Results.Ok(brains);
});

app.MapPost("/query/plan", (OqlQueryRequest request) =>
{
    var plan = planner.BuildPlan(request.Oql);
    return Results.Ok(plan);
});

app.MapPost("/query", async (OqlQueryRequest request, CancellationToken cancellationToken) =>
{
    await brainCatalogStore.UpsertBrainsAsync(options.Brains, cancellationToken);
    var executor = new OqlQueryExecutor(new PostgresDocumentQueryStore(connectionFactory, embeddingProvider));
    var result = await executor.ExecuteAsync(request.Oql, cancellationToken);
    return Results.Ok(result);
});

app.Run();

internal sealed record OqlQueryRequest(string Oql);
