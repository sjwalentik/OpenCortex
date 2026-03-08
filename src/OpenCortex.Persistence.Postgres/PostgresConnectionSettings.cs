namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresConnectionSettings
{
    public string ConnectionString { get; init; } = string.Empty;

    public string Schema { get; init; } = "opencortex";
}
