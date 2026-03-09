using Npgsql;
using OpenCortex.Core.Persistence;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresSubscriptionStore : ISubscriptionStore
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresSubscriptionStore(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SubscriptionRecord> EnsureFreeSubscriptionAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
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
                subscription_id,
                customer_id,
                plan_id,
                status,
                stripe_customer_id,
                stripe_subscription_id,
                seat_count,
                current_period_start,
                current_period_end,
                cancel_at_period_end,
                created_at,
                updated_at;
            """;
        command.Parameters.AddWithValue("subscription_id", $"sub_{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("customer_id", customerId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadSubscription(reader);
    }

    public async Task<SubscriptionRecord?> GetSubscriptionAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                subscription_id,
                customer_id,
                plan_id,
                status,
                stripe_customer_id,
                stripe_subscription_id,
                seat_count,
                current_period_start,
                current_period_end,
                cancel_at_period_end,
                created_at,
                updated_at
            FROM {_connectionFactory.Schema}.subscriptions
            WHERE customer_id = @customer_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("customer_id", customerId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadSubscription(reader);
    }

    public async Task<CustomerBillingProfile?> GetCustomerBillingProfileAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                c.customer_id,
                c.stripe_customer_id,
                COALESCE(s.plan_id, 'free'),
                COALESCE(s.status, 'active'),
                s.stripe_subscription_id,
                s.current_period_end,
                COALESCE(s.cancel_at_period_end, false)
            FROM {_connectionFactory.Schema}.customers c
            LEFT JOIN {_connectionFactory.Schema}.subscriptions s ON s.customer_id = c.customer_id
            WHERE c.customer_id = @customer_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("customer_id", customerId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CustomerBillingProfile(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : new DateTimeOffset(reader.GetDateTime(5), TimeSpan.Zero),
            reader.GetBoolean(6));
    }

    public async Task<string?> FindCustomerIdByStripeCustomerIdAsync(
        string stripeCustomerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT customer_id
            FROM {_connectionFactory.Schema}.customers
            WHERE stripe_customer_id = @stripe_customer_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("stripe_customer_id", stripeCustomerId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    public async Task LinkStripeCustomerAsync(
        string customerId,
        string stripeCustomerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var customerCommand = connection.CreateCommand())
        {
            customerCommand.Transaction = transaction;
            customerCommand.CommandText = $"""
                UPDATE {_connectionFactory.Schema}.customers
                SET stripe_customer_id = @stripe_customer_id,
                    updated_at = now()
                WHERE customer_id = @customer_id;
                """;
            customerCommand.Parameters.AddWithValue("customer_id", customerId);
            customerCommand.Parameters.AddWithValue("stripe_customer_id", stripeCustomerId);
            await customerCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var subscriptionCommand = connection.CreateCommand())
        {
            subscriptionCommand.Transaction = transaction;
            subscriptionCommand.CommandText = $"""
                UPDATE {_connectionFactory.Schema}.subscriptions
                SET stripe_customer_id = @stripe_customer_id,
                    updated_at = now()
                WHERE customer_id = @customer_id;
                """;
            subscriptionCommand.Parameters.AddWithValue("customer_id", customerId);
            subscriptionCommand.Parameters.AddWithValue("stripe_customer_id", stripeCustomerId);
            await subscriptionCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SubscriptionRecord> UpsertSubscriptionAsync(
        SubscriptionUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.StripeCustomerId))
        {
            await using var customerCommand = connection.CreateCommand();
            customerCommand.Transaction = transaction;
            customerCommand.CommandText = $"""
                UPDATE {_connectionFactory.Schema}.customers
                SET stripe_customer_id = @stripe_customer_id,
                    updated_at = now()
                WHERE customer_id = @customer_id;
                """;
            customerCommand.Parameters.AddWithValue("customer_id", request.CustomerId);
            customerCommand.Parameters.AddWithValue("stripe_customer_id", request.StripeCustomerId);
            await customerCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.subscriptions (
                subscription_id,
                customer_id,
                plan_id,
                status,
                stripe_customer_id,
                stripe_subscription_id,
                seat_count,
                current_period_start,
                current_period_end,
                cancel_at_period_end
            )
            VALUES (
                @subscription_id,
                @customer_id,
                @plan_id,
                @status,
                @stripe_customer_id,
                @stripe_subscription_id,
                @seat_count,
                @current_period_start,
                @current_period_end,
                @cancel_at_period_end
            )
            ON CONFLICT (customer_id) DO UPDATE SET
                plan_id = EXCLUDED.plan_id,
                status = EXCLUDED.status,
                stripe_customer_id = EXCLUDED.stripe_customer_id,
                stripe_subscription_id = EXCLUDED.stripe_subscription_id,
                seat_count = EXCLUDED.seat_count,
                current_period_start = EXCLUDED.current_period_start,
                current_period_end = EXCLUDED.current_period_end,
                cancel_at_period_end = EXCLUDED.cancel_at_period_end,
                updated_at = now()
            RETURNING
                subscription_id,
                customer_id,
                plan_id,
                status,
                stripe_customer_id,
                stripe_subscription_id,
                seat_count,
                current_period_start,
                current_period_end,
                cancel_at_period_end,
                created_at,
                updated_at;
            """;
        command.Parameters.AddWithValue("subscription_id", $"sub_{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("customer_id", request.CustomerId);
        command.Parameters.AddWithValue("plan_id", request.PlanId);
        command.Parameters.AddWithValue("status", request.Status);
        command.Parameters.AddWithValue("stripe_customer_id", (object?)request.StripeCustomerId ?? DBNull.Value);
        command.Parameters.AddWithValue("stripe_subscription_id", (object?)request.StripeSubscriptionId ?? DBNull.Value);
        command.Parameters.AddWithValue("seat_count", request.SeatCount);
        command.Parameters.AddWithValue("current_period_start", (object?)request.CurrentPeriodStart?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("current_period_end", (object?)request.CurrentPeriodEnd?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("cancel_at_period_end", request.CancelAtPeriodEnd);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var subscription = ReadSubscription(reader);
        await transaction.CommitAsync(cancellationToken);
        return subscription;
    }

    public async Task<bool> TryRecordSubscriptionEventAsync(
        SubscriptionEventRecord record,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.subscription_events (
                subscription_event_id,
                customer_id,
                stripe_event_id,
                event_type,
                payload,
                occurred_at
            )
            VALUES (
                @subscription_event_id,
                @customer_id,
                @stripe_event_id,
                @event_type,
                CAST(@payload AS jsonb),
                @occurred_at
            )
            ON CONFLICT (stripe_event_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("subscription_event_id", record.SubscriptionEventId);
        command.Parameters.AddWithValue("customer_id", record.CustomerId);
        command.Parameters.AddWithValue("stripe_event_id", record.StripeEventId);
        command.Parameters.AddWithValue("event_type", record.EventType);
        command.Parameters.AddWithValue("payload", record.Payload);
        command.Parameters.AddWithValue("occurred_at", record.OccurredAt.UtcDateTime);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task MarkSubscriptionEventProcessedAsync(
        string stripeEventId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.subscription_events
            SET processed_at = now()
            WHERE stripe_event_id = @stripe_event_id;
            """;
        command.Parameters.AddWithValue("stripe_event_id", stripeEventId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SubscriptionRecord ReadSubscription(NpgsqlDataReader reader)
    {
        return new SubscriptionRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetInt32(6),
            reader.IsDBNull(7) ? null : new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero),
            reader.IsDBNull(8) ? null : new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero),
            reader.GetBoolean(9),
            new DateTimeOffset(reader.GetDateTime(10), TimeSpan.Zero),
            new DateTimeOffset(reader.GetDateTime(11), TimeSpan.Zero));
    }
}
