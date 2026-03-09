using OpenCortex.Core.Tenancy;

namespace OpenCortex.Core.Persistence;

public interface ITenantCatalogStore
{
    Task<TenantContext> EnsureTenantContextAsync(AuthenticatedUserProfile profile, CancellationToken cancellationToken = default);
}
