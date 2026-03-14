using Npgsql;
using OpenCortex.Core;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresUserProviderConfigRepository : IUserProviderConfigRepository
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresUserProviderConfigRepository(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<UserProviderConfig?> GetAsync(Guid userId, string providerId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT
                config_id,
                customer_id,
                user_id,
                provider_id,
                auth_type,
                encrypted_api_key,
                encrypted_access_token,
                encrypted_refresh_token,
                token_expires_at,
                settings_json,
                is_enabled,
                created_at,
                updated_at
            FROM {_connectionFactory.Schema}.user_provider_configs
            WHERE user_id = @user_id AND provider_id = @provider_id;
            """;

        command.Parameters.AddWithValue("user_id", userId.ToString());
        command.Parameters.AddWithValue("provider_id", providerId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapConfig(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<UserProviderConfig>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT
                config_id,
                customer_id,
                user_id,
                provider_id,
                auth_type,
                encrypted_api_key,
                encrypted_access_token,
                encrypted_refresh_token,
                token_expires_at,
                settings_json,
                is_enabled,
                created_at,
                updated_at
            FROM {_connectionFactory.Schema}.user_provider_configs
            WHERE user_id = @user_id
            ORDER BY provider_id;
            """;

        command.Parameters.AddWithValue("user_id", userId.ToString());

        var configs = new List<UserProviderConfig>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            configs.Add(MapConfig(reader));
        }

        return configs;
    }

    public async Task<UserProviderConfig> UpsertAsync(UserProviderConfig config, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.user_provider_configs (
                config_id,
                customer_id,
                user_id,
                provider_id,
                auth_type,
                encrypted_api_key,
                encrypted_access_token,
                encrypted_refresh_token,
                token_expires_at,
                settings_json,
                is_enabled,
                created_at
            ) VALUES (
                @config_id,
                @customer_id,
                @user_id,
                @provider_id,
                @auth_type,
                @encrypted_api_key,
                @encrypted_access_token,
                @encrypted_refresh_token,
                @token_expires_at,
                @settings_json::jsonb,
                @is_enabled,
                @created_at
            )
            ON CONFLICT (user_id, provider_id) DO UPDATE SET
                auth_type = EXCLUDED.auth_type,
                encrypted_api_key = EXCLUDED.encrypted_api_key,
                encrypted_access_token = EXCLUDED.encrypted_access_token,
                encrypted_refresh_token = EXCLUDED.encrypted_refresh_token,
                token_expires_at = EXCLUDED.token_expires_at,
                settings_json = EXCLUDED.settings_json,
                is_enabled = EXCLUDED.is_enabled
            RETURNING config_id, created_at, updated_at;
            """;

        command.Parameters.AddWithValue("config_id", config.ConfigId == Guid.Empty ? Guid.NewGuid() : config.ConfigId);
        command.Parameters.AddWithValue("customer_id", config.CustomerId.ToString());
        command.Parameters.AddWithValue("user_id", config.UserId.ToString());
        command.Parameters.AddWithValue("provider_id", config.ProviderId);
        command.Parameters.AddWithValue("auth_type", config.AuthType);
        command.Parameters.AddWithValue("encrypted_api_key", (object?)config.EncryptedApiKey ?? DBNull.Value);
        command.Parameters.AddWithValue("encrypted_access_token", (object?)config.EncryptedAccessToken ?? DBNull.Value);
        command.Parameters.AddWithValue("encrypted_refresh_token", (object?)config.EncryptedRefreshToken ?? DBNull.Value);
        command.Parameters.AddWithValue("token_expires_at", (object?)config.TokenExpiresAt ?? DBNull.Value);
        command.Parameters.AddWithValue("settings_json", (object?)config.SettingsJson ?? DBNull.Value);
        command.Parameters.AddWithValue("is_enabled", config.IsEnabled);
        command.Parameters.AddWithValue("created_at", config.CreatedAt == default ? DateTime.UtcNow : config.CreatedAt);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            config.ConfigId = reader.GetGuid(0);
            config.CreatedAt = reader.GetDateTime(1);
            config.UpdatedAt = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
        }

        return config;
    }

    public async Task DeleteAsync(Guid userId, string providerId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            DELETE FROM {_connectionFactory.Schema}.user_provider_configs
            WHERE user_id = @user_id AND provider_id = @provider_id;
            """;

        command.Parameters.AddWithValue("user_id", userId.ToString());
        command.Parameters.AddWithValue("provider_id", providerId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> HasAnyConfiguredAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT EXISTS(
                SELECT 1 FROM {_connectionFactory.Schema}.user_provider_configs
                WHERE user_id = @user_id AND is_enabled = true
            );
            """;

        command.Parameters.AddWithValue("user_id", userId.ToString());

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool b && b;
    }

    private static UserProviderConfig MapConfig(NpgsqlDataReader reader)
    {
        return new UserProviderConfig
        {
            ConfigId = reader.GetGuid(0),
            CustomerId = Guid.Parse(reader.GetString(1)),
            UserId = Guid.Parse(reader.GetString(2)),
            ProviderId = reader.GetString(3),
            AuthType = reader.GetString(4),
            EncryptedApiKey = reader.IsDBNull(5) ? null : reader.GetString(5),
            EncryptedAccessToken = reader.IsDBNull(6) ? null : reader.GetString(6),
            EncryptedRefreshToken = reader.IsDBNull(7) ? null : reader.GetString(7),
            TokenExpiresAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            SettingsJson = reader.IsDBNull(9) ? null : reader.GetString(9),
            IsEnabled = reader.GetBoolean(10),
            CreatedAt = reader.GetDateTime(11),
            UpdatedAt = reader.IsDBNull(12) ? null : reader.GetDateTime(12)
        };
    }
}
