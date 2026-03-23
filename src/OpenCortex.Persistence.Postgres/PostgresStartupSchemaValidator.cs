namespace OpenCortex.Persistence.Postgres;

public static class PostgresStartupSchemaValidator
{
    public static async Task<IReadOnlyList<string>> ValidateAsync(
        PostgresConnectionFactory connectionFactory,
        int expectedEmbeddingDimensions,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var migrationErrors = await new PostgresMigrationSchemaValidator(connectionFactory)
                .ValidateAsync(cancellationToken);
            if (migrationErrors.Count > 0)
            {
                return migrationErrors;
            }

            return await new PostgresEmbeddingSchemaValidator(connectionFactory)
                .ValidateAsync(expectedEmbeddingDimensions, cancellationToken);
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or TimeoutException or InvalidOperationException)
        {
            return [$"Postgres schema validation failed: {ex.Message}"];
        }
    }
}
