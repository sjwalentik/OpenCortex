using ModelContextProtocol.AspNetCore;
using OpenCortex.Core.Configuration;
using OpenCortex.Core.Embeddings;
using OpenCortex.Core.Persistence;
using OpenCortex.McpServer;
using OpenCortex.Persistence.Postgres;
using OpenCortex.Retrieval.Execution;
using OpenCortex.Retrieval.Planning;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------

var options = builder.Configuration
    .GetSection(OpenCortexOptions.SectionName)
    .Get<OpenCortexOptions>() ?? new OpenCortexOptions();

var validationErrors = new OpenCortexOptionsValidator().Validate(options);

// ---------------------------------------------------------------------------
// Infrastructure services
// ---------------------------------------------------------------------------

var connectionFactory = new PostgresConnectionFactory(new PostgresConnectionSettings
{
    ConnectionString = options.Database.ConnectionString,
});

var embeddingProvider = EmbeddingProviderFactory.Create(options.Embeddings);

builder.Services.AddSingleton<IBrainCatalogStore>(_ =>
    new PostgresBrainCatalogStore(connectionFactory));

builder.Services.AddSingleton<OqlQueryExecutor>(_ =>
    new OqlQueryExecutor(new PostgresDocumentQueryStore(connectionFactory, embeddingProvider)));

// ---------------------------------------------------------------------------
// MCP server — registers OpenCortexTools via McpServerToolType attribute
// ---------------------------------------------------------------------------

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

// ---------------------------------------------------------------------------
// Diagnostic HTTP endpoints (not part of MCP protocol)
// ---------------------------------------------------------------------------

app.MapGet("/", () => Results.Ok(new
{
    service = "OpenCortex.McpServer",
    protocol = "mcp/1.1",
    status = validationErrors.Count == 0 ? "ready" : "configuration-invalid",
    transport = "http+sse",
}));

app.MapGet("/health", () => Results.Ok(new
{
    service = "OpenCortex.McpServer",
    validationErrors,
}));

// ---------------------------------------------------------------------------
// MCP protocol routes
// ---------------------------------------------------------------------------

app.MapMcp();

app.Run();
