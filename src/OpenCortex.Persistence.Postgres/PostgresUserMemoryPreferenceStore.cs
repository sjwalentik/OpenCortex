using OpenCortex.Core.Persistence;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresUserMemoryPreferenceStore : IUserMemoryPreferenceStore
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresUserMemoryPreferenceStore(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<string?> GetMemoryBrainIdAsync(string customerId, string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT memory_brain_id
            FROM {_connectionFactory.Schema}.customer_memberships
            WHERE customer_id = @customer_id
              AND user_id = @user_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("user_id", userId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is DBNull or null ? null : Convert.ToString(result);
    }

    public async Task SetMemoryBrainIdAsync(string customerId, string userId, string? memoryBrainId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.customer_memberships
            SET memory_brain_id = @memory_brain_id
            WHERE customer_id = @customer_id
              AND user_id = @user_id;
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("memory_brain_id", (object?)memoryBrainId ?? DBNull.Value);

        var updated = await command.ExecuteNonQueryAsync(cancellationToken);
        if (updated == 0)
        {
            throw new InvalidOperationException($"Membership for user '{userId}' in customer '{customerId}' was not found.");
        }
    }
}
