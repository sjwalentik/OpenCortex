namespace OpenCortex.Core.Persistence;

public interface IUserWorkspaceRuntimeProfileStore
{
    Task<string?> GetProfileIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task SetProfileIdAsync(Guid userId, string? profileId, CancellationToken cancellationToken = default);
}
