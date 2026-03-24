using System.Security.Claims;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Tenancy;

namespace OpenCortex.Api;

internal static class HostedTenantContextResolver
{
    public static async Task<(TenantContext? Context, IResult? ErrorResult)> ResolveAsync(
        ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken)
    {
        if (!HostedTenantClaims.TryCreateProfile(user, out var profile, out var error))
        {
            return (null, Results.Unauthorized());
        }

        var context = await catalogStore.EnsureTenantContextAsync(profile!, cancellationToken);
        return (context, null);
    }
}
