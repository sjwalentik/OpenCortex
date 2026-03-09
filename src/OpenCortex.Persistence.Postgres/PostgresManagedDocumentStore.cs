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

    public async Task<ManagedDocumentDetail> CreateManagedDocumentAsync(
        ManagedDocumentCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

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
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return ReadDetail(reader);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
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
        await using var command = connection.CreateCommand();

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
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadDetail(reader);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
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
        await using var command = connection.CreateCommand();
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

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
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
}
