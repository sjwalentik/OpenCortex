using OpenCortex.Core.Persistence;

namespace OpenCortex.Core.Tenancy;

public sealed record EffectiveBillingState(
    string PlanId,
    string StoredPlanId,
    string SubscriptionStatus,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd)
{
    public bool IsDowngradedToFree =>
        string.Equals(PlanId, HostedBillingStateResolver.FreePlanId, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(StoredPlanId, HostedBillingStateResolver.FreePlanId, StringComparison.OrdinalIgnoreCase);
}

public static class HostedBillingStateResolver
{
    public const string FreePlanId = "free";

    public static EffectiveBillingState Resolve(SubscriptionRecord subscription, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var storedPlanId = string.IsNullOrWhiteSpace(subscription.PlanId)
            ? FreePlanId
            : subscription.PlanId;
        var normalizedStatus = NormalizeSubscriptionStatus(subscription.Status);
        var effectivePlanId = storedPlanId;
        var periodEnded = subscription.CurrentPeriodEnd.HasValue && subscription.CurrentPeriodEnd.Value <= nowUtc;

        if (!string.Equals(storedPlanId, FreePlanId, StringComparison.OrdinalIgnoreCase))
        {
            if (subscription.CancelAtPeriodEnd && periodEnded)
            {
                effectivePlanId = FreePlanId;
                normalizedStatus = "cancelled";
            }
            else if (normalizedStatus is "cancelled" or "incomplete_expired")
            {
                effectivePlanId = FreePlanId;
            }
            else if (periodEnded && normalizedStatus is "past_due" or "unpaid")
            {
                effectivePlanId = FreePlanId;
            }
        }

        return new EffectiveBillingState(
            effectivePlanId,
            storedPlanId,
            normalizedStatus,
            subscription.CurrentPeriodStart,
            subscription.CurrentPeriodEnd,
            subscription.CancelAtPeriodEnd);
    }

    public static string NormalizeSubscriptionStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "active";
        }

        var normalized = status.Trim().ToLowerInvariant();
        return normalized switch
        {
            "canceled" => "cancelled",
            _ => normalized,
        };
    }
}
