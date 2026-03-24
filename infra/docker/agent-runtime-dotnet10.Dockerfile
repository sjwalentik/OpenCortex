FROM mcr.microsoft.com/dotnet/sdk:10.0-noble

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y --no-install-recommends \
    git \
    curl \
    wget \
    ca-certificates \
    jq \
    unzip \
    zip \
    tar \
    vim-tiny \
    build-essential \
    python3 \
    python3-pip \
    python3-venv \
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y nodejs \
    && npm install -g @openai/codex \
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
