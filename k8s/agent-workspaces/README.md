# Agent Workspaces Kubernetes Setup

This directory contains Kubernetes manifests for the agent workspace isolation feature.

## Overview

Each user gets an isolated pod with:
- A PersistentVolumeClaim for workspace storage
- Network isolation (deny ingress, configurable egress)
- Resource limits (CPU, memory)
- Automatic cleanup after idle timeout

## Prerequisites

1. A Kubernetes cluster (1.21+)
2. kubectl configured with cluster access
3. A storage class for PVCs (default: `standard`)
4. NetworkPolicy support (most CNIs support this)

## Automated Deployment (CI/CD)

If you're using the GitHub Actions workflows, everything is automatic:

1. **Image Build**: The `build-images-develop.yml` workflow builds and pushes `agent-runtime:develop`
2. **Infrastructure**: The `deploy-develop.yml` workflow applies `k8s/agent-workspaces/`
3. **API Config**: The `patches/api-workspace.yaml` configures the API automatically

Just push to the `develop` branch and everything deploys.

## Manual Deployment

### 1. Deploy the workspace infrastructure

```bash
# From the repository root
kubectl apply -k k8s/agent-workspaces/
```

This creates:
- `agent-workspaces` namespace
- RBAC for the OpenCortex API to manage pods/PVCs
- Default NetworkPolicy (allow egress, deny ingress)
- Resource quotas and limits

### 2. Build and push the agent-runtime image

```bash
# Build the agent runtime image (from repo root)
docker build -t ghcr.io/your-org/opencortex/agent-runtime:develop -f infra/docker/agent-runtime.Dockerfile .

# Push to your registry
docker push ghcr.io/your-org/opencortex/agent-runtime:develop
```

### 3. Configure the OpenCortex API

The configuration is applied via the Kustomize patch in `infra/k8s/overlays/develop/patches/api-workspace.yaml`.

For manual/local development, set these environment variables:

```bash
export OpenCortex__Tools__WorkspaceMode=kubernetes
export OpenCortex__Tools__KubernetesNamespace=agent-workspaces
export OpenCortex__Tools__ContainerImage=ghcr.io/your-org/opencortex/agent-runtime:develop
export OpenCortex__Tools__StorageClassName=standard
export OpenCortex__Tools__PvcSize=10Gi
export OpenCortex__Tools__SessionTimeoutMinutes=15
```

Or use .NET user secrets for local development:

```bash
cd src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Tools:WorkspaceMode" "kubernetes"
dotnet user-secrets set "OpenCortex:Tools:KubernetesNamespace" "agent-workspaces"
dotnet user-secrets set "OpenCortex:Tools:ContainerImage" "ghcr.io/your-org/opencortex/agent-runtime:develop"
```

### 4. Service Account (Automatic in CI/CD)

The API deployment automatically uses `opencortex-workspace-manager` service account via the Kustomize patch. No manual steps required.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    agent-workspaces namespace                   │
│                                                                 │
│  ┌──────────────────┐  ┌──────────────────┐                    │
│  │  agent-{user-id} │  │  agent-{user-id} │                    │
│  │       (Pod)      │  │       (Pod)      │                    │
│  │                  │  │                  │                    │
│  │  ┌────────────┐  │  │  ┌────────────┐  │                    │
│  │  │    PVC     │  │  │  │    PVC     │  │                    │
│  │  │ /workspace │  │  │  │ /workspace │  │                    │
│  │  └────────────┘  │  │  └────────────┘  │                    │
│  └──────────────────┘  └──────────────────┘                    │
│                                                                 │
│  NetworkPolicy: deny ingress, allow egress                      │
│  ResourceQuota: limits total cluster usage                      │
│  LimitRange: limits per-pod resources                          │
└─────────────────────────────────────────────────────────────────┘
```

## Pod Lifecycle

1. **User sends agentic request**
   - API checks if pod exists
   - If not, creates PVC (if needed) and Pod
   - Waits for pod to be ready (up to 60s)

2. **Tool execution**
   - Commands run via `kubectl exec`
   - Results returned to API

3. **Idle timeout** (default 15 min)
   - Pod is deleted
   - PVC is preserved (workspace persists)

4. **Next request**
   - Pod recreated with same PVC
   - Fast startup (PVC already exists)

## Security

- Pods run as non-root (UID 1000)
- No privilege escalation allowed
- All capabilities dropped
- Read-only root filesystem where possible
- Network ingress denied
- Egress configurable per-user

## Customization

### Different storage class

Edit `resource-quota.yaml` or set via API config:

```json
{
  "OpenCortex": {
    "Tools": {
      "StorageClassName": "fast-ssd"
    }
  }
}
```

### Restrict egress

Apply a per-user NetworkPolicy:

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: user-{user-id}-policy
  namespace: agent-workspaces
spec:
  podSelector:
    matchLabels:
      user-id: "{user-id}"
  policyTypes:
    - Egress
  egress:
    - to:
        - ipBlock:
            cidr: 140.82.112.0/20  # GitHub
      ports:
        - port: 443
```

## Troubleshooting

### Pod not starting

```bash
kubectl describe pod agent-{user-id} -n agent-workspaces
kubectl logs agent-{user-id} -n agent-workspaces
```

### PVC not binding

```bash
kubectl get pvc -n agent-workspaces
kubectl describe pvc workspace-{user-id} -n agent-workspaces
```

### API can't create pods

Check RBAC:

```bash
kubectl auth can-i create pods --as=system:serviceaccount:opencortex:opencortex-workspace-manager -n agent-workspaces
```
