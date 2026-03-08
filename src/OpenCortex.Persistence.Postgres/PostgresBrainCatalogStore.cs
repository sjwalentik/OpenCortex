using OpenCortex.Core.Brains;
using OpenCortex.Core.Persistence;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresBrainCatalogStore : IBrainCatalogStore
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresBrainCatalogStore(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<BrainSummary>> ListBrainsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                b.brain_id,
                b.name,
                b.slug,
                b.mode,
                b.status,
                COUNT(sr.source_root_id)::int AS source_root_count
            FROM {_connectionFactory.Schema}.brains b
            LEFT JOIN {_connectionFactory.Schema}.source_roots sr ON sr.brain_id = b.brain_id
            GROUP BY b.brain_id, b.name, b.slug, b.mode, b.status
            ORDER BY b.name;
            """;

        var brains = new List<BrainSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            brains.Add(new BrainSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5)));
        }

        return brains;
    }

    public async Task UpsertBrainsAsync(IReadOnlyList<BrainDefinition> brains, CancellationToken cancellationToken = default)
    {
        if (brains.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        foreach (var brain in brains)
        {
            await using (var brainCommand = connection.CreateCommand())
            {
                brainCommand.CommandText = $"""
                    INSERT INTO {_connectionFactory.Schema}.brains (
                        brain_id,
                        customer_id,
                        slug,
                        name,
                        mode,
                        status,
                        description
                    )
                    VALUES (
                        @brain_id,
                        @customer_id,
                        @slug,
                        @name,
                        @mode,
                        @status,
                        @description
                    )
                    ON CONFLICT (brain_id) DO UPDATE SET
                        customer_id = EXCLUDED.customer_id,
                        slug = EXCLUDED.slug,
                        name = EXCLUDED.name,
                        mode = EXCLUDED.mode,
                        status = EXCLUDED.status,
                        description = EXCLUDED.description,
                        updated_at = now();
                    """;

                brainCommand.Parameters.AddWithValue("brain_id", brain.BrainId);
                brainCommand.Parameters.AddWithValue("customer_id", (object?)brain.CustomerId ?? DBNull.Value);
                brainCommand.Parameters.AddWithValue("slug", brain.Slug);
                brainCommand.Parameters.AddWithValue("name", brain.Name);
                brainCommand.Parameters.AddWithValue("mode", brain.Mode.ToString().ToLowerInvariant().Replace("managedcontent", "managed-content"));
                brainCommand.Parameters.AddWithValue("status", brain.Status);
                brainCommand.Parameters.AddWithValue("description", DBNull.Value);

                await brainCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var sourceRoot in brain.SourceRoots)
            {
                await using var rootCommand = connection.CreateCommand();
                rootCommand.CommandText = $"""
                    INSERT INTO {_connectionFactory.Schema}.source_roots (
                        source_root_id,
                        brain_id,
                        path,
                        path_type,
                        is_writable,
                        include_patterns,
                        exclude_patterns,
                        watch_mode,
                        is_active
                    )
                    VALUES (
                        @source_root_id,
                        @brain_id,
                        @path,
                        @path_type,
                        @is_writable,
                        CAST(@include_patterns AS jsonb),
                        CAST(@exclude_patterns AS jsonb),
                        @watch_mode,
                        @is_active
                    )
                    ON CONFLICT (source_root_id) DO UPDATE SET
                        path = EXCLUDED.path,
                        path_type = EXCLUDED.path_type,
                        is_writable = EXCLUDED.is_writable,
                        include_patterns = EXCLUDED.include_patterns,
                        exclude_patterns = EXCLUDED.exclude_patterns,
                        watch_mode = EXCLUDED.watch_mode,
                        is_active = EXCLUDED.is_active,
                        updated_at = now();
                    """;

                rootCommand.Parameters.AddWithValue("source_root_id", sourceRoot.SourceRootId);
                rootCommand.Parameters.AddWithValue("brain_id", brain.BrainId);
                rootCommand.Parameters.AddWithValue("path", sourceRoot.Path);
                rootCommand.Parameters.AddWithValue("path_type", sourceRoot.PathType);
                rootCommand.Parameters.AddWithValue("is_writable", sourceRoot.IsWritable);
                rootCommand.Parameters.AddWithValue("include_patterns", PostgresJson.Serialize(sourceRoot.IncludePatterns));
                rootCommand.Parameters.AddWithValue("exclude_patterns", PostgresJson.Serialize(sourceRoot.ExcludePatterns));
                rootCommand.Parameters.AddWithValue("watch_mode", sourceRoot.WatchMode);
                rootCommand.Parameters.AddWithValue("is_active", true);

                await rootCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
