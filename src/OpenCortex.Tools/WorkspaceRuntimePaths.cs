namespace OpenCortex.Tools;

public static class WorkspaceRuntimePaths
{
    public const string CodexProviderId = "openai-codex";
    public const string ClaudeCliProviderId = "claude-cli";
    private const string CodexStateDirectoryName = ".opencortex-codex";
    private const string ClaudeStateDirectoryName = ".opencortex-claude";

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

    // Claude CLI paths

    public static string GetClaudeHomePath(bool supportsContainerIsolation, string workspacePath)
        => supportsContainerIsolation
            ? "/home/ubuntu/.opencortex-claude-home"
            : Path.Combine(workspacePath, ".claude-home");

    public static string GetClaudeCredentialsFilePath(bool supportsContainerIsolation, string workspacePath)
        => supportsContainerIsolation
            ? "/home/ubuntu/.opencortex-claude-home/.claude/.credentials.json"
            : Path.Combine(workspacePath, ".claude-home", ".claude", ".credentials.json");

    public static string GetClaudeStateDirectoryPath(string workspacePath)
        => Path.Combine(workspacePath, ClaudeStateDirectoryName);

    public static string GetClaudeLoginLogPath(string workspacePath)
        => Path.Combine(GetClaudeStateDirectoryPath(workspacePath), "login.log");

    public static string GetClaudeLoginPidPath(string workspacePath)
        => Path.Combine(GetClaudeStateDirectoryPath(workspacePath), "login.pid");
}
