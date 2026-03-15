namespace OpenCortex.Tools;

/// <summary>
/// Configuration options for the tools system.
/// </summary>
public sealed class ToolsOptions
{
    public const string SectionName = "OpenCortex:Tools";

    /// <summary>
    /// Workspace isolation mode: "local", "docker", or "kubernetes".
    /// </summary>
    public string WorkspaceMode { get; set; } = "local";

    /// <summary>
    /// Base path for user workspaces (local mode).
    /// </summary>
    public string WorkspaceBasePath { get; set; } = "./workspaces";

    /// <summary>
    /// Container image for agent runtime (docker/kubernetes mode).
    /// </summary>
    public string ContainerImage { get; set; } = "opencortex/agent-runtime:latest";

    /// <summary>
    /// Session timeout in minutes before workspace cleanup.
    /// </summary>
    public int SessionTimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// Maximum tool iterations per request (safety limit).
    /// </summary>
    public int MaxToolIterations { get; set; } = 25;

    /// <summary>
    /// Default command execution mode: "auto" or "approval".
    /// </summary>
    public string DefaultCommandMode { get; set; } = "approval";
}
