namespace OpenCortex.Core.Tenancy;

public sealed record AuthenticatedUserProfile(
    string ExternalId,
    string Email,
    string DisplayName,
    string? AvatarUrl);
