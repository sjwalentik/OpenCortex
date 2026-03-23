# Workspace Isolation Architecture

## Overview

OpenCortex agents execute tools (git clone, file operations, shell commands) in isolated workspaces. This document describes the architecture for secure, scalable workspace isolation using Docker and Kubernetes.

## Design Goals

1. **Security**: Each user's workspace is isolated from others
2. **Persistence**: Workspaces survive pod restarts (PVC per user)
3. **Cost Efficiency**: Scale to zero when not in use
4. **Flexibility**: User-configurable network policies
5. **Developer Experience**: Docker support for local development

## Workspace Tiers

| Tier | Behavior | Cold Start | Use Case |
|------|----------|------------|----------|
| **On-Demand** | Scale to zero, spin up when needed | 10-30s | Standard users, cost-sensitive |
| **Shared Pool** | Pre-warmed pods, claim on session start | ~2-3s | Faster experience, moderate cost |
| **Dedicated** | Always running, background agents | 0s | Power users, scheduled tasks, CI/CD workflows |

## Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        OpenCortex API                           │
│                                                                 │
│  AgenticOrchestrationEngine                                     │
│         │                                                       │
│         ▼                                                       │
│  IWorkspaceManager (interface)                                  │
│    ├── LocalWorkspaceManager     (development - filesystem)    │
│    ├── DockerWorkspaceManager    (local dev with isolation)    │
│    └── KubernetesWorkspaceManager (production)                 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Kubernetes Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    agent-workspaces namespace                   │
│                                                                 │
│  ┌──────────────────┐  ┌──────────────────┐                    │
│  │  user-{id}-pod   │  │  user-{id}-pod   │                    │
│  │                  │  │                  │                    │
│  │  ┌────────────┐  │  │  ┌────────────┐  │                    │
│  │  │    PVC     │  │  │  │    PVC     │  │                    │
│  │  │ /workspace │  │  │  │ /workspace │  │                    │
│  │  └────────────┘  │  │  └────────────┘  │                    │
│  └──────────────────┘  └──────────────────┘                    │
│                                                                 │
│  NetworkPolicy: configurable per user                           │
│  - Default: allow all egress                                    │
│  - Optional: restrict to allowlist                              │
└─────────────────────────────────────────────────────────────────┘
```

### Docker Architecture (Local Development)

```
┌─────────────────────────────────────────────────────────────────┐
│                         Docker Host                             │
│                                                                 │
│  ┌──────────────────┐  ┌──────────────────┐                    │
│  │ opencortex-agent │  │ opencortex-agent │                    │
│  │   -user-{id}     │  │   -user-{id}     │                    │
│  │                  │  │                  │                    │
│  │  -v volume:/work │  │  -v volume:/work │                    │
│  └──────────────────┘  └──────────────────┘                    │
│                                                                 │
│  Network: opencortex-agents (isolated bridge)                   │
└─────────────────────────────────────────────────────────────────┘
```

## Configuration

### ToolsOptions

```csharp
public sealed class ToolsOptions
{
    // Workspace isolation mode
    public string WorkspaceMode { get; set; } = "local"; // local, docker, kubernetes

    // Local mode settings
    public string WorkspaceBasePath { get; set; } = "./workspaces";

    // Container settings (docker/kubernetes)
    public string ContainerImage { get; set; } = "opencortex/agent-runtime:latest";
    public int SessionTimeoutMinutes { get; set; } = 15;

    // Kubernetes settings
    public string KubernetesNamespace { get; set; } = "agent-workspaces";
    public string StorageClassName { get; set; } = "standard";
    public string PvcSizeGi { get; set; } = "10Gi";

    // Docker settings
    public string DockerNetwork { get; set; } = "opencortex-agents";

    // Resource limits
    public string CpuLimit { get; set; } = "1";
    public string MemoryLimit { get; set; } = "1Gi";
    public string CpuRequest { get; set; } = "100m";
    public string MemoryRequest { get; set; } = "256Mi";
}
```

### User Workspace Settings

Stored per-user in database:

```csharp
public sealed class UserWorkspaceSettings
{
    public Guid UserId { get; set; }

    // Network policy
    public WorkspaceNetworkPolicy NetworkPolicy { get; set; } = WorkspaceNetworkPolicy.AllowAll;
    public List<string>? AllowedEgressDomains { get; set; } // When policy is AllowList

    // Tier (future)
    public WorkspaceTier Tier { get; set; } = WorkspaceTier.OnDemand;

    // Shell command execution mode
    public CommandExecutionMode CommandMode { get; set; } = CommandExecutionMode.Auto;
}

public enum WorkspaceNetworkPolicy
{
    AllowAll,      // Full internet access (default)
    AllowList,     // Only allowed domains
    GitHubOnly,    // Only GitHub API and git operations
    Deny           // No egress (internal tools only)
}

public enum WorkspaceTier
{
    OnDemand,      // Scale to zero
    SharedPool,    // Pre-warmed pool
    Dedicated      // Always running
}

public enum CommandExecutionMode
{
    Auto,          // Execute immediately
    Approval       // Require user confirmation
}
```

## Security

### Pod Security Context

```yaml
securityContext:
  runAsNonRoot: true
  runAsUser: 1000
  runAsGroup: 1000
  fsGroup: 1000
  readOnlyRootFilesystem: true
  allowPrivilegeEscalation: false
  seccompProfile:
    type: RuntimeDefault
  capabilities:
    drop: ["ALL"]
