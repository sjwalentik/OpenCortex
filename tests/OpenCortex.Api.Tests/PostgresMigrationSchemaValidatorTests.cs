using OpenCortex.Persistence.Postgres;

namespace OpenCortex.Api.Tests;

public sealed class PostgresMigrationSchemaValidatorTests
{
    [Fact]
    public void GetMissingMigrations_ReturnsEntireCatalogWhenNoMigrationsAreApplied()
    {
        var missing = PostgresMigrationSchemaValidator.GetMissingMigrations([]);

        Assert.Equal(PostgresMigrationCatalog.All.Count, missing.Count);
        Assert.Equal(PostgresMigrationCatalog.All.Select(migration => migration.Id), missing.Select(migration => migration.Id));
    }

    [Fact]
    public void GetMissingMigrations_TreatsAppliedIdsAsCaseInsensitive()
    {
        var applied = PostgresMigrationCatalog.All
            .Take(2)
            .Select(migration => migration.Id.ToLowerInvariant())
            .ToArray();

        var missing = PostgresMigrationSchemaValidator.GetMissingMigrations(applied);

        Assert.DoesNotContain(missing, migration => string.Equals(migration.Id, "0001", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(missing, migration => string.Equals(migration.Id, "0002", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(PostgresMigrationCatalog.All.Count - 2, missing.Count);
    }

    [Fact]
    public void GetMissingMigrations_TreatsLongFormAppliedIdsAsMatchingNumericCatalogIds()
    {
        var applied = new[] { "0001_initial_schema", "0002_identity_and_tenancy" };

        var missing = PostgresMigrationSchemaValidator.GetMissingMigrations(applied);

        Assert.DoesNotContain(missing, migration => string.Equals(migration.Id, "0001", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(missing, migration => string.Equals(migration.Id, "0002", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(PostgresMigrationCatalog.All.Count - 2, missing.Count);
    }

    [Fact]
    public void GetMissingMigrations_ReturnsEmptyWhenAllCatalogMigrationsAreApplied()
    {
        var applied = PostgresMigrationCatalog.All.Select(migration => migration.Id).ToArray();

        var missing = PostgresMigrationSchemaValidator.GetMissingMigrations(applied);

        Assert.Empty(missing);
    }

    [Fact]
    public void GetMissingMigrations_ReturnsEmptyWhenAllLongFormMigrationIdsAreApplied()
    {
        var applied = new[]
        {
            "0001_initial_schema",
            "0002_identity_and_tenancy",
            "0003_billing_schema",
            "0004_managed_content",
            "0005_api_tokens",
            "0006_managed_document_versions",
            "0007_conversations",
            "0008_user_provider_configs",
            "0009_user_memory_brain",
            "0009a_customer_membership_memory_brain",
        };

        var missing = PostgresMigrationSchemaValidator.GetMissingMigrations(applied);

        Assert.Empty(missing);
    }
}
