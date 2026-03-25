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

    public static string GetClaudeGlobalSettingsPath(bool supportsContainerIsolation, string workspacePath)
        => supportsContainerIsolation
            ? "/home/ubuntu/.opencortex-claude-home/.claude/settings.json"
            : Path.Combine(workspacePath, ".claude-home", ".claude", "settings.json");

    /// <summary>
    /// Path to ~/.claude.json — the Claude Code 2.x user-level config file where mcpServers is stored.
    /// </summary>
    public static string GetClaudeDotJsonPath(bool supportsContainerIsolation, string workspacePath)
        => supportsContainerIsolation
            ? "/home/ubuntu/.opencortex-claude-home/.claude.json"
            : Path.Combine(workspacePath, ".claude-home", ".claude.json");

    /// <summary>Key used in the credentials dictionary to pass the MCP token to workspace managers.</summary>
    public const string ClaudeMcpTokenKey = "claude-mcp-token";

    /// <summary>Key used in the credentials dictionary to pass the MCP server URL to workspace managers.</summary>
    public const string ClaudeMcpServerUrlKey = "claude-mcp-server-url";
}
