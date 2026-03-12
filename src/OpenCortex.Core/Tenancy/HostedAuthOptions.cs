namespace OpenCortex.Core.Tenancy;

public sealed class HostedAuthOptions
{
    public bool Enabled { get; init; }

    public string FirebaseProjectId { get; init; } = string.Empty;

    /// <summary>
    /// List of Firebase user IDs (sub claims) that have admin access.
    /// </summary>
    public IReadOnlyList<string> AdminUserIds { get; init; } = [];

    /// <summary>
    /// List of email patterns (exact match or wildcard like *@example.com) that have admin access.
    /// </summary>
    public IReadOnlyList<string> AdminEmailPatterns { get; init; } = [];

    public string Authority =>
        string.IsNullOrWhiteSpace(FirebaseProjectId)
            ? string.Empty
            : $"https://securetoken.google.com/{FirebaseProjectId}";

    public bool IsAdmin(string? userId, string? email)
    {
        if (!string.IsNullOrWhiteSpace(userId) && AdminUserIds.Contains(userId, StringComparer.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        foreach (var pattern in AdminEmailPatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            if (pattern.StartsWith('*'))
            {
                var suffix = pattern[1..];
                if (email.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(email, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
