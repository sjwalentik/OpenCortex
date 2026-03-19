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
    public void GetMissingMigrations_ReturnsEmptyWhenAllCatalogMigrationsAreApplied()
    {
        var applied = PostgresMigrationCatalog.All.Select(migration => migration.Id).ToArray();

        var missing = PostgresMigrationSchemaValidator.GetMissingMigrations(applied);

        Assert.Empty(missing);
    }
}
