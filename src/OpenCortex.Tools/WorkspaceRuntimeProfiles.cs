using System.Collections.ObjectModel;

namespace OpenCortex.Tools;

public static class WorkspaceRuntimeProfiles
{
    public const string DefaultProfileId = "default";
    public const string DotNet10ProfileId = "dotnet10";

    public static string NormalizeProfileId(string? profileId)
    {
        var normalized = profileId?.Trim().ToLowerInvariant();
        return normalized switch
        {
            DotNet10ProfileId => DotNet10ProfileId,
            _ => DefaultProfileId
        };
    }

    public static IReadOnlyList<WorkspaceRuntimeProfileDefinition> GetAvailableProfiles(ToolsOptions options)
    {
        var profiles = new List<WorkspaceRuntimeProfileDefinition>
        {
            new(
                DefaultProfileId,
                "Default",
                "Slim agent runtime with the standard OpenCortex toolchain.",
                true)
        };

        if (!string.IsNullOrWhiteSpace(options.DotNet10ContainerImage))
        {
            profiles.Add(new(
                DotNet10ProfileId,
                ".NET 10 SDK",
                "Adds the .NET 10 SDK for building and testing .NET repositories inside the workspace runtime.",
                true));
        }

        return new ReadOnlyCollection<WorkspaceRuntimeProfileDefinition>(profiles);
    }

    public static bool IsAvailableProfile(ToolsOptions options, string? profileId) =>
        GetAvailableProfiles(options).Any(profile =>
            string.Equals(profile.ProfileId, NormalizeProfileId(profileId), StringComparison.Ordinal));

    public static string ResolveContainerImage(ToolsOptions options, string? profileId)
    {
        var normalized = NormalizeProfileId(profileId);
        return normalized switch
        {
            DotNet10ProfileId when !string.IsNullOrWhiteSpace(options.DotNet10ContainerImage) => options.DotNet10ContainerImage!,
            _ => options.ContainerImage
        };
    }
}

public sealed record WorkspaceRuntimeProfileDefinition(
    string ProfileId,
    string Name,
    string Description,
    bool IsAvailable);
