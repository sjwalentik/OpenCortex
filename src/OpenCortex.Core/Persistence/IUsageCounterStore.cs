namespace OpenCortex.Core.Persistence;

public interface IUsageCounterStore
{
    Task<UsageCounterRecord?> GetCounterAsync(
        string customerId,
        string counterKey,
        CancellationToken cancellationToken = default);

    Task<UsageCounterRecord> IncrementCounterAsync(
        UsageCounterIncrementRequest request,
        CancellationToken cancellationToken = default);

    Task<UsageCounterRecord> SetCounterAsync(
        UsageCounterSetRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record UsageCounterRecord(
    string CustomerId,
    string CounterKey,
    long Value,
    DateTimeOffset? ResetAt,
    DateTimeOffset UpdatedAt);

public sealed record UsageCounterIncrementRequest(
    string CustomerId,
    string CounterKey,
    long Delta,
    DateTimeOffset? ResetAt);

public sealed record UsageCounterSetRequest(
    string CustomerId,
    string CounterKey,
    long Value,
    DateTimeOffset? ResetAt);
