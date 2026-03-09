namespace OpenCortex.Core.Persistence;

public interface ISubscriptionStore
{
    Task<SubscriptionRecord> EnsureFreeSubscriptionAsync(
        string customerId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionRecord?> GetSubscriptionAsync(
        string customerId,
        CancellationToken cancellationToken = default);

    Task<CustomerBillingProfile?> GetCustomerBillingProfileAsync(
        string customerId,
        CancellationToken cancellationToken = default);

    Task<string?> FindCustomerIdByStripeCustomerIdAsync(
        string stripeCustomerId,
        CancellationToken cancellationToken = default);

    Task LinkStripeCustomerAsync(
        string customerId,
        string stripeCustomerId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionRecord> UpsertSubscriptionAsync(
        SubscriptionUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> TryRecordSubscriptionEventAsync(
        SubscriptionEventRecord record,
        CancellationToken cancellationToken = default);

    Task MarkSubscriptionEventProcessedAsync(
        string stripeEventId,
        CancellationToken cancellationToken = default);
}

public sealed record SubscriptionRecord(
    string SubscriptionId,
    string CustomerId,
    string PlanId,
    string Status,
    string? StripeCustomerId,
    string? StripeSubscriptionId,
    int SeatCount,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CustomerBillingProfile(
    string CustomerId,
    string? StripeCustomerId,
    string PlanId,
    string SubscriptionStatus,
    string? StripeSubscriptionId,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);

public sealed record SubscriptionUpsertRequest(
    string CustomerId,
    string PlanId,
    string Status,
    string? StripeCustomerId,
    string? StripeSubscriptionId,
    int SeatCount,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);

public sealed record SubscriptionEventRecord(
    string SubscriptionEventId,
    string CustomerId,
    string StripeEventId,
    string EventType,
    string Payload,
    DateTimeOffset OccurredAt);
