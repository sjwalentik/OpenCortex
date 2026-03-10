using System.Text.RegularExpressions;

namespace OpenCortex.Core.Tenancy;

public static class TenantSlugGenerator
{
    private static readonly Regex NonSlugCharacterRegex = new("[^a-z0-9]+", RegexOptions.Compiled);

    public static string CreateSlugSeed(string? displayName, string email, string stableFallback)
    {
        var primary = string.IsNullOrWhiteSpace(displayName)
            ? email.Split('@', 2, StringSplitOptions.TrimEntries)[0]
            : displayName;

        var slug = NonSlugCharacterRegex.Replace(primary.ToLowerInvariant(), "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = stableFallback.Replace("_", "-", StringComparison.OrdinalIgnoreCase);
        }

        if (slug.Length > 24)
        {
            slug = slug[..24].Trim('-');
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "user";
        }

        return slug;
    }

    public static string GetStableSuffix(string value, int maxLength = 8) =>
        value.Length <= maxLength ? value : value[^maxLength..];

    public static string BuildCustomerSlug(string slugSeed, string stableId) =>
        $"personal-{slugSeed}-{GetStableSuffix(stableId)}";

    public static string BuildBrainSlug(string slugSeed, string stableId) =>
        $"personal-brain-{slugSeed}-{GetStableSuffix(stableId, 12)}";

    public static string BuildWorkspaceName(string? displayName, string email)
    {
        var ownerName = string.IsNullOrWhiteSpace(displayName)
            ? email
            : displayName.Trim();

        return $"{ownerName}'s Workspace";
    }

    public static string BuildBrainName(string? displayName, string email)
    {
        var ownerName = string.IsNullOrWhiteSpace(displayName)
            ? email
            : displayName.Trim();

        return $"{ownerName}'s Brain";
    }
}
