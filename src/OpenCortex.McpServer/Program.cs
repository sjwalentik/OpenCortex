using ModelContextProtocol.AspNetCore;
using OpenCortex.Core.Configuration;
using OpenCortex.Core.Embeddings;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Security;
using OpenCortex.Indexer.Indexing;
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

var validationErrors = new OpenCortexOptionsValidator().Validate(options).ToList();

// ---------------------------------------------------------------------------
// Infrastructure services
// ---------------------------------------------------------------------------

var connectionFactory = new PostgresConnectionFactory(new PostgresConnectionSettings
{
    ConnectionString = options.Database.ConnectionString,
});

if (!builder.Environment.IsEnvironment("Testing"))
{
    try
    {
        validationErrors.AddRange(await new PostgresEmbeddingSchemaValidator(connectionFactory)
            .ValidateAsync(options.Embeddings.Dimensions));
    }
    catch (Exception ex) when (ex is Npgsql.NpgsqlException or TimeoutException or InvalidOperationException)
    {
        validationErrors.Add($"Postgres schema validation failed: {ex.Message}");
    }
}

if (validationErrors.Count > 0)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, validationErrors));
}

var embeddingProvider = EmbeddingProviderFactory.Create(options.Embeddings);

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(options);

builder.Services.AddSingleton<IBrainCatalogStore>(_ =>
    new PostgresBrainCatalogStore(connectionFactory));

builder.Services.AddSingleton<OqlQueryExecutor>(_ =>
    new OqlQueryExecutor(new PostgresDocumentQueryStore(connectionFactory, embeddingProvider)));

builder.Services.AddSingleton<IApiTokenStore>(_ =>
    new PostgresApiTokenStore(connectionFactory));

builder.Services.AddSingleton<IManagedDocumentStore>(_ =>
    new PostgresManagedDocumentStore(connectionFactory));

builder.Services.AddSingleton<ISubscriptionStore>(_ =>
    new PostgresSubscriptionStore(connectionFactory));

builder.Services.AddSingleton<IUsageCounterStore>(_ =>
    new PostgresUsageCounterStore(connectionFactory));

builder.Services.AddSingleton<IManagedContentBrainIndexingService>(_ =>
    new ManagedContentBrainIndexingService(
        new PostgresManagedDocumentStore(connectionFactory),
        new PostgresDocumentCatalogStore(connectionFactory),
        new PostgresChunkStore(connectionFactory),
        new PostgresLinkGraphStore(connectionFactory),
        new PostgresIndexRunStore(connectionFactory),
        new PostgresEmbeddingStore(connectionFactory),
        embeddingProvider));

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

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (string.Equals(path, "/", StringComparison.Ordinal)
        || string.Equals(path, "/health", StringComparison.Ordinal))
    {
        await next();
        return;
    }

    var authorization = context.Request.Headers.Authorization.ToString();
    if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            type = "unauthorized",
            title = "API token required",
        });
        return;
    }

    var rawToken = authorization["Bearer ".Length..].Trim();
    if (!PersonalApiToken.IsValidFormat(rawToken))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            type = "unauthorized",
            title = "Invalid or expired token",
        });
        return;
    }

    var apiTokenStore = context.RequestServices.GetRequiredService<IApiTokenStore>();
    var token = await apiTokenStore.GetActiveTokenByHashAsync(
        PersonalApiToken.ComputeHash(rawToken),
        context.RequestAborted);

    if (token is null || token.ExpiresAt.HasValue && token.ExpiresAt.Value <= DateTimeOffset.UtcNow)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            type = "unauthorized",
            title = "Invalid or expired token",
        });
        return;
    }

    if (token.RevokedAt.HasValue)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            type = "unauthorized",
            title = "Token has been revoked",
        });
        return;
    }

    if (!token.Scopes.Contains("mcp:read", StringComparer.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            type = "forbidden",
            title = "Insufficient token scope",
            requiredScope = "mcp:read",
        });
        return;
    }

    await apiTokenStore.TouchLastUsedAsync(token.ApiTokenId, context.RequestAborted);
    context.SetMcpTokenContext(new McpTokenContext(
        token.ApiTokenId,
        token.UserId,
        token.CustomerId,
        token.Scopes,
        token.TokenPrefix));

    await next();
});

// ---------------------------------------------------------------------------
// MCP protocol routes
// ---------------------------------------------------------------------------

app.MapMcp("/mcp");

app.Run();
