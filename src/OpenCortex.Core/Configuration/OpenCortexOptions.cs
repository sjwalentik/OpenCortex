using OpenCortex.Core.Brains;
using OpenCortex.Core.Embeddings;
using OpenCortex.Core.Tenancy;

namespace OpenCortex.Core.Configuration;

public sealed class OpenCortexOptions
{
    public const string SectionName = "OpenCortex";

    public DatabaseOptions Database { get; init; } = new();

    public IndexingOptions Indexing { get; init; } = new();

    public EmbeddingOptions Embeddings { get; init; } = new();

    public HostedAuthOptions HostedAuth { get; init; } = new();

    public BillingOptions Billing { get; init; } = new();

    public IReadOnlyList<BrainDefinition> Brains { get; init; } = [];
}

public sealed class DatabaseOptions
{
    public string ConnectionString { get; init; } = string.Empty;

    public string VectorProvider { get; init; } = "pgvector";
}

public sealed class IndexingOptions
{
    public string DefaultSchedule { get; init; } = "0 */15 * * * *";

    public bool EnableFileWatching { get; init; }
}

public sealed class BillingOptions
{
    public StripeBillingOptions Stripe { get; init; } = new();

    public Dictionary<string, PlanEntitlements> Plans { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["free"] = new()
        {
            MaxDocuments = 10,
            MaxBrains = 1,
            McpQueriesPerMonth = 100,
            McpWrite = false,
        },
        ["pro"] = new()
        {
            MaxDocuments = 500,
            MaxBrains = 3,
            McpQueriesPerMonth = -1,
            McpWrite = true,
        },
        ["teams"] = new()
        {
            MaxDocuments = 2000,
            MaxBrains = 10,
            McpQueriesPerMonth = -1,
            McpWrite = true,
        },
    };
}

public sealed class StripeBillingOptions
{
    public bool Enabled { get; init; }

    public string SecretKey { get; init; } = string.Empty;

    public string WebhookSecret { get; init; } = string.Empty;

    public string AppBaseUrl { get; init; } = string.Empty;

    public Dictionary<string, string> PriceIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PlanEntitlements
{
    public int MaxDocuments { get; init; }

    public int MaxBrains { get; init; }

    public int McpQueriesPerMonth { get; init; }

    public bool McpWrite { get; init; }
}
