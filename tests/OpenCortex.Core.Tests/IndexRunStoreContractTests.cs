using OpenCortex.Core.Persistence;

namespace OpenCortex.Core.Tests;

public sealed class IndexRunStoreContractTests
{
    [Fact]
    public async Task FakeIndexRunStoreStyleImplementation_CanListAndFetchRuns()
    {
        var store = new InMemoryIndexRunStore();
        var run = new IndexRunRecord("run-1", "brain-a", "manual", "completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 3, 3, 0, null);

        await store.StartIndexRunAsync(run);
        await store.CompleteIndexRunAsync(run);

        var listed = await store.ListIndexRunsAsync("brain-a", 10);
        var fetched = await store.GetIndexRunAsync("run-1");
        await store.AddIndexRunErrorAsync(new IndexRunErrorRecord("err-1", "run-1", null, null, "TestError", "boom", DateTimeOffset.UtcNow));
        var errors = await store.ListIndexRunErrorsAsync("run-1");

        Assert.Equal(2, listed.Count);
        Assert.NotNull(fetched);
        Assert.Equal("run-1", fetched!.IndexRunId);
        Assert.Single(errors);
    }

    private sealed class InMemoryIndexRunStore : IIndexRunStore
    {
        private readonly List<IndexRunRecord> _runs = [];

        public Task StartIndexRunAsync(IndexRunRecord indexRun, CancellationToken cancellationToken = default)
        {
            _runs.Add(indexRun);
            return Task.CompletedTask;
        }

        public Task CompleteIndexRunAsync(IndexRunRecord indexRun, CancellationToken cancellationToken = default)
        {
            _runs.Add(indexRun);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IndexRunRecord>> ListIndexRunsAsync(string? brainId = null, int limit = 20, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<IndexRunRecord> result = string.IsNullOrWhiteSpace(brainId)
                ? _runs.Take(limit).ToArray()
                : _runs.Where(run => run.BrainId == brainId).Take(limit).ToArray();

            return Task.FromResult(result);
        }

        public Task<IndexRunRecord?> GetIndexRunAsync(string indexRunId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_runs.LastOrDefault(run => run.IndexRunId == indexRunId));
        }

        public Task AddIndexRunErrorAsync(IndexRunErrorRecord error, CancellationToken cancellationToken = default)
        {
            _errors.Add(error);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IndexRunErrorRecord>> ListIndexRunErrorsAsync(string indexRunId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<IndexRunErrorRecord> result = _errors.Where(error => error.IndexRunId == indexRunId).ToArray();
            return Task.FromResult(result);
        }

        private readonly List<IndexRunErrorRecord> _errors = [];
    }
}
