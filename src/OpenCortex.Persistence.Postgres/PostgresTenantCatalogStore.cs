using Npgsql;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Tenancy;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresTenantCatalogStore : ITenantCatalogStore
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresTenantCatalogStore(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<TenantContext> EnsureTenantContextAsync(AuthenticatedUserProfile profile, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var user = await UpsertUserAsync(connection, transaction, profile, cancellationToken);
        var customer = await EnsurePersonalCustomerAsync(connection, transaction, user, profile, cancellationToken);
        await EnsureMembershipAsync(connection, transaction, user.UserId, customer.CustomerId, cancellationToken);
        var subscription = await EnsureSubscriptionAsync(connection, transaction, customer.CustomerId, cancellationToken);
        var brain = await EnsurePersonalBrainAsync(connection, transaction, customer.CustomerId, profile, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new TenantContext(
            user.UserId,
            profile.ExternalId,
            user.Email,
            user.DisplayName,
            user.AvatarUrl,
            customer.CustomerId,
            customer.Slug,
            customer.Name,
            "owner",
            subscription.PlanId,
            subscription.Status,
            subscription.CurrentPeriodEnd,
            subscription.CancelAtPeriodEnd,
            brain.BrainId,
            brain.Slug,
            brain.Name);
    }

    private async Task<UserRow> UpsertUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuthenticatedUserProfile profile,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.users (
                user_id,
                external_id,
                email,
                display_name,
                avatar_url,
                status,
                last_seen_at
            )
            VALUES (
                @user_id,
                @external_id,
                @email,
                @display_name,
                @avatar_url,
                'active',
                now()
            )
            ON CONFLICT (external_id) DO UPDATE SET
                email = EXCLUDED.email,
                display_name = EXCLUDED.display_name,
                avatar_url = EXCLUDED.avatar_url,
                status = 'active',
                last_seen_at = now(),
                updated_at = now()
            RETURNING user_id, email, display_name, avatar_url;
            """;
        command.Parameters.AddWithValue("user_id", CreateId("user"));
        command.Parameters.AddWithValue("external_id", profile.ExternalId);
        command.Parameters.AddWithValue("email", profile.Email);
        command.Parameters.AddWithValue("display_name", profile.DisplayName);
        command.Parameters.AddWithValue("avatar_url", (object?)profile.AvatarUrl ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new UserRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private async Task<CustomerRow> EnsurePersonalCustomerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        UserRow user,
        AuthenticatedUserProfile profile,
        CancellationToken cancellationToken)
    {
        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText = $"""
                SELECT customer_id, slug, name
                FROM {_connectionFactory.Schema}.customers
                WHERE owner_user_id = @owner_user_id
                LIMIT 1;
                """;
            lookup.Parameters.AddWithValue("owner_user_id", user.UserId);

            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new CustomerRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2));
            }
        }

        var slugSeed = TenantSlugGenerator.CreateSlugSeed(profile.DisplayName, profile.Email, user.UserId);
        var customerSlug = TenantSlugGenerator.BuildCustomerSlug(slugSeed, user.UserId);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.customers (
                customer_id,
                slug,
                name,
                status,
                owner_user_id
            )
            VALUES (
                @customer_id,
                @slug,
                @name,
                'active',
                @owner_user_id
            )
            RETURNING customer_id, slug, name;
            """;
        insert.Parameters.AddWithValue("customer_id", CreateId("cust"));
        insert.Parameters.AddWithValue("slug", customerSlug);
        insert.Parameters.AddWithValue("name", TenantSlugGenerator.BuildWorkspaceName(profile.DisplayName, profile.Email));
        insert.Parameters.AddWithValue("owner_user_id", user.UserId);

        await using var inserted = await insert.ExecuteReaderAsync(cancellationToken);
        await inserted.ReadAsync(cancellationToken);

        return new CustomerRow(
            inserted.GetString(0),
            inserted.GetString(1),
            inserted.GetString(2));
    }

    private async Task EnsureMembershipAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string userId,
        string customerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.customer_memberships (
                membership_id,
                user_id,
                customer_id,
                role
            )
            VALUES (
                @membership_id,
                @user_id,
                @customer_id,
                'owner'
            )
            ON CONFLICT (user_id, customer_id) DO UPDATE SET
                role = EXCLUDED.role;
            """;
        command.Parameters.AddWithValue("membership_id", CreateId("membership"));
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("customer_id", customerId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<BrainRow> EnsurePersonalBrainAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string customerId,
        AuthenticatedUserProfile profile,
        CancellationToken cancellationToken)
    {
        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText = $"""
                SELECT brain_id, slug, name
                FROM {_connectionFactory.Schema}.brains
                WHERE customer_id = @customer_id
                  AND mode = 'managed-content'
                  AND status != 'retired'
                ORDER BY created_at
                LIMIT 1;
                """;
            lookup.Parameters.AddWithValue("customer_id", customerId);

            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new BrainRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2));
            }
        }

        var slugSeed = TenantSlugGenerator.CreateSlugSeed(profile.DisplayName, profile.Email, customerId);
        var brainSlug = TenantSlugGenerator.BuildBrainSlug(slugSeed, customerId);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.brains (
                brain_id,
                customer_id,
                slug,
                name,
                mode,
                status,
                description
            )
            VALUES (
                @brain_id,
                @customer_id,
                @slug,
                @name,
                'managed-content',
                'active',
                @description
            )
            RETURNING brain_id, slug, name;
            """;
        insert.Parameters.AddWithValue("brain_id", CreateId("brain"));
        insert.Parameters.AddWithValue("customer_id", customerId);
        insert.Parameters.AddWithValue("slug", brainSlug);
        insert.Parameters.AddWithValue("name", TenantSlugGenerator.BuildBrainName(profile.DisplayName, profile.Email));
        insert.Parameters.AddWithValue("description", "Default personal brain provisioned for hosted SaaS users.");

        await using var inserted = await insert.ExecuteReaderAsync(cancellationToken);
        await inserted.ReadAsync(cancellationToken);

        return new BrainRow(
            inserted.GetString(0),
            inserted.GetString(1),
            inserted.GetString(2));
    }

    private async Task<SubscriptionRow> EnsureSubscriptionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string customerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.subscriptions (
                subscription_id,
                customer_id,
                plan_id,
                status
            )
            VALUES (
                @subscription_id,
                @customer_id,
                'free',
                'active'
            )
            ON CONFLICT (customer_id) DO UPDATE SET
                updated_at = now()
            RETURNING
                plan_id,
                status,
                current_period_end,
                cancel_at_period_end;
            """;
        command.Parameters.AddWithValue("subscription_id", CreateId("sub"));
        command.Parameters.AddWithValue("customer_id", customerId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new SubscriptionRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero),
            reader.GetBoolean(3));
    }

    private static string CreateId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    private sealed record UserRow(string UserId, string Email, string DisplayName, string? AvatarUrl);

    private sealed record CustomerRow(string CustomerId, string Slug, string Name);

    private sealed record SubscriptionRow(
        string PlanId,
        string Status,
        DateTimeOffset? CurrentPeriodEnd,
        bool CancelAtPeriodEnd);

    private sealed record BrainRow(string BrainId, string Slug, string Name);
}
