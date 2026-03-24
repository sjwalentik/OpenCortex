namespace OpenCortex.Tools;

public static class WorkspaceRuntimePaths
{
    public const string CodexProviderId = "openai-codex";
    public const string ClaudeCliProviderId = "claude-cli";
    private const string CodexStateDirectoryName = ".opencortex-codex";

    public static string GetCodexHomePath(bool supportsContainerIsolation, string workspacePath)
    {
        return supportsContainerIsolation
            ? "/home/ubuntu/.opencortex-codex-home"
            : Path.Combine(workspacePath, ".codex-home");
    }

    public static string GetCodexAuthFilePath(bool supportsContainerIsolation, string workspacePath)
    {
        return supportsContainerIsolation
            ? "/home/ubuntu/.opencortex-codex-home/.codex/auth.json"
            : Path.Combine(workspacePath, ".codex-home", ".codex", "auth.json");
    }

    public static string GetCodexStateDirectoryPath(string workspacePath)
        => Path.Combine(workspacePath, CodexStateDirectoryName);

    public static string GetCodexDeviceAuthLogPath(string workspacePath)
        => Path.Combine(GetCodexStateDirectoryPath(workspacePath), "device-auth.log");

    public static string GetCodexDeviceAuthPidPath(string workspacePath)
        => Path.Combine(GetCodexStateDirectoryPath(workspacePath), "device-auth.pid");
}
