using Npgsql;
using PostgresException = Npgsql.PostgresException;
using OpenCortex.Core.Authoring;
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

        await AcquireTenantBootstrapLockAsync(connection, transaction, profile.ExternalId, cancellationToken);

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
        var brain = await GetPreferredActiveManagedContentBrainAsync(connection, transaction, customerId, cancellationToken);
        if (brain is null)
        {
            await RestoreRetiredManagedContentBrainsWithDocumentsAsync(connection, transaction, customerId, cancellationToken);

            brain = await GetPreferredActiveManagedContentBrainAsync(connection, transaction, customerId, cancellationToken)
                ?? await RestoreLatestRetiredManagedContentBrainAsync(connection, transaction, customerId, cancellationToken);
        }

        if (brain is null)
        {
            var slugSeed = TenantSlugGenerator.CreateSlugSeed(profile.DisplayName, profile.Email, customerId);
            var brainName = TenantSlugGenerator.BuildBrainName(profile.DisplayName, profile.Email);

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var brainId = CreateId("brain");
                var brainSlug = TenantSlugGenerator.BuildBrainSlug(slugSeed, brainId);

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
                insert.Parameters.AddWithValue("brain_id", brainId);
                insert.Parameters.AddWithValue("customer_id", customerId);
                insert.Parameters.AddWithValue("slug", brainSlug);
                insert.Parameters.AddWithValue("name", brainName);
                insert.Parameters.AddWithValue("description", "Default personal brain provisioned for hosted SaaS users.");

                try
                {
                    await using var inserted = await insert.ExecuteReaderAsync(cancellationToken);
                    await inserted.ReadAsync(cancellationToken);

                    brain = new BrainRow(
                        inserted.GetString(0),
                        inserted.GetString(1),
                        inserted.GetString(2));
                    break;
                }
                catch (PostgresException ex) when (ex.SqlState == "23505" && ex.ConstraintName == "brains_slug_key")
                {
                    if (attempt == 4)
                    {
                        throw;
                    }
                }
            }
        }

        if (brain is null)
        {
            throw new InvalidOperationException("Failed to provision a unique personal brain slug.");
        }

        await ConsolidateDuplicateManagedContentBrainsAsync(connection, transaction, customerId, brain, cancellationToken);
        return brain;
    }

    private async Task<BrainRow?> GetPreferredActiveManagedContentBrainAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string customerId,
        CancellationToken cancellationToken)
    {
        await using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = $"""
            SELECT
                b.brain_id,
                b.slug,
                b.name
            FROM {_connectionFactory.Schema}.brains b
            LEFT JOIN {_connectionFactory.Schema}.managed_documents md
                ON md.brain_id = b.brain_id
               AND md.is_deleted = false
            WHERE b.customer_id = @customer_id
              AND b.mode = 'managed-content'
              AND b.status != 'retired'
            GROUP BY b.brain_id, b.slug, b.name, b.created_at
            ORDER BY COUNT(md.managed_document_id) DESC, b.created_at DESC
            LIMIT 1;
            """;
        lookup.Parameters.AddWithValue("customer_id", customerId);

        await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new BrainRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2));
    }

    private async Task RestoreRetiredManagedContentBrainsWithDocumentsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string customerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.brains b
            SET status = 'active',
                updated_at = now()
            WHERE b.customer_id = @customer_id
              AND b.mode = 'managed-content'
              AND b.status = 'retired'
              AND EXISTS (
                  SELECT 1
                  FROM {_connectionFactory.Schema}.managed_documents md
                  WHERE md.brain_id = b.brain_id
                    AND md.is_deleted = false
              );
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<BrainRow?> RestoreLatestRetiredManagedContentBrainAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string customerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            WITH candidate AS (
                SELECT brain_id
                FROM {_connectionFactory.Schema}.brains
                WHERE customer_id = @customer_id
                  AND mode = 'managed-content'
                  AND status = 'retired'
                ORDER BY created_at DESC
                LIMIT 1
            )
            UPDATE {_connectionFactory.Schema}.brains b
            SET status = 'active',
                updated_at = now()
            FROM candidate
            WHERE b.brain_id = candidate.brain_id
            RETURNING b.brain_id, b.slug, b.name;
            """;
        command.Parameters.AddWithValue("customer_id", customerId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new BrainRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2));
    }

    private async Task ConsolidateDuplicateManagedContentBrainsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string customerId,
        BrainRow canonicalBrain,
        CancellationToken cancellationToken)
    {
        await ActivateCanonicalBrainAsync(connection, transaction, canonicalBrain.BrainId, cancellationToken);

        var documentsToMove = await ListManagedDocumentsToMoveAsync(
            connection,
            transaction,
            customerId,
            canonicalBrain.BrainId,
            cancellationToken);

        foreach (var document in documentsToMove)
        {
            var canonicalSlug = await GetAvailableManagedDocumentSlugAsync(
                connection,
                transaction,
                canonicalBrain.BrainId,
                document.ManagedDocumentId,
                document.Slug,
                cancellationToken);

            await MoveManagedDocumentToCanonicalBrainAsync(
                connection,
                transaction,
                canonicalBrain.BrainId,
                document.ManagedDocumentId,
                canonicalSlug,
                cancellationToken);
        }

        await RetireDuplicateManagedContentBrainsAsync(
            connection,
            transaction,
            customerId,
            canonicalBrain.BrainId,
            cancellationToken);
    }

    private async Task ActivateCanonicalBrainAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string canonicalBrainId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.brains
            SET status = 'active',
                updated_at = now()
            WHERE brain_id = @brain_id
              AND status != 'active';
            """;
        command.Parameters.AddWithValue("brain_id", canonicalBrainId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ManagedDocumentMoveRow>> ListManagedDocumentsToMoveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string customerId,
        string canonicalBrainId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT managed_document_id, brain_id, slug
            FROM {_connectionFactory.Schema}.managed_documents
            WHERE customer_id = @customer_id
              AND is_deleted = false
              AND brain_id != @canonical_brain_id
            ORDER BY updated_at, managed_document_id;
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("canonical_brain_id", canonicalBrainId);

        var results = new List<ManagedDocumentMoveRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ManagedDocumentMoveRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return results;
    }

    private async Task<string> GetAvailableManagedDocumentSlugAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string canonicalBrainId,
        string managedDocumentId,
        string requestedSlug,
        CancellationToken cancellationToken)
    {
        var normalizedBaseSlug = ManagedDocumentText.NormalizeSlug(requestedSlug);
        var candidate = normalizedBaseSlug;
        var suffix = 1;

        while (await ManagedDocumentSlugExistsAsync(
                   connection,
                   transaction,
                   canonicalBrainId,
                   managedDocumentId,
                   candidate,
                   cancellationToken))
        {
            candidate = $"{normalizedBaseSlug}-{suffix}";
            suffix += 1;
        }

        return candidate;
    }

    private async Task<bool> ManagedDocumentSlugExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string canonicalBrainId,
        string managedDocumentId,
        string slug,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT EXISTS (
                SELECT 1
                FROM {_connectionFactory.Schema}.managed_documents
                WHERE brain_id = @brain_id
                  AND managed_document_id != @managed_document_id
                  AND slug = @slug
                  AND is_deleted = false
            );
            """;
        command.Parameters.AddWithValue("brain_id", canonicalBrainId);
        command.Parameters.AddWithValue("managed_document_id", managedDocumentId);
        command.Parameters.AddWithValue("slug", slug);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool exists && exists;
    }

    private async Task MoveManagedDocumentToCanonicalBrainAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string canonicalBrainId,
        string managedDocumentId,
        string canonicalSlug,
        CancellationToken cancellationToken)
    {
        var canonicalPath = ManagedDocumentText.BuildCanonicalPath(canonicalSlug);

        await using (var documentCommand = connection.CreateCommand())
        {
            documentCommand.Transaction = transaction;
            documentCommand.CommandText = $"""
                UPDATE {_connectionFactory.Schema}.managed_documents
                SET brain_id = @canonical_brain_id,
                    slug = @slug,
                    updated_at = now()
                WHERE managed_document_id = @managed_document_id
                  AND brain_id != @canonical_brain_id;
                """;
            documentCommand.Parameters.AddWithValue("canonical_brain_id", canonicalBrainId);
            documentCommand.Parameters.AddWithValue("managed_document_id", managedDocumentId);
            documentCommand.Parameters.AddWithValue("slug", canonicalSlug);
            await documentCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = $"""
                UPDATE {_connectionFactory.Schema}.managed_document_versions
                SET brain_id = @canonical_brain_id,
                    slug = @slug
                WHERE managed_document_id = @managed_document_id
                  AND brain_id != @canonical_brain_id;
                """;
            versionCommand.Parameters.AddWithValue("canonical_brain_id", canonicalBrainId);
            versionCommand.Parameters.AddWithValue("managed_document_id", managedDocumentId);
            versionCommand.Parameters.AddWithValue("slug", canonicalSlug);
            await versionCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var indexedDocumentCommand = connection.CreateCommand())
        {
            indexedDocumentCommand.Transaction = transaction;
            indexedDocumentCommand.CommandText = $"""
                UPDATE {_connectionFactory.Schema}.documents
                SET brain_id = @canonical_brain_id,
                    canonical_path = @canonical_path
                WHERE document_id = @managed_document_id
                  AND brain_id != @canonical_brain_id;
                """;
            indexedDocumentCommand.Parameters.AddWithValue("canonical_brain_id", canonicalBrainId);
            indexedDocumentCommand.Parameters.AddWithValue("managed_document_id", managedDocumentId);
            indexedDocumentCommand.Parameters.AddWithValue("canonical_path", canonicalPath);
            await indexedDocumentCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var chunkCommand = connection.CreateCommand())
        {
            chunkCommand.Transaction = transaction;
            chunkCommand.CommandText = $"""
                UPDATE {_connectionFactory.Schema}.chunks
                SET brain_id = @canonical_brain_id
                WHERE document_id = @managed_document_id
                  AND brain_id != @canonical_brain_id;
                """;
            chunkCommand.Parameters.AddWithValue("canonical_brain_id", canonicalBrainId);
            chunkCommand.Parameters.AddWithValue("managed_document_id", managedDocumentId);
            await chunkCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var edgeCommand = connection.CreateCommand())
        {
            edgeCommand.Transaction = transaction;
            edgeCommand.CommandText = $"""
                UPDATE {_connectionFactory.Schema}.link_edges
                SET brain_id = @canonical_brain_id
                WHERE brain_id != @canonical_brain_id
                  AND (
                      from_document_id = @managed_document_id
                      OR to_document_id = @managed_document_id
                  );
                """;
            edgeCommand.Parameters.AddWithValue("canonical_brain_id", canonicalBrainId);
            edgeCommand.Parameters.AddWithValue("managed_document_id", managedDocumentId);
            await edgeCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var embeddingCommand = connection.CreateCommand();
        embeddingCommand.Transaction = transaction;
        embeddingCommand.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.embeddings e
            SET brain_id = @canonical_brain_id
            WHERE brain_id != @canonical_brain_id
              AND EXISTS (
                  SELECT 1
                  FROM {_connectionFactory.Schema}.chunks c
                  WHERE c.chunk_id = e.chunk_id
                    AND c.document_id = @managed_document_id
              );
            """;
        embeddingCommand.Parameters.AddWithValue("canonical_brain_id", canonicalBrainId);
        embeddingCommand.Parameters.AddWithValue("managed_document_id", managedDocumentId);
        await embeddingCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task RetireDuplicateManagedContentBrainsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string customerId,
        string canonicalBrainId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.brains
            SET status = 'retired',
                updated_at = now()
            WHERE customer_id = @customer_id
              AND mode = 'managed-content'
              AND brain_id != @canonical_brain_id
              AND status != 'retired';
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("canonical_brain_id", canonicalBrainId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AcquireTenantBootstrapLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string externalId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtext(@lock_key), 0);";
        command.Parameters.AddWithValue("lock_key", externalId);
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private sealed record ManagedDocumentMoveRow(string ManagedDocumentId, string BrainId, string Slug);
}
