using Npgsql;
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
