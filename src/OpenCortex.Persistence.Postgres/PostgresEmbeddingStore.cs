using OpenCortex.Core.Embeddings;
using OpenCortex.Core.Persistence;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresEmbeddingStore : IEmbeddingStore
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresEmbeddingStore(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task UpsertEmbeddingsAsync(IReadOnlyList<EmbeddingRecord> embeddings, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(embeddings, cancellationToken);
    }

    public async Task DeleteStaleEmbeddingsAsync(string brainId, IReadOnlyList<string> activeEmbeddingIds, IReadOnlyList<string> activeChunkIds, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (activeChunkIds.Count == 0)
        {
            command.CommandText = $"DELETE FROM {_connectionFactory.Schema}.embeddings WHERE brain_id = @brain_id;";
            command.Parameters.AddWithValue("brain_id", brainId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        command.CommandText = $"""
            DELETE FROM {_connectionFactory.Schema}.embeddings
            WHERE brain_id = @brain_id
              AND chunk_id = ANY(@active_chunk_ids)
              AND embedding_id <> ALL(@active_embedding_ids);
            """;
        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("active_chunk_ids", activeChunkIds.ToArray());
        command.Parameters.AddWithValue("active_embedding_ids", activeEmbeddingIds.Count == 0 ? Array.Empty<string>() : activeEmbeddingIds.ToArray());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ExecuteAsync(IReadOnlyList<EmbeddingRecord> embeddings, CancellationToken cancellationToken)
    {
        if (embeddings.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        foreach (var embedding in embeddings)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO {_connectionFactory.Schema}.embeddings (
                    embedding_id,
                    brain_id,
                    chunk_id,
                    model,
                    dimensions,
                    vector,
                    created_at
                )
                VALUES (
                    @embedding_id,
                    @brain_id,
                    @chunk_id,
                    @model,
                    @dimensions,
                    CAST(@vector AS vector),
                    now()
                )
                ON CONFLICT (embedding_id) DO UPDATE SET
                    model = EXCLUDED.model,
                    dimensions = EXCLUDED.dimensions,
                    vector = EXCLUDED.vector;
                """;

            command.Parameters.AddWithValue("embedding_id", embedding.EmbeddingId);
            command.Parameters.AddWithValue("brain_id", embedding.BrainId);
            command.Parameters.AddWithValue("chunk_id", embedding.ChunkId);
            command.Parameters.AddWithValue("model", embedding.Model);
            command.Parameters.AddWithValue("dimensions", embedding.Dimensions);
            command.Parameters.AddWithValue("vector", EmbeddingVector.ToVectorLiteral(embedding.Vector));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
