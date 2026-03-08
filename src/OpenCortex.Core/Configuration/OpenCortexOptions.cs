using OpenCortex.Core.Brains;
using OpenCortex.Core.Embeddings;

namespace OpenCortex.Core.Configuration;

public sealed class OpenCortexOptions
{
    public const string SectionName = "OpenCortex";

    public DatabaseOptions Database { get; init; } = new();

    public IndexingOptions Indexing { get; init; } = new();

    public EmbeddingOptions Embeddings { get; init; } = new();

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
