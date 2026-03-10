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
        return await ListBrainsAsync(connection, null, cancellationToken);
    }

    public async Task<IReadOnlyList<BrainSummary>> ListBrainsByCustomerAsync(string customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        return await ListBrainsAsync(connection, customerId, cancellationToken);
    }

    public async Task<BrainDetail?> GetBrainAsync(string brainId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        return await GetBrainAsync(connection, null, brainId, cancellationToken);
    }

    public async Task<BrainDetail?> GetBrainByCustomerAsync(string customerId, string brainId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        return await GetBrainAsync(connection, customerId, brainId, cancellationToken);
    }

    private async Task<BrainDetail?> GetBrainAsync(
        Npgsql.NpgsqlConnection connection,
        string? customerId,
        string brainId,
        CancellationToken cancellationToken)
    {
        BrainDetail? brain = null;

        await using (var brainCommand = connection.CreateCommand())
        {
            var sql = $"""
                SELECT brain_id, name, slug, mode, status, description, customer_id
                FROM {_connectionFactory.Schema}.brains
                WHERE brain_id = @brain_id
                """;

            if (!string.IsNullOrWhiteSpace(customerId))
            {
                sql += """
                     AND customer_id = @customer_id
                     AND status != 'retired'
                    """;
            }

            sql += ";";

            brainCommand.CommandText = sql;
            if (!string.IsNullOrWhiteSpace(customerId))
            {
                brainCommand.Parameters.AddWithValue("customer_id", customerId);
            }
            brainCommand.Parameters.AddWithValue("brain_id", brainId);

            await using var reader = await brainCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                brain = new BrainDetail(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    []);
            }
        }

        if (brain is null)
        {
            return null;
        }

        var sourceRoots = await GetSourceRootsAsync(connection, brainId, cancellationToken);
        return brain with { SourceRoots = sourceRoots };
    }

    private async Task<IReadOnlyList<BrainSummary>> ListBrainsAsync(
        Npgsql.NpgsqlConnection connection,
        string? customerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var whereClause = string.Empty;

        if (!string.IsNullOrWhiteSpace(customerId))
        {
            whereClause = """
                WHERE b.customer_id = @customer_id
                  AND b.status != 'retired'
                """;
            command.Parameters.AddWithValue("customer_id", customerId);
        }

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
            {whereClause}
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

    public async Task<BrainDetail> CreateBrainAsync(BrainDefinition brain, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        await using var brainCommand = connection.CreateCommand();
        brainCommand.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.brains (
                brain_id, customer_id, slug, name, mode, status, description
            )
            VALUES (
                @brain_id, @customer_id, @slug, @name, @mode, @status, @description
            )
            ON CONFLICT (brain_id) DO UPDATE SET
                customer_id = EXCLUDED.customer_id,
                slug = EXCLUDED.slug,
                name = EXCLUDED.name,
                mode = EXCLUDED.mode,
                status = EXCLUDED.status,
                description = EXCLUDED.description,
                updated_at = now()
            RETURNING brain_id, name, slug, mode, status, description, customer_id;
            """;
        brainCommand.Parameters.AddWithValue("brain_id", brain.BrainId);
        brainCommand.Parameters.AddWithValue("customer_id", (object?)brain.CustomerId ?? DBNull.Value);
        brainCommand.Parameters.AddWithValue("slug", brain.Slug);
        brainCommand.Parameters.AddWithValue("name", brain.Name);
        brainCommand.Parameters.AddWithValue("mode", NormalizeMode(brain.Mode));
        brainCommand.Parameters.AddWithValue("status", brain.Status);
        brainCommand.Parameters.AddWithValue("description", DBNull.Value);

        await using var reader = await brainCommand.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new BrainDetail(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            []);
    }

    public async Task<BrainDetail?> UpdateBrainAsync(string brainId, string name, string slug, string mode, string status, string? description, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.brains
            SET name = @name,
                slug = @slug,
                mode = @mode,
                status = @status,
                description = @description,
                updated_at = now()
            WHERE brain_id = @brain_id
            RETURNING brain_id, name, slug, mode, status, description, customer_id;
            """;
        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("slug", slug);
        command.Parameters.AddWithValue("mode", mode);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var brain = new BrainDetail(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            []);

        await reader.CloseAsync();
        var sourceRoots = await GetSourceRootsAsync(connection, brainId, cancellationToken);
        return brain with { SourceRoots = sourceRoots };
    }

    public async Task<bool> RetireBrainAsync(string brainId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.brains
            SET status = 'retired', updated_at = now()
            WHERE brain_id = @brain_id AND status != 'retired';
            """;
        command.Parameters.AddWithValue("brain_id", brainId);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<SourceRootSummary> AddSourceRootAsync(string brainId, SourceRootDefinition sourceRoot, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.source_roots (
                source_root_id, brain_id, path, path_type, is_writable,
                include_patterns, exclude_patterns, watch_mode, is_active
            )
            VALUES (
                @source_root_id, @brain_id, @path, @path_type, @is_writable,
                CAST(@include_patterns AS jsonb), CAST(@exclude_patterns AS jsonb), @watch_mode, true
            )
            ON CONFLICT (source_root_id) DO UPDATE SET
                path = EXCLUDED.path,
                path_type = EXCLUDED.path_type,
                is_writable = EXCLUDED.is_writable,
                include_patterns = EXCLUDED.include_patterns,
                exclude_patterns = EXCLUDED.exclude_patterns,
                watch_mode = EXCLUDED.watch_mode,
                is_active = EXCLUDED.is_active,
                updated_at = now()
            RETURNING source_root_id, brain_id, path, path_type, is_writable,
                      include_patterns, exclude_patterns, watch_mode, is_active;
            """;
        command.Parameters.AddWithValue("source_root_id", sourceRoot.SourceRootId);
        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("path", sourceRoot.Path);
        command.Parameters.AddWithValue("path_type", sourceRoot.PathType);
        command.Parameters.AddWithValue("is_writable", sourceRoot.IsWritable);
        command.Parameters.AddWithValue("include_patterns", PostgresJson.Serialize(sourceRoot.IncludePatterns));
        command.Parameters.AddWithValue("exclude_patterns", PostgresJson.Serialize(sourceRoot.ExcludePatterns));
        command.Parameters.AddWithValue("watch_mode", sourceRoot.WatchMode);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadSourceRoot(reader);
    }

    public async Task<SourceRootSummary?> UpdateSourceRootAsync(string brainId, string sourceRootId, string path, string pathType, bool isWritable, string[] includePatterns, string[] excludePatterns, string watchMode, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.source_roots
            SET path = @path,
                path_type = @path_type,
                is_writable = @is_writable,
                include_patterns = CAST(@include_patterns AS jsonb),
                exclude_patterns = CAST(@exclude_patterns AS jsonb),
                watch_mode = @watch_mode,
                updated_at = now()
            WHERE source_root_id = @source_root_id AND brain_id = @brain_id
            RETURNING source_root_id, brain_id, path, path_type, is_writable,
                      include_patterns, exclude_patterns, watch_mode, is_active;
            """;
        command.Parameters.AddWithValue("source_root_id", sourceRootId);
        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("path", path);
        command.Parameters.AddWithValue("path_type", pathType);
        command.Parameters.AddWithValue("is_writable", isWritable);
        command.Parameters.AddWithValue("include_patterns", PostgresJson.Serialize(includePatterns));
        command.Parameters.AddWithValue("exclude_patterns", PostgresJson.Serialize(excludePatterns));
        command.Parameters.AddWithValue("watch_mode", watchMode);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSourceRoot(reader) : null;
    }

    public async Task<bool> RemoveSourceRootAsync(string brainId, string sourceRootId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {_connectionFactory.Schema}.source_roots
            WHERE source_root_id = @source_root_id AND brain_id = @brain_id;
            """;
        command.Parameters.AddWithValue("source_root_id", sourceRootId);
        command.Parameters.AddWithValue("brain_id", brainId);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    private async Task<IReadOnlyList<SourceRootSummary>> GetSourceRootsAsync(Npgsql.NpgsqlConnection connection, string brainId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT source_root_id, brain_id, path, path_type, is_writable,
                   include_patterns, exclude_patterns, watch_mode, is_active
            FROM {_connectionFactory.Schema}.source_roots
            WHERE brain_id = @brain_id
            ORDER BY source_root_id;
            """;
        command.Parameters.AddWithValue("brain_id", brainId);

        var roots = new List<SourceRootSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            roots.Add(ReadSourceRoot(reader));
        }

        return roots;
    }

    private static SourceRootSummary ReadSourceRoot(Npgsql.NpgsqlDataReader reader)
    {
        return new SourceRootSummary(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            PostgresJson.Deserialize<string[]>(reader.IsDBNull(5) ? "[]" : reader.GetString(5)) ?? [],
            PostgresJson.Deserialize<string[]>(reader.IsDBNull(6) ? "[]" : reader.GetString(6)) ?? [],
            reader.GetString(7),
            reader.GetBoolean(8));
    }

    private static string NormalizeMode(BrainMode mode) =>
        mode.ToString().ToLowerInvariant().Replace("managedcontent", "managed-content");

    public async Task UpsertBrainsAsync(IReadOnlyList<BrainDefinition> brains, CancellationToken cancellationToken = default)
    {
        if (brains.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        // Mark any persisted brains that are no longer in the current config as retired.
        // This keeps their history visible in the admin UI without polluting active brain listings.
        var currentBrainIds = brains.Select(b => b.BrainId).ToList();
        await using (var retireCommand = connection.CreateCommand())
        {
            var paramNames = currentBrainIds.Select((_, i) => $"@active_{i}").ToList();
            retireCommand.CommandText = $"""
                UPDATE {_connectionFactory.Schema}.brains
                SET status = 'retired', updated_at = now()
                WHERE status != 'retired'
                  AND brain_id NOT IN ({string.Join(", ", paramNames)});
                """;
            for (var i = 0; i < currentBrainIds.Count; i++)
            {
                retireCommand.Parameters.AddWithValue($"active_{i}", currentBrainIds[i]);
            }

            await retireCommand.ExecuteNonQueryAsync(cancellationToken);
        }

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
                brainCommand.Parameters.AddWithValue("mode", NormalizeMode(brain.Mode));
                brainCommand.Parameters.AddWithValue("status", brain.Status);
                brainCommand.Parameters.AddWithValue("description", DBNull.Value);

                await brainCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            // Remove source roots that are no longer present in the current config for this brain.
            // This prevents orphaned rows from accumulating when source root IDs are renamed or removed.
            var currentSourceRootIds = brain.SourceRoots.Select(sr => sr.SourceRootId).ToList();
            await using (var deleteCommand = connection.CreateCommand())
            {
                // Build a parameterized NOT IN list. If there are no current source roots, delete all.
                if (currentSourceRootIds.Count > 0)
                {
                    var paramNames = currentSourceRootIds
                        .Select((_, i) => $"@keep_{i}")
                        .ToList();
                    deleteCommand.CommandText = $"""
                        DELETE FROM {_connectionFactory.Schema}.source_roots
                        WHERE brain_id = @brain_id
                          AND source_root_id NOT IN ({string.Join(", ", paramNames)});
                        """;
                    deleteCommand.Parameters.AddWithValue("brain_id", brain.BrainId);
                    for (var i = 0; i < currentSourceRootIds.Count; i++)
                    {
                        deleteCommand.Parameters.AddWithValue($"keep_{i}", currentSourceRootIds[i]);
                    }
                }
                else
                {
                    deleteCommand.CommandText = $"""
                        DELETE FROM {_connectionFactory.Schema}.source_roots
                        WHERE brain_id = @brain_id;
                        """;
                    deleteCommand.Parameters.AddWithValue("brain_id", brain.BrainId);
                }

                await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
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
