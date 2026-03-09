namespace OpenCortex.Persistence.Postgres;

public static class PostgresMigrationCatalog
{
    public static IReadOnlyList<PostgresMigration> All { get; } =
    [
        new("0001", "Initial OpenCortex schema", "infra/postgres/migrations/0001_initial_schema.sql"),
        new("0002", "Identity and tenancy schema", "infra/postgres/migrations/0002_identity_and_tenancy.sql"),
        new("0003", "Billing schema", "infra/postgres/migrations/0003_billing_schema.sql"),
        new("0004", "Managed content schema", "infra/postgres/migrations/0004_managed_content.sql"),
        new("0005", "API tokens schema", "infra/postgres/migrations/0005_api_tokens.sql"),
    ];
}
