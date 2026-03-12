#!/bin/bash
# Setup script for GitHub Actions self-hosted runner in Kubernetes
#
# Usage:
#   ./setup-runner.sh <github-pat-token> <github-repo-url>
#
# Example:
#   ./setup-runner.sh ghp_xxxxxxxxxxxx https://github.com/your-org/OpenCortex

set -e

GITHUB_TOKEN="${1:?Error: GitHub PAT token required as first argument}"
GITHUB_REPO="${2:-https://github.com/your-org/OpenCortex}"

echo "Setting up GitHub Actions runner for: $GITHUB_REPO"

# Create namespace
kubectl create namespace actions-runner-system --dry-run=client -o yaml | kubectl apply -f -

# Create secret with GitHub token
kubectl create secret generic github-runner-secret \
  --namespace=actions-runner-system \
  --from-literal=github_token="$GITHUB_TOKEN" \
  --dry-run=client -o yaml | kubectl apply -f -

# Update the deployment with the correct repo URL
cat runner-deployment.yaml | sed "s|https://github.com/YOUR_ORG/OpenCortex|$GITHUB_REPO|g" | kubectl apply -f -

echo ""
echo "Runner setup complete!"
echo ""
echo "To check runner status:"
echo "  kubectl get pods -n actions-runner-system"
echo ""
echo "To view runner logs:"
echo "  kubectl logs -n actions-runner-system -l app.kubernetes.io/name=github-runner -f"
echo ""
echo "Next steps:"
echo "1. Verify the runner appears in GitHub Settings > Actions > Runners"
echo "2. Create a 'develop' branch to trigger the deployment workflow"
echo "3. Add required secrets to GitHub repository settings:"
echo "   - DB_CONNECTION_STRING"
echo "   - FIREBASE_PROJECT_ID"
echo "   - FIREBASE_API_KEY"
echo "   - FIREBASE_ADMIN_USER_IDS"
echo "   - FIREBASE_ADMIN_EMAIL_PATTERNS"
echo "   - EMBEDDINGS_API_KEY"
echo "   - STRIPE_SECRET_KEY (optional)"
echo "   - STRIPE_WEBHOOK_SECRET (optional)"
