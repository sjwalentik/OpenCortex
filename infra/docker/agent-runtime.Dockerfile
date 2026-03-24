# OpenCortex Agent Runtime
# Isolated environment for executing agent tools (git, shell commands, etc.)

FROM ubuntu:22.04

# Avoid prompts during package installation
ENV DEBIAN_FRONTEND=noninteractive

# Install essential tools
RUN apt-get update && apt-get install -y --no-install-recommends \
    # Version control
    git \
    # Network tools
    curl \
    wget \
    ca-certificates \
    gnupg \
    # JSON processing
    jq \
    # Archive tools
    unzip \
    zip \
    tar \
    # Text processing
    vim-tiny \
    # Build essentials (for compiling dependencies if needed)
    build-essential \
    # Python (commonly needed)
    python3 \
    python3-pip \
    python3-venv \
    # Node.js setup
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg | dd of=/etc/apt/keyrings/githubcli-archive-keyring.gpg \
    && chmod go+r /etc/apt/keyrings/githubcli-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" > /etc/apt/sources.list.d/github-cli.list \
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get update \
    && apt-get install -y gh \
    && apt-get install -y nodejs \
    && npm install -g @openai/codex \
    && npm install -g @anthropic-ai/claude-code \
    # Cleanup
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN groupadd -g 1000 agent && \
    useradd -u 1000 -g agent -m -s /bin/bash agent

# Configure git for the agent user
RUN git config --system init.defaultBranch main && \
    git config --system user.email "agent@opencortex.local" && \
    git config --system user.name "OpenCortex Agent"

# Create workspace directory
RUN mkdir -p /workspace && chown agent:agent /workspace

# Set workspace as volume mount point
VOLUME /workspace
WORKDIR /workspace

# Switch to non-root user
USER agent

# Set environment
ENV HOME=/home/agent
ENV PATH="/home/agent/.local/bin:${PATH}"

# Keep container running (will be exec'd into)
CMD ["sleep", "infinity"]
