using OpenCortex.Core.Persistence;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresUserWorkspaceRuntimeProfileStore : IUserWorkspaceRuntimeProfileStore
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresUserWorkspaceRuntimeProfileStore(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<string?> GetProfileIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT profile_id
            FROM {_connectionFactory.Schema}.user_workspace_runtime_profiles
            WHERE user_id = @user_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("user_id", userId.ToString());

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is DBNull or null ? null : Convert.ToString(result);
    }

    public async Task SetProfileIdAsync(Guid userId, string? profileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(profileId))
        {
            await using var delete = connection.CreateCommand();
            delete.CommandText = $"""
                DELETE FROM {_connectionFactory.Schema}.user_workspace_runtime_profiles
                WHERE user_id = @user_id;
                """;
            delete.Parameters.AddWithValue("user_id", userId.ToString());
            await delete.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        await using var upsert = connection.CreateCommand();
        upsert.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.user_workspace_runtime_profiles (
                user_id,
                profile_id
            )
            VALUES (
                @user_id,
                @profile_id
            )
            ON CONFLICT (user_id) DO UPDATE SET
                profile_id = EXCLUDED.profile_id,
                updated_at = now();
            """;
        upsert.Parameters.AddWithValue("user_id", userId.ToString());
        upsert.Parameters.AddWithValue("profile_id", profileId);

        await upsert.ExecuteNonQueryAsync(cancellationToken);
    }
}
