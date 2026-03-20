using System.Security.Claims;
using OpenCortex.Core.Persistence;

namespace OpenCortex.Api;

internal static class MemoryBrainEndpoints
{
    public static async Task<IResult> GetMemoryBrainAsync(
        ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        IBrainCatalogStore brainCatalogStore,
        IUserMemoryPreferenceStore memoryPreferenceStore,
        OpenCortex.Orchestration.Memory.IMemoryBrainResolver memoryBrainResolver,
        CancellationToken cancellationToken)
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        return await BuildMemoryBrainResponseAsync(
            context!,
            brainCatalogStore,
            memoryPreferenceStore,
            memoryBrainResolver,
            cancellationToken);
    }

    public static async Task<IResult> UpdateMemoryBrainAsync(
        UpdateMemoryBrainRequest request,
        ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        IBrainCatalogStore brainCatalogStore,
        IUserMemoryPreferenceStore memoryPreferenceStore,
        OpenCortex.Orchestration.Memory.IMemoryBrainResolver memoryBrainResolver,
        CancellationToken cancellationToken)
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var requestedBrainId = request.MemoryBrainId?.Trim();
        if (!string.IsNullOrWhiteSpace(requestedBrainId))
        {
            var availableBrains = await ListAvailableMemoryBrainsAsync(context!.CustomerId, brainCatalogStore, cancellationToken);
            var matches = availableBrains.Any(brain => string.Equals(brain.BrainId, requestedBrainId, StringComparison.OrdinalIgnoreCase));
            if (!matches)
            {
                return Results.BadRequest(new
                {
                    message = $"Brain '{requestedBrainId}' is not an active managed-content brain in your workspace."
                });
            }
        }

        await memoryPreferenceStore.SetMemoryBrainIdAsync(context!.CustomerId, context.UserId, requestedBrainId, cancellationToken);
        return await BuildMemoryBrainResponseAsync(
            context,
            brainCatalogStore,
            memoryPreferenceStore,
            memoryBrainResolver,
            cancellationToken);
    }

    private static async Task<IResult> BuildMemoryBrainResponseAsync(
        OpenCortex.Core.Tenancy.TenantContext context,
        IBrainCatalogStore brainCatalogStore,
        IUserMemoryPreferenceStore memoryPreferenceStore,
        OpenCortex.Orchestration.Memory.IMemoryBrainResolver memoryBrainResolver,
        CancellationToken cancellationToken)
    {
        var availableBrains = await ListAvailableMemoryBrainsAsync(context.CustomerId, brainCatalogStore, cancellationToken);
        var configuredMemoryBrainId = await memoryPreferenceStore.GetMemoryBrainIdAsync(context.CustomerId, context.UserId, cancellationToken);
        var resolved = await memoryBrainResolver.ResolveAsync(context.CustomerId, context.UserId, cancellationToken);

        return Results.Ok(new
        {
            configuredMemoryBrainId,
            effectiveMemoryBrainId = resolved.Success ? resolved.BrainId : null,
            needsConfiguration = resolved.NeedsConfiguration,
            error = resolved.Success ? null : resolved.Error,
            availableBrains = availableBrains.Select(brain => new
            {
                brain.BrainId,
                brain.Name,
                brain.Slug,
                brain.Mode,
                brain.Status,
                brain.SourceRootCount
            })
        });
    }

    private static async Task<IReadOnlyList<BrainSummary>> ListAvailableMemoryBrainsAsync(
        string customerId,
        IBrainCatalogStore brainCatalogStore,
        CancellationToken cancellationToken)
    {
        return (await brainCatalogStore.ListBrainsByCustomerAsync(customerId, cancellationToken))
            .Where(brain =>
                string.Equals(brain.Mode, "managed-content", StringComparison.OrdinalIgnoreCase)
                && string.Equals(brain.Status, "active", StringComparison.OrdinalIgnoreCase))
            .OrderBy(brain => brain.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

internal sealed record UpdateMemoryBrainRequest(string? MemoryBrainId);
