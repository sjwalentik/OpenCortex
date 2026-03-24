FROM mcr.microsoft.com/dotnet/sdk:10.0-noble

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y --no-install-recommends \
    git \
    curl \
    wget \
    ca-certificates \
    gnupg \
    jq \
    unzip \
    zip \
    tar \
    vim-tiny \
    build-essential \
    python3 \
    python3-pip \
    python3-venv \
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
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

RUN git config --system init.defaultBranch main && \
    git config --system user.email "agent@opencortex.local" && \
    git config --system user.name "OpenCortex Agent"

RUN mkdir -p /workspace && chown 1000:1000 /workspace

VOLUME /workspace
WORKDIR /workspace

USER 1000:1000

ENV HOME=/home/ubuntu
ENV DOTNET_CLI_HOME=/home/ubuntu
ENV PATH="/home/ubuntu/.local/bin:${PATH}"

CMD ["sleep", "infinity"]
