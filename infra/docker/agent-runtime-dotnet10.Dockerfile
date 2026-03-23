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

RUN if ! getent group agent >/dev/null; then groupadd agent; fi && \
    if ! id -u agent >/dev/null 2>&1; then useradd -g agent -m -s /bin/bash agent; fi

RUN git config --system init.defaultBranch main && \
    git config --system user.email "agent@opencortex.local" && \
    git config --system user.name "OpenCortex Agent"

RUN mkdir -p /workspace && chown agent:agent /workspace

VOLUME /workspace
WORKDIR /workspace

USER agent

ENV HOME=/home/agent
ENV PATH="/home/agent/.local/bin:${PATH}"

CMD ["sleep", "infinity"]
