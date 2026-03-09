using System.Security.Claims;
using OpenCortex.Core.Tenancy;

namespace OpenCortex.Api;

internal static class HostedTenantClaims
{
    public static bool TryCreateProfile(ClaimsPrincipal principal, out AuthenticatedUserProfile? profile, out string? error)
    {
        profile = null;
        error = null;

        var externalId =
            GetClaimValue(principal, ClaimTypes.NameIdentifier)
            ?? GetClaimValue(principal, "user_id")
            ?? GetClaimValue(principal, "sub");

        if (string.IsNullOrWhiteSpace(externalId))
        {
            error = "Authenticated token is missing a subject claim.";
            return false;
        }

        var email =
            GetClaimValue(principal, ClaimTypes.Email)
            ?? GetClaimValue(principal, "email");

        if (string.IsNullOrWhiteSpace(email))
        {
            error = "Authenticated token is missing an email claim.";
            return false;
        }

        var displayName =
            GetClaimValue(principal, "name")
            ?? GetClaimValue(principal, ClaimTypes.Name)
            ?? email.Split('@', 2, StringSplitOptions.TrimEntries)[0];

        var avatarUrl = GetClaimValue(principal, "picture");

        profile = new AuthenticatedUserProfile(
            externalId,
            email,
            displayName,
            avatarUrl);

        return true;
    }

    private static string? GetClaimValue(ClaimsPrincipal principal, string claimType) =>
        principal.FindFirst(claimType)?.Value;
}
