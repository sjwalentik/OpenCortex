using OpenCortex.Core.Persistence;
using OpenCortex.Core.Tenancy;

namespace OpenCortex.Core.Tests;

public sealed class HostedBillingStateResolverTests
{
    [Fact]
    public void Resolve_KeepsPaidPlanDuringCancellationGracePeriod()
    {
        var now = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);
        var subscription = CreateSubscription(
            planId: "pro",
            status: "active",
            currentPeriodEnd: now.AddDays(7),
            cancelAtPeriodEnd: true);

        var state = HostedBillingStateResolver.Resolve(subscription, now);

        Assert.Equal("pro", state.PlanId);
        Assert.Equal("active", state.SubscriptionStatus);
        Assert.False(state.IsDowngradedToFree);
    }

    [Fact]
    public void Resolve_DowngradesExpiredCancelledSubscriptionsToFree()
    {
        var now = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);
        var subscription = CreateSubscription(
            planId: "pro",
            status: "canceled",
            currentPeriodEnd: now.AddMinutes(-1),
            cancelAtPeriodEnd: false);

        var state = HostedBillingStateResolver.Resolve(subscription, now);

        Assert.Equal("free", state.PlanId);
        Assert.Equal("cancelled", state.SubscriptionStatus);
        Assert.True(state.IsDowngradedToFree);
    }

    [Fact]
    public void Resolve_DowngradesPastDueSubscriptionsAfterBillingPeriodEnds()
    {
        var now = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);
        var subscription = CreateSubscription(
            planId: "pro",
            status: "past_due",
            currentPeriodEnd: now.AddDays(-2),
            cancelAtPeriodEnd: false);

        var state = HostedBillingStateResolver.Resolve(subscription, now);

        Assert.Equal("free", state.PlanId);
        Assert.Equal("past_due", state.SubscriptionStatus);
        Assert.True(state.IsDowngradedToFree);
    }

    [Fact]
    public void Resolve_NormalizesCanceledStatusSpelling()
    {
        var subscription = CreateSubscription(
            planId: "free",
            status: "canceled",
            currentPeriodEnd: null,
            cancelAtPeriodEnd: false);

        var state = HostedBillingStateResolver.Resolve(subscription, DateTimeOffset.UtcNow);

        Assert.Equal("cancelled", state.SubscriptionStatus);
    }

    private static SubscriptionRecord CreateSubscription(
        string planId,
        string status,
        DateTimeOffset? currentPeriodEnd,
        bool cancelAtPeriodEnd)
    {
        return new SubscriptionRecord(
            SubscriptionId: "sub_test",
            CustomerId: "cus_test",
            PlanId: planId,
            Status: status,
            StripeCustomerId: "stripe_cus_test",
            StripeSubscriptionId: "stripe_sub_test",
            SeatCount: 1,
            CurrentPeriodStart: currentPeriodEnd?.AddMonths(-1),
            CurrentPeriodEnd: currentPeriodEnd,
            CancelAtPeriodEnd: cancelAtPeriodEnd,
            CreatedAt: DateTimeOffset.UtcNow.AddMonths(-1),
            UpdatedAt: DateTimeOffset.UtcNow);
    }
}
