namespace OpenCortex.Tools.FileSystem;

internal static class SensitivePathPolicy
{
    private static readonly string[] AllowedEnvTemplateFileNames =
    {
        ".env.example",
        ".env.sample",
        ".env.template",
        ".env.local.example",
        ".env.development.example",
        ".env.production.example"
    };

    private static readonly string[] SensitiveFileNames =
    {
        ".env",
        ".env.local",
        ".env.development",
        ".env.production",
        ".envrc",
        ".npmrc",
        ".pypirc",
        ".git-credentials",
        ".terraform.lock.hcl",
        "credentials",
        "secrets.yaml",
        "secrets.yml",
        "secrets.json"
    };

    private static readonly string[] SensitivePathFragments =
    {
        "/.git/config",
        "/.git/hooks/",
        "/.git/info/",
        "/.git/refs/",
        "/.git/objects/",
        "/.ssh/",
        "/.aws/",
        "/.azure/",
        "/.config/gcloud/",
        "/.terraform/",
        "/.pulumi/",
        "/.venv/",
        "/venv/",
        "/.kube/config",
        "/id_rsa",
        "/id_ed25519",
        "/id_ecdsa",
        "/authorized_keys",
        "/known_hosts"
    };

    private static readonly string[] SensitiveExtensions =
    {
        ".pem",
        ".key",
        ".p12",
        ".pfx",
        ".crt",
        ".cer",
        ".der",
        ".asc",
        ".kdbx"
    };

    public static bool IsSensitive(string path)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        var normalizedForFragments = normalized.StartsWith("/", StringComparison.Ordinal)
            ? normalized
            : $"/{normalized}";
        var fileName = Path.GetFileName(normalized);

        if (SensitiveFileNames.Contains(fileName, StringComparer.Ordinal))
        {
            return true;
        }

        if (AllowedEnvTemplateFileNames.Contains(fileName, StringComparer.Ordinal))
        {
            return false;
        }

        if (fileName.StartsWith(".env.", StringComparison.Ordinal) ||
            fileName.StartsWith("secrets.", StringComparison.Ordinal))
        {
            return true;
        }

        if (SensitiveExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.Ordinal)))
        {
            return true;
        }

        return SensitivePathFragments.Any(fragment => normalizedForFragments.Contains(fragment, StringComparison.Ordinal));
    }
}
