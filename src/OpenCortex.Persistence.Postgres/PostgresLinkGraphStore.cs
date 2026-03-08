using Npgsql;
using OpenCortex.Core.Persistence;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresLinkGraphStore : ILinkGraphStore
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresLinkGraphStore(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task UpsertEdgesAsync(IReadOnlyList<LinkEdgeRecord> edges, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(edges, cancellationToken);
    }

    public async Task DeleteStaleEdgesAsync(string brainId, IReadOnlyList<string> activeEdgeIds, IReadOnlyList<string> activeDocumentIds, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (activeDocumentIds.Count == 0)
        {
            command.CommandText = $"DELETE FROM {_connectionFactory.Schema}.link_edges WHERE brain_id = @brain_id;";
            command.Parameters.AddWithValue("brain_id", brainId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        command.CommandText = $"""
            DELETE FROM {_connectionFactory.Schema}.link_edges
            WHERE brain_id = @brain_id
              AND from_document_id = ANY(@active_document_ids)
              AND link_edge_id <> ALL(@active_edge_ids);
            """;
        command.Parameters.AddWithValue("brain_id", brainId);
        command.Parameters.AddWithValue("active_document_ids", activeDocumentIds.ToArray());
        command.Parameters.AddWithValue("active_edge_ids", activeEdgeIds.Count == 0 ? Array.Empty<string>() : activeEdgeIds.ToArray());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ExecuteAsync(IReadOnlyList<LinkEdgeRecord> edges, CancellationToken cancellationToken)
    {
        if (edges.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        foreach (var edge in edges)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO {_connectionFactory.Schema}.link_edges (
                    link_edge_id,
                    brain_id,
                    from_document_id,
                    to_document_id,
                    target_ref,
                    link_text,
                    link_type
                )
                VALUES (
                    @link_edge_id,
                    @brain_id,
                    @from_document_id,
                    @to_document_id,
                    @target_ref,
                    @link_text,
                    @link_type
                )
                ON CONFLICT (link_edge_id) DO UPDATE SET
                    to_document_id = EXCLUDED.to_document_id,
                    target_ref = EXCLUDED.target_ref,
                    link_text = EXCLUDED.link_text,
                    link_type = EXCLUDED.link_type;
                """;

            command.Parameters.AddWithValue("link_edge_id", edge.LinkEdgeId);
            command.Parameters.AddWithValue("brain_id", edge.BrainId);
            command.Parameters.AddWithValue("from_document_id", edge.FromDocumentId);
            command.Parameters.AddWithValue("to_document_id", (object?)edge.ToDocumentId ?? DBNull.Value);
            command.Parameters.AddWithValue("target_ref", edge.TargetRef);
            command.Parameters.AddWithValue("link_text", (object?)edge.LinkText ?? DBNull.Value);
            command.Parameters.AddWithValue("link_type", edge.LinkType);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
