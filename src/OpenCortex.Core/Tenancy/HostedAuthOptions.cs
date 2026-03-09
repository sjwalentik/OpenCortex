namespace OpenCortex.Core.Tenancy;

public sealed class HostedAuthOptions
{
    public bool Enabled { get; init; }

    public string FirebaseProjectId { get; init; } = string.Empty;

    public string Authority =>
        string.IsNullOrWhiteSpace(FirebaseProjectId)
            ? string.Empty
            : $"https://securetoken.google.com/{FirebaseProjectId}";
}
