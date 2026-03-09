namespace OpenCortex.Core.Tenancy;

public sealed record TenantContext(
    string UserId,
    string ExternalId,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    string CustomerId,
    string CustomerSlug,
    string CustomerName,
    string Role,
    string PlanId,
    string SubscriptionStatus,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    string BrainId,
    string BrainSlug,
    string BrainName);