```

### Network Policy (Default - Allow All)

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: user-{id}-netpol
spec:
  podSelector:
    matchLabels:
      app: opencortex-agent
      user-id: "{id}"
  policyTypes:
    - Ingress
    - Egress
  ingress: []  # Deny all ingress
  egress:
    - {}  # Allow all egress (default)
```

### Network Policy (Allowlist Example)

```yaml
egress:
  - to:
    - ipBlock:
        cidr: 0.0.0.0/0
        except:
          - 10.0.0.0/8      # Block internal
          - 172.16.0.0/12   # Block internal
          - 192.168.0.0/16  # Block internal
          - 169.254.169.254/32  # Block metadata
    ports:
      - protocol: TCP
        port: 443
  - to:  # Allow DNS
    - namespaceSelector: {}
    ports:
      - protocol: UDP
        port: 53
```

### Secrets Handling

- GitHub PAT injected as environment variable at pod creation
- Secret created per-pod, deleted with pod
- Never logged or exposed in error messages

## Lifecycle

### Pod Lifecycle Events

```
User sends agentic request
         │
         ▼
┌─────────────────────────────────┐
│ Check pod status                │
└─────────────────────────────────┘
         │
    ┌────┴────┐
    │ Exists? │
    └────┬────┘
    No   │   Yes
    ▼    │    ▼
┌────────┴────────┐
│ Create PVC if   │    Pod ready
│ not exists      │        │
└────────┬────────┘        │
         │                 │
         ▼                 │
┌─────────────────┐        │
│ Create Pod      │        │
│ (emit event)    │        │
└────────┬────────┘        │
         │                 │
         ▼                 │
┌─────────────────┐        │
│ Wait for ready  │        │
│ (30s timeout)   │        │
└────────┬────────┘        │
         │                 │
         ▼◄────────────────┘
┌─────────────────────────────────┐
│ Execute tool via exec API       │
│ Reset idle timer                │
└─────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────┐
│ Idle for 15 minutes             │
└─────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────┐
│ Delete Pod (PVC persists)       │
└─────────────────────────────────┘
```

### SSE Events for Workspace Status

```typescript
// New event types
interface WorkspaceProvisioningEvent {
  type: "workspace_provisioning";
  status: "creating_pvc" | "creating_pod" | "waiting_ready";
  message: string;
}

interface WorkspaceReadyEvent {
  type: "workspace_ready";
  podName: string;
  startupTimeMs: number;
}

interface WorkspaceErrorEvent {
  type: "workspace_error";
  error: string;
  retryable: boolean;
}
```

## Implementation Status

### Completed
- [x] `DockerWorkspaceManager` - Docker container lifecycle
- [x] `KubernetesWorkspaceManager` - K8s pod with PVC
- [x] Workspace lifecycle SSE events
- [x] Portal UI workspace status display
- [x] CI/CD integration (build + deploy)
- [x] RBAC and NetworkPolicy
- [x] Resource quotas and limits

### Pending
- [ ] Add `UserWorkspaceSettings` to database
- [ ] API endpoints for workspace settings
- [ ] Portal UI for network policy configuration
- [ ] PVC cleanup for inactive users (30 days)
- [ ] Metrics and observability

## Quick Start

### Automated (CI/CD)

Push to `develop` branch - everything deploys automatically:
1. `build-images-develop.yml` builds `agent-runtime` image
2. `deploy-develop.yml` deploys `k8s/agent-workspaces/` and configures API

### Manual

```bash
# 1. Deploy workspace infrastructure
kubectl apply -k k8s/agent-workspaces/

# 2. Build agent-runtime image
docker build -t ghcr.io/your-org/opencortex/agent-runtime:develop -f infra/docker/agent-runtime.Dockerfile .
docker push ghcr.io/your-org/opencortex/agent-runtime:develop

# 3. Deploy main application (includes workspace config)
kubectl apply -k infra/k8s/overlays/develop
```

### Local Development

```bash
# Use .NET user secrets
cd src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Tools:WorkspaceMode" "docker"  # or "local"
```

## Agent Runtime Image

The `opencortex/agent-runtime` image should include:

```dockerfile
FROM ubuntu:22.04

# Essential tools
RUN apt-get update && apt-get install -y \
    git \
    curl \
    wget \
    jq \
    unzip \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN groupadd -g 1000 agent && \
    useradd -u 1000 -g agent -m -s /bin/bash agent

# Workspace directory
RUN mkdir -p /workspace && chown agent:agent /workspace
VOLUME /workspace
WORKDIR /workspace

USER agent

# Keep container running
CMD ["sleep", "infinity"]
```

## Cost Considerations

| Component | Cost Driver | Optimization |
|-----------|-------------|--------------|
| Pod compute | CPU/memory × uptime | Scale to zero, right-size limits |
| PVC storage | Size × duration | Cleanup inactive users |
| Network egress | Data transfer | User awareness, optional limits |
| Control plane | API calls | Batch operations, caching |

## Future Enhancements

1. **Shared Pool**: Pre-warmed pods for faster cold starts
2. **Dedicated Tier**: Always-on pods for background agents
3. **Scheduled Tasks**: Cron-like agent executions
4. **Webhook Triggers**: GitHub webhooks trigger agent actions
5. **Resource Quotas**: Per-user compute limits
6. **Cost Tracking**: Per-user resource usage metrics
