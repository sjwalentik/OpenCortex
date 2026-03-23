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
    /// Optional container image for the managed .NET 10 workspace runtime profile.
    /// </summary>
    public string? DotNet10ContainerImage { get; set; }

    /// <summary>
    /// Session timeout in minutes before workspace cleanup (idle timeout).
    /// </summary>
    public int SessionTimeoutMinutes { get; set; } = 15;

    /// <summary>
    /// Maximum tool iterations per request (safety limit).
    /// </summary>
    public int MaxToolIterations { get; set; } = 25;

    /// <summary>
    /// Default command execution mode: "auto" or "approval".
    /// </summary>
    public string DefaultCommandMode { get; set; } = "approval";

    // ─────────────────────────────────────────────────────────────
    // Kubernetes Settings
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Kubernetes namespace for agent workspaces.
    /// </summary>
    public string KubernetesNamespace { get; set; } = "agent-workspaces";

    /// <summary>
    /// Storage class for PVCs. Use "standard" for default, or specify cluster-specific class.
    /// </summary>
    public string StorageClassName { get; set; } = "standard";

    /// <summary>
    /// PVC size for user workspaces.
    /// </summary>
    public string PvcSize { get; set; } = "10Gi";

    /// <summary>
    /// Timeout for pod startup in seconds.
    /// </summary>
    public int PodStartupTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Timeout for commands executed inside Docker containers or Kubernetes pods.
    /// </summary>
    public int CommandExecutionTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Image pull secret name for pulling container images (optional).
    /// </summary>
    public string? ImagePullSecretName { get; set; }

    // ─────────────────────────────────────────────────────────────
    // Docker Settings
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Docker network for agent containers.
    /// </summary>
    public string DockerNetwork { get; set; } = "opencortex-agents";

    // ─────────────────────────────────────────────────────────────
    // Resource Limits
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// CPU limit for containers (e.g., "1", "500m").
    /// </summary>
    public string CpuLimit { get; set; } = "1";

    /// <summary>
    /// Memory limit for containers (e.g., "1Gi", "512Mi").
    /// </summary>
    public string MemoryLimit { get; set; } = "1Gi";

    /// <summary>
    /// CPU request for containers.
    /// </summary>
    public string CpuRequest { get; set; } = "100m";

    /// <summary>
    /// Memory request for containers.
    /// </summary>
    public string MemoryRequest { get; set; } = "256Mi";
}
