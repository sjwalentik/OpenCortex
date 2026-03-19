namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresMigrationSchemaValidator
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresMigrationSchemaValidator(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<string>> ValidateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        await using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM pg_tables
                WHERE schemaname = @schema
                  AND tablename = 'schema_migrations'
            );
            """;
        existsCommand.Parameters.AddWithValue("schema", _connectionFactory.Schema);

        var schemaMigrationsExists = (bool?)await existsCommand.ExecuteScalarAsync(cancellationToken) ?? false;
        if (!schemaMigrationsExists)
        {
            return [$"Postgres schema validation failed: '{_connectionFactory.Schema}.schema_migrations' was not found. Apply the OpenCortex migrations before starting the service."];
        }

        await using var appliedCommand = connection.CreateCommand();
        appliedCommand.CommandText = $"SELECT migration_id FROM {QuoteIdentifier(_connectionFactory.Schema)}.schema_migrations ORDER BY migration_id;";

        var appliedMigrationIds = new List<string>();
        await using var reader = await appliedCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                appliedMigrationIds.Add(reader.GetString(0));
            }
        }

        var missingMigrations = GetMissingMigrations(appliedMigrationIds);
        if (missingMigrations.Count == 0)
        {
            return [];
        }

        var migrationSummary = string.Join(", ", missingMigrations.Select(migration => $"{migration.Id} ({migration.RelativePath})"));
        return [$"Postgres schema validation failed: missing OpenCortex migrations {migrationSummary}. Apply the pending migrations before starting the service."];
    }

    public static IReadOnlyList<PostgresMigration> GetMissingMigrations(IEnumerable<string>? appliedMigrationIds)
    {
        var applied = new HashSet<string>(appliedMigrationIds ?? [], StringComparer.OrdinalIgnoreCase);
        return PostgresMigrationCatalog.All
            .Where(migration => !applied.Contains(migration.Id))
            .ToArray();
    }

    private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
