using Npgsql;
using OpenCortex.Core.Persistence;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresChunkStore : IChunkStore
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresChunkStore(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task UpsertChunksAsync(IReadOnlyList<ChunkRecord> chunks, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(chunks, cancellationToken);
    }

    public async Task DeleteStaleChunksAsync(string brainId, IReadOnlyList<string> activeChunkIds, IReadOnlyList<string> activeDocumentIds, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (activeDocumentIds.Count == 0)
        {
            command.CommandText = $"DELETE FROM {_connectionFactory.Schema}.chunks WHERE brain_id = @brain_id;";
            command.Parameters.AddWithValue("brain_id", brainId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        command.CommandText = $"""
            DELETE FROM {_connectionFactory.Schema}.chunks
            WHERE brain_id = @brain_id
              AND document_id = ANY(@active_document_ids)
              AND chunk_id <> ALL(@active_chunk_ids);
            """;
        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("active_document_ids", activeDocumentIds.ToArray());
        command.Parameters.AddWithValue("active_chunk_ids", activeChunkIds.Count == 0 ? Array.Empty<string>() : activeChunkIds.ToArray());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ExecuteAsync(IReadOnlyList<ChunkRecord> chunks, CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        foreach (var chunk in chunks)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO {_connectionFactory.Schema}.chunks (
                    chunk_id,
                    brain_id,
                    document_id,
                    chunk_index,
                    heading_path,
                    content,
                    token_count,
                    metadata
                )
                VALUES (
                    @chunk_id,
                    @brain_id,
                    @document_id,
                    @chunk_index,
                    @heading_path,
                    @content,
                    @token_count,
                    CAST(@metadata AS jsonb)
                )
                ON CONFLICT (chunk_id) DO UPDATE SET
                    chunk_index = EXCLUDED.chunk_index,
                    heading_path = EXCLUDED.heading_path,
                    content = EXCLUDED.content,
                    token_count = EXCLUDED.token_count,
                    metadata = EXCLUDED.metadata;
                """;

            command.Parameters.AddWithValue("chunk_id", chunk.ChunkId);
            command.Parameters.AddWithValue("brain_id", chunk.BrainId);
            command.Parameters.AddWithValue("document_id", chunk.DocumentId);
            command.Parameters.AddWithValue("chunk_index", chunk.ChunkIndex);
            command.Parameters.AddWithValue("heading_path", (object?)chunk.HeadingPath ?? DBNull.Value);
            command.Parameters.AddWithValue("content", chunk.Content);
            command.Parameters.AddWithValue("token_count", (object?)chunk.TokenCount ?? DBNull.Value);
            command.Parameters.AddWithValue("metadata", PostgresJson.Serialize(new Dictionary<string, string>()));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
