using Npgsql;
using NpgsqlTypes;
using OpenCortex.Core.Authoring;
using OpenCortex.Core.Persistence;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresManagedDocumentStore : IManagedDocumentStore
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresManagedDocumentStore(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ManagedDocumentSummary>> ListManagedDocumentsAsync(
        string customerId,
        string brainId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                managed_document_id,
                brain_id,
                customer_id,
                title,
                slug,
                status,
                word_count,
                created_at,
                updated_at
            FROM {_connectionFactory.Schema}.managed_documents
            WHERE customer_id = @customer_id
              AND brain_id = @brain_id
              AND is_deleted = false
            ORDER BY updated_at DESC
            LIMIT CAST(@limit AS integer);
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);

        var results = new List<ManagedDocumentSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ManagedDocumentSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                ManagedDocumentText.BuildCanonicalPath(reader.GetString(4)),
                reader.GetString(5),
                reader.GetInt32(6),
                new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero),
                new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero)));
        }

        return results;
    }

    public async Task<int> CountActiveManagedDocumentsAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*)::int
            FROM {_connectionFactory.Schema}.managed_documents
            WHERE customer_id = @customer_id
              AND is_deleted = false;
            """;
        command.Parameters.AddWithValue("customer_id", customerId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int count ? count : Convert.ToInt32(result);
    }

    public async Task<ManagedDocumentDetail?> GetManagedDocumentAsync(
        string customerId,
        string brainId,
        string managedDocumentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                managed_document_id,
                brain_id,
                customer_id,
                title,
                slug,
                content,
                frontmatter,
                content_hash,
                status,
                word_count,
                created_by,
                updated_by,
                created_at,
                updated_at,
                is_deleted
            FROM {_connectionFactory.Schema}.managed_documents
            WHERE customer_id = @customer_id
              AND brain_id = @brain_id
              AND managed_document_id = @managed_document_id
              AND is_deleted = false;
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("managed_document_id", managedDocumentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadDetail(reader);
    }

    public async Task<IReadOnlyList<ManagedDocumentVersionSummary>> ListManagedDocumentVersionsAsync(
        string customerId,
        string brainId,
        string managedDocumentId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                managed_document_version_id,
                managed_document_id,
                brain_id,
                customer_id,
                title,
                slug,
                status,
                content_hash,
                word_count,
                snapshot_kind,
                snapshot_by,
                created_at
            FROM {_connectionFactory.Schema}.managed_document_versions
            WHERE customer_id = @customer_id
              AND brain_id = @brain_id
              AND managed_document_id = @managed_document_id
            ORDER BY created_at DESC
            LIMIT CAST(@limit AS integer);
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("managed_document_id", managedDocumentId);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);

        var results = new List<ManagedDocumentVersionSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadVersionSummary(reader));
        }

        return results;
    }

    public async Task<ManagedDocumentVersionDetail?> GetManagedDocumentVersionAsync(
        string customerId,
        string brainId,
        string managedDocumentId,
        string managedDocumentVersionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        return await GetManagedDocumentVersionAsyncInternal(
            connection,
            transaction: null,
            customerId,
            brainId,
            managedDocumentId,
            managedDocumentVersionId,
            cancellationToken);
    }

    public async Task<ManagedDocumentDetail> CreateManagedDocumentAsync(
        ManagedDocumentCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var normalizedSlug = ManagedDocumentText.NormalizeSlug(string.IsNullOrWhiteSpace(request.Slug) ? request.Title : request.Slug);
        var contentHash = ManagedDocumentText.ComputeContentHash(request.Content);
        var wordCount = ManagedDocumentText.CountWords(request.Content);

        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.managed_documents (
                managed_document_id,
                brain_id,
                customer_id,
                title,
                slug,
                content,
                content_hash,
                word_count,
                frontmatter,
                status,
                created_by,
                updated_by,
                is_deleted
            )
            VALUES (
                @managed_document_id,
                @brain_id,
                @customer_id,
                @title,
                @slug,
                @content,
                @content_hash,
                @word_count,
                CAST(@frontmatter AS jsonb),
                @status,
                @created_by,
                @updated_by,
                false
            )
            RETURNING
                managed_document_id,
                brain_id,
                customer_id,
                title,
                slug,
                content,
                frontmatter,
                content_hash,
                status,
                word_count,
                created_by,
                updated_by,
                created_at,
                updated_at,
                is_deleted;
            """;
        command.Parameters.AddWithValue("managed_document_id", $"mdoc_{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("brain_id", request.BrainId);
        command.Parameters.AddWithValue("customer_id", request.CustomerId);
        command.Parameters.AddWithValue("title", request.Title.Trim());
        command.Parameters.AddWithValue("slug", normalizedSlug);
        command.Parameters.AddWithValue("content", request.Content);
        command.Parameters.AddWithValue("content_hash", contentHash);
        command.Parameters.AddWithValue("word_count", wordCount);
        command.Parameters.AddWithValue("frontmatter", PostgresJson.Serialize(request.Frontmatter));
        command.Parameters.AddWithValue("status", string.IsNullOrWhiteSpace(request.Status) ? "draft" : request.Status.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("created_by", request.UserId);
        command.Parameters.AddWithValue("updated_by", request.UserId);

        try
        {
            ManagedDocumentDetail document;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                document = ReadDetail(reader);
            }

            await InsertVersionSnapshotAsync(connection, transaction, document, "created", request.UserId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return document;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(
                $"A document with slug '{normalizedSlug}' already exists in this brain.",
                ex);
        }
    }

    public async Task<ManagedDocumentDetail?> UpdateManagedDocumentAsync(
        ManagedDocumentUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var normalizedSlug = ManagedDocumentText.NormalizeSlug(string.IsNullOrWhiteSpace(request.Slug) ? request.Title : request.Slug);
        var contentHash = ManagedDocumentText.ComputeContentHash(request.Content);
        var wordCount = ManagedDocumentText.CountWords(request.Content);

        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.managed_documents
            SET title = @title,
                slug = @slug,
                content = @content,
                content_hash = @content_hash,
                word_count = @word_count,
                frontmatter = CAST(@frontmatter AS jsonb),
                status = @status,
                updated_by = @updated_by,
                updated_at = now()
            WHERE customer_id = @customer_id
              AND brain_id = @brain_id
              AND managed_document_id = @managed_document_id
              AND is_deleted = false
            RETURNING
                managed_document_id,
                brain_id,
                customer_id,
                title,
                slug,
                content,
                frontmatter,
                content_hash,
                status,
                word_count,
                created_by,
                updated_by,
                created_at,
                updated_at,
                is_deleted;
            """;
        command.Parameters.AddWithValue("managed_document_id", request.ManagedDocumentId);
        command.Parameters.AddWithValue("brain_id", request.BrainId);
        command.Parameters.AddWithValue("customer_id", request.CustomerId);
        command.Parameters.AddWithValue("title", request.Title.Trim());
        command.Parameters.AddWithValue("slug", normalizedSlug);
        command.Parameters.AddWithValue("content", request.Content);
        command.Parameters.AddWithValue("content_hash", contentHash);
        command.Parameters.AddWithValue("word_count", wordCount);
        command.Parameters.AddWithValue("frontmatter", PostgresJson.Serialize(request.Frontmatter));
        command.Parameters.AddWithValue("status", string.IsNullOrWhiteSpace(request.Status) ? "draft" : request.Status.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("updated_by", request.UserId);

        try
        {
            ManagedDocumentDetail? document = null;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    document = ReadDetail(reader);
                }
            }

            if (document is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            await InsertVersionSnapshotAsync(connection, transaction, document, "updated", request.UserId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return document;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(
                $"A document with slug '{normalizedSlug}' already exists in this brain.",
                ex);
        }
    }

    public async Task<bool> SoftDeleteManagedDocumentAsync(
        string customerId,
        string brainId,
        string managedDocumentId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var document = await GetManagedDocumentAsyncInternal(
            connection,
            transaction,
            customerId,
            brainId,
            managedDocumentId,
            includeDeleted: false,
            cancellationToken);

        if (document is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await InsertVersionSnapshotAsync(connection, transaction, document, "deleted", userId, cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.managed_documents
            SET is_deleted = true,
                updated_by = @updated_by,
                updated_at = now()
            WHERE customer_id = @customer_id
              AND brain_id = @brain_id
              AND managed_document_id = @managed_document_id
              AND is_deleted = false;
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("managed_document_id", managedDocumentId);
        command.Parameters.AddWithValue("updated_by", userId);

        var deleted = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    public async Task<ManagedDocumentDetail?> RestoreManagedDocumentVersionAsync(
        string customerId,
        string brainId,
        string managedDocumentId,
        string managedDocumentVersionId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var version = await GetManagedDocumentVersionAsyncInternal(
            connection,
            transaction,
            customerId,
            brainId,
            managedDocumentId,
            managedDocumentVersionId,
            cancellationToken);

        if (version is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var existingDocument = await GetManagedDocumentAsyncInternal(
            connection,
            transaction,
            customerId,
            brainId,
            managedDocumentId,
            includeDeleted: true,
            cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("managed_document_id", managedDocumentId);
        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("title", version.Title);
        command.Parameters.AddWithValue("slug", version.Slug);
        command.Parameters.AddWithValue("content", version.Content);
        command.Parameters.AddWithValue("content_hash", version.ContentHash);
        command.Parameters.AddWithValue("word_count", version.WordCount);
        command.Parameters.AddWithValue("frontmatter", PostgresJson.Serialize(version.Frontmatter));
        command.Parameters.AddWithValue("status", version.Status);
        command.Parameters.AddWithValue("updated_by", userId);
        command.Parameters.AddWithValue("created_by", existingDocument?.CreatedBy ?? userId);

        command.CommandText = existingDocument is null
            ? $"""
                INSERT INTO {_connectionFactory.Schema}.managed_documents (
                    managed_document_id,
                    brain_id,
                    customer_id,
                    title,
                    slug,
                    content,
                    content_hash,
                    word_count,
                    frontmatter,
                    status,
                    created_by,
                    updated_by,
                    is_deleted
                )
                VALUES (
                    @managed_document_id,
                    @brain_id,
                    @customer_id,
                    @title,
                    @slug,
                    @content,
                    @content_hash,
                    @word_count,
                    CAST(@frontmatter AS jsonb),
                    @status,
                    @created_by,
                    @updated_by,
                    false
                )
                RETURNING
                    managed_document_id,
                    brain_id,
                    customer_id,
                    title,
                    slug,
                    content,
                    frontmatter,
                    content_hash,
                    status,
                    word_count,
                    created_by,
                    updated_by,
                    created_at,
                    updated_at,
                    is_deleted;
                """
            : $"""
                UPDATE {_connectionFactory.Schema}.managed_documents
                SET title = @title,
                    slug = @slug,
                    content = @content,
                    content_hash = @content_hash,
                    word_count = @word_count,
                    frontmatter = CAST(@frontmatter AS jsonb),
                    status = @status,
                    updated_by = @updated_by,
                    updated_at = now(),
                    is_deleted = false
                WHERE customer_id = @customer_id
                  AND brain_id = @brain_id
                  AND managed_document_id = @managed_document_id
                RETURNING
                    managed_document_id,
                    brain_id,
                    customer_id,
                    title,
                    slug,
                    content,
                    frontmatter,
                    content_hash,
                    status,
                    word_count,
                    created_by,
                    updated_by,
                    created_at,
                    updated_at,
                    is_deleted;
                """;

        try
        {
            ManagedDocumentDetail? restoredDocument = null;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    restoredDocument = ReadDetail(reader);
                }
            }

            if (restoredDocument is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            await InsertVersionSnapshotAsync(connection, transaction, restoredDocument, "restored", userId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return restoredDocument;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(
                $"A document with slug '{version.Slug}' already exists in this brain.",
                ex);
        }
    }

    public async Task<IReadOnlyList<ManagedDocumentDetail>> ListManagedDocumentsForIndexingAsync(
        string customerId,
        string brainId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                managed_document_id,
                brain_id,
                customer_id,
                title,
                slug,
                content,
                frontmatter,
                content_hash,
                status,
                word_count,
                created_by,
                updated_by,
                created_at,
                updated_at,
                is_deleted
            FROM {_connectionFactory.Schema}.managed_documents
            WHERE customer_id = @customer_id
              AND brain_id = @brain_id
              AND is_deleted = false
            ORDER BY slug;
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("brain_id", brainId);

        var results = new List<ManagedDocumentDetail>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadDetail(reader));
        }

        return results;
    }

    private async Task<ManagedDocumentDetail?> GetManagedDocumentAsyncInternal(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string customerId,
        string brainId,
        string managedDocumentId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                managed_document_id,
                brain_id,
                customer_id,
                title,
                slug,
                content,
                frontmatter,
                content_hash,
                status,
                word_count,
                created_by,
                updated_by,
                created_at,
                updated_at,
                is_deleted
            FROM {_connectionFactory.Schema}.managed_documents
            WHERE customer_id = @customer_id
              AND brain_id = @brain_id
              AND managed_document_id = @managed_document_id
              AND (@include_deleted OR is_deleted = false);
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("managed_document_id", managedDocumentId);
        command.Parameters.AddWithValue("include_deleted", includeDeleted);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadDetail(reader);
    }

    private async Task<ManagedDocumentVersionDetail?> GetManagedDocumentVersionAsyncInternal(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string customerId,
        string brainId,
        string managedDocumentId,
        string managedDocumentVersionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                managed_document_version_id,
                managed_document_id,
                brain_id,
                customer_id,
                title,
                slug,
                content,
                frontmatter,
                content_hash,
                status,
                word_count,
                snapshot_kind,
                snapshot_by,
                created_at
            FROM {_connectionFactory.Schema}.managed_document_versions
            WHERE customer_id = @customer_id
              AND brain_id = @brain_id
              AND managed_document_id = @managed_document_id
              AND managed_document_version_id = @managed_document_version_id;
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("managed_document_id", managedDocumentId);
        command.Parameters.AddWithValue("managed_document_version_id", managedDocumentVersionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadVersionDetail(reader);
    }

    private async Task InsertVersionSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ManagedDocumentDetail document,
        string snapshotKind,
        string snapshotBy,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.managed_document_versions (
                managed_document_version_id,
                managed_document_id,
                brain_id,
                customer_id,
                title,
                slug,
                content,
                frontmatter,
                content_hash,
                status,
                word_count,
                snapshot_kind,
                snapshot_by
            )
            VALUES (
                @managed_document_version_id,
                @managed_document_id,
                @brain_id,
                @customer_id,
                @title,
                @slug,
                @content,
                CAST(@frontmatter AS jsonb),
                @content_hash,
                @status,
                @word_count,
                @snapshot_kind,
                @snapshot_by
            );
            """;
        command.Parameters.AddWithValue("managed_document_version_id", $"mdver_{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("managed_document_id", document.ManagedDocumentId);
        command.Parameters.AddWithValue("brain_id", document.BrainId);
        command.Parameters.AddWithValue("customer_id", document.CustomerId);
        command.Parameters.AddWithValue("title", document.Title);
        command.Parameters.AddWithValue("slug", document.Slug);
        command.Parameters.AddWithValue("content", document.Content);
        command.Parameters.AddWithValue("frontmatter", PostgresJson.Serialize(document.Frontmatter));
        command.Parameters.AddWithValue("content_hash", document.ContentHash);
        command.Parameters.AddWithValue("status", document.Status);
        command.Parameters.AddWithValue("word_count", document.WordCount);
        command.Parameters.AddWithValue("snapshot_kind", snapshotKind);
        command.Parameters.AddWithValue("snapshot_by", snapshotBy);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ManagedDocumentDetail ReadDetail(NpgsqlDataReader reader)
    {
        var slug = reader.GetString(4);
        return new ManagedDocumentDetail(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            slug,
            ManagedDocumentText.BuildCanonicalPath(slug),
            reader.GetString(5),
            PostgresJson.Deserialize<Dictionary<string, string>>(reader.IsDBNull(6) ? "{}" : reader.GetString(6)) ?? [],
            reader.GetString(7),
            reader.GetString(8),
            reader.GetInt32(9),
            reader.GetString(10),
            reader.GetString(11),
            new DateTimeOffset(reader.GetDateTime(12), TimeSpan.Zero),
            new DateTimeOffset(reader.GetDateTime(13), TimeSpan.Zero),
            reader.GetBoolean(14));
    }

    private static ManagedDocumentVersionSummary ReadVersionSummary(NpgsqlDataReader reader)
    {
        var slug = reader.GetString(5);
        return new ManagedDocumentVersionSummary(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            slug,
            ManagedDocumentText.BuildCanonicalPath(slug),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetInt32(8),
            reader.GetString(9),
            reader.GetString(10),
            new DateTimeOffset(reader.GetDateTime(11), TimeSpan.Zero));
    }

    private static ManagedDocumentVersionDetail ReadVersionDetail(NpgsqlDataReader reader)
    {
        var slug = reader.GetString(5);
        return new ManagedDocumentVersionDetail(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            slug,
            ManagedDocumentText.BuildCanonicalPath(slug),
            reader.GetString(6),
            PostgresJson.Deserialize<Dictionary<string, string>>(reader.IsDBNull(7) ? "{}" : reader.GetString(7)) ?? [],
            reader.GetString(8),
            reader.GetString(9),
            reader.GetInt32(10),
            reader.GetString(11),
            reader.GetString(12),
            new DateTimeOffset(reader.GetDateTime(13), TimeSpan.Zero));
    }
}
