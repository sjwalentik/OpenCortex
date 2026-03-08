using Npgsql;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresConnectionFactory
{
    private readonly PostgresConnectionSettings _settings;

    public PostgresConnectionFactory(PostgresConnectionSettings settings)
    {
        _settings = settings;
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public string Schema => _settings.Schema;
}
