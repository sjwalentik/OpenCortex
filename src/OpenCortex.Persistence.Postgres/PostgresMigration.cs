namespace OpenCortex.Persistence.Postgres;

public sealed record PostgresMigration(string Id, string Description, string RelativePath);
