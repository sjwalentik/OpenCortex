using OpenCortex.Core.Configuration;
using OpenCortex.Core.Embeddings;
using OpenCortex.Indexer.Indexing;
using OpenCortex.Persistence.Postgres;

namespace OpenCortex.Workers;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _configuration.GetSection(OpenCortexOptions.SectionName).Get<OpenCortexOptions>() ?? new OpenCortexOptions();
        var validationErrors = new OpenCortexOptionsValidator().Validate(options);

        if (validationErrors.Count > 0)
        {
            foreach (var error in validationErrors)
            {
                _logger.LogError("Configuration error: {Error}", error);
            }

            return;
        }

        var plans = new BrainIndexingPlanner().BuildPlans(options);
        var connectionFactory = new PostgresConnectionFactory(new PostgresConnectionSettings
        {
            ConnectionString = options.Database.ConnectionString,
        });
        var brainCatalogStore = new PostgresBrainCatalogStore(connectionFactory);
        var embeddingProvider = EmbeddingProviderFactory.Create(options.Embeddings);
        var coordinator = new BrainIngestionPersistenceCoordinator(
            new PostgresDocumentCatalogStore(connectionFactory),
            new PostgresChunkStore(connectionFactory),
            new PostgresLinkGraphStore(connectionFactory),
            new PostgresIndexRunStore(connectionFactory),
            new PostgresEmbeddingStore(connectionFactory));
        var ingestionService = new FilesystemBrainIngestionService(embeddingProvider);

        await brainCatalogStore.UpsertBrainsAsync(options.Brains, stoppingToken);

        foreach (var plan in plans)
        {
            _logger.LogInformation(
                "Brain '{BrainId}' queued for {Mode} indexing with {SourceRootCount} root(s) on schedule {Schedule}",
                plan.BrainId,
                plan.Mode,
                plan.SourceRootCount,
                plan.Schedule);
        }

        await RunIndexingCycleAsync(options, brainCatalogStore, ingestionService, coordinator, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            await RunIndexingCycleAsync(options, brainCatalogStore, ingestionService, coordinator, stoppingToken);
        }
    }

    private async Task RunIndexingCycleAsync(
        OpenCortexOptions options,
        PostgresBrainCatalogStore brainCatalogStore,
        FilesystemBrainIngestionService ingestionService,
        BrainIngestionPersistenceCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        await brainCatalogStore.UpsertBrainsAsync(options.Brains, cancellationToken);

        foreach (var brain in options.Brains.Where(brain => brain.Mode == OpenCortex.Core.Brains.BrainMode.Filesystem))
        {
            try
            {
                var batch = await ingestionService.IngestAsync(brain, cancellationToken);
                var indexRun = await coordinator.PersistAsync(batch, "scheduled", cancellationToken);

                _logger.LogInformation(
                    "Indexed brain '{BrainId}' with {DocumentCount} document(s), {ChunkCount} chunk(s), and {EdgeCount} link edge(s). Run {IndexRunId} finished with status {Status}.",
                    brain.BrainId,
                    batch.Documents.Count,
                    batch.Chunks.Count,
                    batch.LinkEdges.Count,
                    indexRun.IndexRunId,
                    indexRun.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to index brain '{BrainId}'.", brain.BrainId);
            }
        }
    }
}
