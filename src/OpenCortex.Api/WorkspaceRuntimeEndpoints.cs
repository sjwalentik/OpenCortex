using System.Security.Claims;
using Microsoft.Extensions.Options;
using OpenCortex.Core.Persistence;
using OpenCortex.Tools;

namespace OpenCortex.Api;

internal static class WorkspaceRuntimeEndpoints
{
    public static async Task<IResult> GetWorkspaceRuntimeAsync(
        ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        IUserWorkspaceRuntimeProfileStore profileStore,
        IOptions<ToolsOptions> toolsOptions,
        IWorkspaceManager workspaceManager,
        CancellationToken cancellationToken)
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var userGuid = GuidFromString(context!.ExternalId);
        return await BuildWorkspaceRuntimeResponseAsync(
            userGuid,
            profileStore,
            toolsOptions.Value,
            workspaceManager,
            restartRequested: false,
            cancellationToken);
    }

    public static async Task<IResult> UpdateWorkspaceRuntimeAsync(
        UpdateWorkspaceRuntimeRequest request,
        ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        IUserWorkspaceRuntimeProfileStore profileStore,
        IOptions<ToolsOptions> toolsOptions,
        IWorkspaceManager workspaceManager,
        CancellationToken cancellationToken)
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var options = toolsOptions.Value;
        var requestedProfileId = WorkspaceRuntimeProfiles.NormalizeProfileId(request.ProfileId);
        if (!WorkspaceRuntimeProfiles.IsAvailableProfile(options, requestedProfileId))
        {
            return Results.BadRequest(new
            {
                message = $"Workspace runtime profile '{requestedProfileId}' is not available in this deployment."
            });
        }

        var userGuid = GuidFromString(context!.ExternalId);
        var existingProfileId = WorkspaceRuntimeProfiles.NormalizeProfileId(
            await profileStore.GetProfileIdAsync(userGuid, cancellationToken));
        var restartRequested = !string.Equals(existingProfileId, requestedProfileId, StringComparison.Ordinal);

        await profileStore.SetProfileIdAsync(
            userGuid,
            string.Equals(requestedProfileId, WorkspaceRuntimeProfiles.DefaultProfileId, StringComparison.Ordinal)
                ? null
                : requestedProfileId,
            cancellationToken);

        if (restartRequested)
        {
            await workspaceManager.StopAsync(userGuid, cancellationToken);
        }

        return await BuildWorkspaceRuntimeResponseAsync(
            userGuid,
            profileStore,
            options,
            workspaceManager,
            restartRequested,
            cancellationToken);
    }

    private static async Task<IResult> BuildWorkspaceRuntimeResponseAsync(
        Guid userGuid,
        IUserWorkspaceRuntimeProfileStore profileStore,
        ToolsOptions options,
        IWorkspaceManager workspaceManager,
        bool restartRequested,
        CancellationToken cancellationToken)
    {
        var configuredProfileId = await profileStore.GetProfileIdAsync(userGuid, cancellationToken);
        var effectiveProfileId = WorkspaceRuntimeProfiles.NormalizeProfileId(configuredProfileId);
        var supportsManagedProfiles = workspaceManager.SupportsContainerIsolation;

        return Results.Ok(new
        {
            scope = "user",
            supportsManagedProfiles,
            configuredProfileId,
            effectiveProfileId,
            restartRequested,
            message = supportsManagedProfiles
                ? "Changing the runtime profile restarts your isolated workspace so the new image is used on the next request."
                : "This deployment is using local workspaces, so runtime profiles are stored but only take effect in container-based workspace modes.",
            availableProfiles = WorkspaceRuntimeProfiles.GetAvailableProfiles(options).Select(profile => new
            {
                profile.ProfileId,
                profile.Name,
                profile.Description,
                profile.IsAvailable
            })
        });
    }

    private static Guid GuidFromString(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }
}

internal sealed record UpdateWorkspaceRuntimeRequest(string? ProfileId);
