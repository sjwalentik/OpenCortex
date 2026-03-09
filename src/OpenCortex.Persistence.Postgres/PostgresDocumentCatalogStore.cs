using Npgsql;
using NpgsqlTypes;
using OpenCortex.Core.Persistence;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresDocumentCatalogStore : IDocumentCatalogStore
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresDocumentCatalogStore(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task UpsertDocumentsAsync(IReadOnlyList<DocumentRecord> documents, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(documents, cancellationToken);
    }

    public async Task MarkMissingDocumentsDeletedAsync(
        string brainId,
        string sourceRootId,
        IReadOnlyList<string> activeCanonicalPaths,
        DateTimeOffset indexedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (activeCanonicalPaths.Count == 0)
        {
            command.CommandText = $"""
                UPDATE {_connectionFactory.Schema}.documents
                SET is_deleted = true,
                    indexed_at = @indexed_at
                WHERE brain_id = @brain_id
                  AND source_root_id = @source_root_id;
                """;
        }
        else
        {
            command.CommandText = $"""
                UPDATE {_connectionFactory.Schema}.documents
                SET is_deleted = true,
                    indexed_at = @indexed_at
                WHERE brain_id = @brain_id
                  AND source_root_id = @source_root_id
                  AND canonical_path <> ALL(@active_paths);
                """;
            command.Parameters.AddWithValue("active_paths", activeCanonicalPaths.ToArray());
        }

        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("source_root_id", sourceRootId);
        command.Parameters.AddWithValue("indexed_at", indexedAt.UtcDateTime);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkMissingManagedDocumentsDeletedAsync(
        string brainId,
        IReadOnlyList<string> activeCanonicalPaths,
        DateTimeOffset indexedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (activeCanonicalPaths.Count == 0)
        {
            command.CommandText = $"""
                UPDATE {_connectionFactory.Schema}.documents
                SET is_deleted = true,
                    indexed_at = @indexed_at
                WHERE brain_id = @brain_id
                  AND source_root_id IS NULL;
                """;
        }
        else
        {
            command.CommandText = $"""
                UPDATE {_connectionFactory.Schema}.documents
                SET is_deleted = true,
                    indexed_at = @indexed_at
                WHERE brain_id = @brain_id
                  AND source_root_id IS NULL
                  AND canonical_path <> ALL(@active_paths);
                """;
            command.Parameters.AddWithValue("active_paths", activeCanonicalPaths.ToArray());
        }

        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("indexed_at", indexedAt.UtcDateTime);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentListItem>> ListDocumentsAsync(
        string brainId,
        string? sourceRootId = null,
        string? pathPrefix = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var sql = $"""
            SELECT
                document_id,
                brain_id,
                source_root_id,
                canonical_path,
                title,
                document_type,
                source_updated_at,
                indexed_at
            FROM {_connectionFactory.Schema}.documents
            WHERE brain_id = @brain_id
              AND is_deleted = false
            """;

        if (!string.IsNullOrWhiteSpace(sourceRootId))
        {
            sql += "\n  AND source_root_id = @source_root_id";
            command.Parameters.AddWithValue("source_root_id", sourceRootId);
        }

        if (!string.IsNullOrWhiteSpace(pathPrefix))
        {
            sql += "\n  AND canonical_path LIKE @path_prefix_like";
            command.Parameters.AddWithValue("path_prefix_like", pathPrefix.TrimEnd('/') + "/%");
        }

        sql += "\nORDER BY canonical_path\nLIMIT CAST(@limit AS integer);";

        command.CommandText = sql;

        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<DocumentListItem>();

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DocumentListItem(
                DocumentId: reader.GetString(0),
                BrainId: reader.GetString(1),
                SourceRootId: reader.IsDBNull(2) ? null : reader.GetString(2),
                CanonicalPath: reader.GetString(3),
                Title: reader.GetString(4),
                DocumentType: reader.IsDBNull(5) ? null : reader.GetString(5),
                SourceUpdatedAt: reader.IsDBNull(6) ? null : new DateTimeOffset(reader.GetDateTime(6), TimeSpan.Zero),
                IndexedAt: new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero)));
        }

        return results;
    }

    private async Task ExecuteAsync(IReadOnlyList<DocumentRecord> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        foreach (var document in documents)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO {_connectionFactory.Schema}.documents (
                    document_id,
                    brain_id,
                    source_root_id,
                    canonical_path,
                    title,
                    document_type,
                    frontmatter,
                    content_hash,
                    source_updated_at,
                    indexed_at,
                    is_deleted
                )
                VALUES (
                    @document_id,
                    @brain_id,
                    @source_root_id,
                    @canonical_path,
                    @title,
                    @document_type,
                    CAST(@frontmatter AS jsonb),
                    @content_hash,
                    @source_updated_at,
                    @indexed_at,
                    @is_deleted
                )
                ON CONFLICT (document_id) DO UPDATE SET
                    source_root_id = EXCLUDED.source_root_id,
                    canonical_path = EXCLUDED.canonical_path,
                    title = EXCLUDED.title,
                    document_type = EXCLUDED.document_type,
                    frontmatter = EXCLUDED.frontmatter,
                    content_hash = EXCLUDED.content_hash,
                    source_updated_at = EXCLUDED.source_updated_at,
                    indexed_at = EXCLUDED.indexed_at,
                    is_deleted = EXCLUDED.is_deleted;
                """;

            command.Parameters.AddWithValue("document_id", document.DocumentId);
            command.Parameters.AddWithValue("brain_id", document.BrainId);
            command.Parameters.AddWithValue("source_root_id", (object?)document.SourceRootId ?? DBNull.Value);
            command.Parameters.AddWithValue("canonical_path", document.CanonicalPath);
            command.Parameters.AddWithValue("title", document.Title);
            command.Parameters.AddWithValue("document_type", (object?)document.DocumentType ?? DBNull.Value);
            command.Parameters.AddWithValue("frontmatter", PostgresJson.Serialize(document.Frontmatter));
            command.Parameters.AddWithValue("content_hash", document.ContentHash);
            command.Parameters.AddWithValue("source_updated_at", (object?)document.SourceUpdatedAt?.UtcDateTime ?? DBNull.Value);
            command.Parameters.AddWithValue("indexed_at", document.IndexedAt.UtcDateTime);
            command.Parameters.AddWithValue("is_deleted", document.IsDeleted);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
