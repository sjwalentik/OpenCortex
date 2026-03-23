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
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y nodejs \
    && npm install -g @openai/codex \
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
