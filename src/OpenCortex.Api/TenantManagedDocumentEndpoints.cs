using OpenCortex.Core.Persistence;

namespace OpenCortex.Api;

internal static class TenantManagedDocumentEndpoints
{
    public static async Task<IResult> GetDocumentByCanonicalPathAsync(
        string customerId,
        string brainId,
        string? canonicalPath,
        IBrainCatalogStore brainCatalogStore,
        IManagedDocumentStore managedDocumentStore,
        CancellationToken cancellationToken)
    {
        var brain = await brainCatalogStore.GetBrainByCustomerAsync(customerId, brainId, cancellationToken);
        if (brain is null)
        {
            return Results.NotFound(new { message = $"Brain '{brainId}' was not found in your workspace." });
        }

        if (!string.Equals(brain.Mode, "managed-content", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = $"Brain '{brainId}' is not a managed-content brain." });
        }

        if (string.IsNullOrWhiteSpace(canonicalPath))
        {
            return Results.BadRequest(new { message = "Query parameter 'canonicalPath' or 'canonical_path' is required." });
        }

        var document = await managedDocumentStore.GetManagedDocumentByCanonicalPathAsync(
            customerId,
            brainId,
            canonicalPath,
            cancellationToken);

        return document is null
            ? Results.NotFound(new { message = $"Document '{canonicalPath}' was not found in brain '{brainId}'." })
            : Results.Ok(document);
    }
}
