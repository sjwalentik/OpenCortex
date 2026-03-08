using Npgsql;
using OpenCortex.Core.Persistence;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresIndexRunStore : IIndexRunStore
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresIndexRunStore(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task StartIndexRunAsync(IndexRunRecord indexRun, CancellationToken cancellationToken = default)
    {
        return UpsertAsync(indexRun, cancellationToken);
    }

    public Task CompleteIndexRunAsync(IndexRunRecord indexRun, CancellationToken cancellationToken = default)
    {
        return UpsertAsync(indexRun, cancellationToken);
    }

    public async Task<IReadOnlyList<IndexRunRecord>> ListIndexRunsAsync(string? brainId = null, int limit = 20, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var whereClause = string.IsNullOrWhiteSpace(brainId) ? string.Empty : "WHERE brain_id = @brain_id";
        command.CommandText = $"""
            SELECT
                index_run_id,
                brain_id,
                trigger_type,
                status,
                started_at,
                completed_at,
                documents_seen,
                documents_indexed,
                documents_failed,
                error_summary
            FROM {_connectionFactory.Schema}.index_runs
            {whereClause}
            ORDER BY started_at DESC
            LIMIT @limit;
            """;

        if (!string.IsNullOrWhiteSpace(brainId))
        {
            command.Parameters.AddWithValue("brain_id", brainId);
        }

        command.Parameters.AddWithValue("limit", limit);

        var runs = new List<IndexRunRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(MapIndexRun(reader));
        }

        return runs;
    }

    public async Task<IndexRunRecord?> GetIndexRunAsync(string indexRunId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                index_run_id,
                brain_id,
                trigger_type,
                status,
                started_at,
                completed_at,
                documents_seen,
                documents_indexed,
                documents_failed,
                error_summary
            FROM {_connectionFactory.Schema}.index_runs
            WHERE index_run_id = @index_run_id;
            """;
        command.Parameters.AddWithValue("index_run_id", indexRunId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapIndexRun(reader);
    }

    private async Task UpsertAsync(IndexRunRecord indexRun, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.index_runs (
                index_run_id,
                brain_id,
                trigger_type,
                status,
                started_at,
                completed_at,
                documents_seen,
                documents_indexed,
                documents_failed,
                error_summary
            )
            VALUES (
                @index_run_id,
                @brain_id,
                @trigger_type,
                @status,
                @started_at,
                @completed_at,
                @documents_seen,
                @documents_indexed,
                @documents_failed,
                @error_summary
            )
            ON CONFLICT (index_run_id) DO UPDATE SET
                status = EXCLUDED.status,
                completed_at = EXCLUDED.completed_at,
                documents_seen = EXCLUDED.documents_seen,
                documents_indexed = EXCLUDED.documents_indexed,
                documents_failed = EXCLUDED.documents_failed,
                error_summary = EXCLUDED.error_summary;
            """;

        command.Parameters.AddWithValue("index_run_id", indexRun.IndexRunId);
        command.Parameters.AddWithValue("brain_id", indexRun.BrainId);
        command.Parameters.AddWithValue("trigger_type", indexRun.TriggerType);
        command.Parameters.AddWithValue("status", indexRun.Status);
        command.Parameters.AddWithValue("started_at", indexRun.StartedAt.UtcDateTime);
        command.Parameters.AddWithValue("completed_at", (object?)indexRun.CompletedAt?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("documents_seen", indexRun.DocumentsSeen);
        command.Parameters.AddWithValue("documents_indexed", indexRun.DocumentsIndexed);
        command.Parameters.AddWithValue("documents_failed", indexRun.DocumentsFailed);
        command.Parameters.AddWithValue("error_summary", (object?)indexRun.ErrorSummary ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IndexRunRecord MapIndexRun(NpgsqlDataReader reader)
    {
        return new IndexRunRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc)),
            reader.IsDBNull(5) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetString(9));
    }
}
