namespace OpenCortex.Core.Persistence;

public interface IIndexRunStore
{
    Task StartIndexRunAsync(IndexRunRecord indexRun, CancellationToken cancellationToken = default);

    Task CompleteIndexRunAsync(IndexRunRecord indexRun, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexRunRecord>> ListIndexRunsAsync(string? brainId = null, int limit = 20, CancellationToken cancellationToken = default);

    Task<IndexRunRecord?> GetIndexRunAsync(string indexRunId, CancellationToken cancellationToken = default);
}
