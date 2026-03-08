namespace OpenCortex.Persistence.Postgres;

public static class PostgresMigrationCatalog
{
    public static IReadOnlyList<PostgresMigration> All { get; } =
    [
        new("0001", "Initial OpenCortex schema", "infra/postgres/migrations/0001_initial_schema.sql"),
    ];
}
