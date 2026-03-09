using OpenCortex.Core.Persistence;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresUsageCounterStore : IUsageCounterStore
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresUsageCounterStore(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<UsageCounterRecord?> GetCounterAsync(
        string customerId,
        string counterKey,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                customer_id,
                counter_key,
                value,
                reset_at,
                updated_at
            FROM {_connectionFactory.Schema}.usage_counters
            WHERE customer_id = @customer_id
              AND counter_key = @counter_key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("counter_key", counterKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadRecord(reader);
    }

    public async Task<UsageCounterRecord> IncrementCounterAsync(
        UsageCounterIncrementRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.usage_counters (
                customer_id,
                counter_key,
                value,
                reset_at
            )
            VALUES (
                @customer_id,
                @counter_key,
                @delta,
                @reset_at
            )
            ON CONFLICT (customer_id, counter_key) DO UPDATE SET
                value = {_connectionFactory.Schema}.usage_counters.value + EXCLUDED.value,
                reset_at = EXCLUDED.reset_at,
                updated_at = now()
            RETURNING
                customer_id,
                counter_key,
                value,
                reset_at,
                updated_at;
            """;
        command.Parameters.AddWithValue("customer_id", request.CustomerId);
        command.Parameters.AddWithValue("counter_key", request.CounterKey);
        command.Parameters.AddWithValue("delta", request.Delta);
        command.Parameters.AddWithValue("reset_at", (object?)request.ResetAt?.UtcDateTime ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadRecord(reader);
    }

    public async Task<UsageCounterRecord> SetCounterAsync(
        UsageCounterSetRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.usage_counters (
                customer_id,
                counter_key,
                value,
                reset_at
            )
            VALUES (
                @customer_id,
                @counter_key,
                @value,
                @reset_at
            )
            ON CONFLICT (customer_id, counter_key) DO UPDATE SET
                value = EXCLUDED.value,
                reset_at = EXCLUDED.reset_at,
                updated_at = now()
            RETURNING
                customer_id,
                counter_key,
                value,
                reset_at,
                updated_at;
            """;
        command.Parameters.AddWithValue("customer_id", request.CustomerId);
        command.Parameters.AddWithValue("counter_key", request.CounterKey);
        command.Parameters.AddWithValue("value", request.Value);
        command.Parameters.AddWithValue("reset_at", (object?)request.ResetAt?.UtcDateTime ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadRecord(reader);
    }

    private static UsageCounterRecord ReadRecord(Npgsql.NpgsqlDataReader reader)
    {
        return new UsageCounterRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? null : new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero),
            new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero));
    }
}
