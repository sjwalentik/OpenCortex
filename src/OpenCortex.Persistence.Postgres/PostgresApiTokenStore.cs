using Npgsql;
using OpenCortex.Core.Persistence;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresApiTokenStore : IApiTokenStore
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresApiTokenStore(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ApiTokenSummary>> ListTokensAsync(
        string userId,
        string customerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                api_token_id,
                name,
                token_prefix,
                scopes,
                expires_at,
                last_used_at,
                created_at,
                revoked_at
            FROM {_connectionFactory.Schema}.api_tokens
            WHERE user_id = @user_id
              AND customer_id = @customer_id
            ORDER BY created_at DESC;
            """;
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("customer_id", customerId);

        var tokens = new List<ApiTokenSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tokens.Add(new ApiTokenSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                ReadScopes(reader, 3),
                ReadTimestamp(reader, 4),
                ReadTimestamp(reader, 5),
                ReadTimestamp(reader, 6)!.Value,
                ReadTimestamp(reader, 7)));
        }

        return tokens;
    }

    public async Task<ApiTokenRecord> CreateTokenAsync(
        ApiTokenCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.api_tokens (
                api_token_id,
                user_id,
                customer_id,
                name,
                token_hash,
                token_prefix,
                scopes,
                expires_at
            )
            VALUES (
                @api_token_id,
                @user_id,
                @customer_id,
                @name,
                @token_hash,
                @token_prefix,
                @scopes,
                @expires_at
            )
            RETURNING
                api_token_id,
                user_id,
                customer_id,
                name,
                token_prefix,
                scopes,
                expires_at,
                last_used_at,
                created_at,
                revoked_at;
            """;
        command.Parameters.AddWithValue("api_token_id", $"tok_{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("user_id", request.UserId);
        command.Parameters.AddWithValue("customer_id", request.CustomerId);
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("token_hash", request.TokenHash);
        command.Parameters.AddWithValue("token_prefix", request.TokenPrefix);
        command.Parameters.AddWithValue("scopes", request.Scopes.ToArray());
        command.Parameters.AddWithValue("expires_at", (object?)request.ExpiresAt?.UtcDateTime ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new ApiTokenRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            ReadScopes(reader, 5),
            ReadTimestamp(reader, 6),
            ReadTimestamp(reader, 7),
            ReadTimestamp(reader, 8)!.Value,
            ReadTimestamp(reader, 9));
    }

    public async Task<ApiTokenAuthenticationRecord?> GetActiveTokenByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                api_token_id,
                user_id,
                customer_id,
                token_prefix,
                scopes,
                expires_at,
                revoked_at
            FROM {_connectionFactory.Schema}.api_tokens
            WHERE token_hash = @token_hash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("token_hash", tokenHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ApiTokenAuthenticationRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            ReadScopes(reader, 4),
            ReadTimestamp(reader, 5),
            ReadTimestamp(reader, 6));
    }

    public async Task TouchLastUsedAsync(
        string apiTokenId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.api_tokens
            SET last_used_at = now()
            WHERE api_token_id = @api_token_id;
            """;
        command.Parameters.AddWithValue("api_token_id", apiTokenId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> RevokeTokenAsync(
        string apiTokenId,
        string userId,
        string customerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.api_tokens
            SET revoked_at = COALESCE(revoked_at, now())
            WHERE api_token_id = @api_token_id
              AND user_id = @user_id
              AND customer_id = @customer_id;
            """;
        command.Parameters.AddWithValue("api_token_id", apiTokenId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("customer_id", customerId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static IReadOnlyList<string> ReadScopes(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? [] : reader.GetFieldValue<string[]>(ordinal);

    private static DateTimeOffset? ReadTimestamp(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : new DateTimeOffset(reader.GetDateTime(ordinal), TimeSpan.Zero);
}
