# syntax=docker/dockerfile:1.7
# OpenCortex MCP Server Dockerfile
# Multi-stage build for optimized production image

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first for better layer caching
COPY OpenCortex.sln ./
COPY src/OpenCortex.Core/OpenCortex.Core.csproj src/OpenCortex.Core/
COPY src/OpenCortex.Conversations/OpenCortex.Conversations.csproj src/OpenCortex.Conversations/
COPY src/OpenCortex.Persistence.Postgres/OpenCortex.Persistence.Postgres.csproj src/OpenCortex.Persistence.Postgres/
COPY src/OpenCortex.Indexer/OpenCortex.Indexer.csproj src/OpenCortex.Indexer/
COPY src/OpenCortex.Orchestration/OpenCortex.Orchestration.csproj src/OpenCortex.Orchestration/
COPY src/OpenCortex.Providers.Abstractions/OpenCortex.Providers.Abstractions.csproj src/OpenCortex.Providers.Abstractions/
COPY src/OpenCortex.Retrieval/OpenCortex.Retrieval.csproj src/OpenCortex.Retrieval/
COPY src/OpenCortex.Tools/OpenCortex.Tools.csproj src/OpenCortex.Tools/
COPY src/OpenCortex.Tools.Memory/OpenCortex.Tools.Memory.csproj src/OpenCortex.Tools.Memory/
COPY src/OpenCortex.McpServer/OpenCortex.McpServer.csproj src/OpenCortex.McpServer/

# Restore dependencies. Cache NuGet state and retry transient CI network failures.
RUN --mount=type=cache,target=/root/.nuget/packages \
    --mount=type=cache,target=/root/.local/share/NuGet/v3-cache \
    for attempt in 1 2 3; do \
      NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT=1 \
      NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS=1000 \
      dotnet restore src/OpenCortex.McpServer/OpenCortex.McpServer.csproj --disable-parallel && exit 0; \
      if [ "$attempt" -eq 3 ]; then exit 1; fi; \
      sleep $((attempt * 10)); \
    done

# Copy source code
COPY src/OpenCortex.Core/ src/OpenCortex.Core/
COPY src/OpenCortex.Conversations/ src/OpenCortex.Conversations/
COPY src/OpenCortex.Persistence.Postgres/ src/OpenCortex.Persistence.Postgres/
COPY src/OpenCortex.Indexer/ src/OpenCortex.Indexer/
COPY src/OpenCortex.Orchestration/ src/OpenCortex.Orchestration/
COPY src/OpenCortex.Providers.Abstractions/ src/OpenCortex.Providers.Abstractions/
COPY src/OpenCortex.Retrieval/ src/OpenCortex.Retrieval/
COPY src/OpenCortex.Tools/ src/OpenCortex.Tools/
COPY src/OpenCortex.Tools.Memory/ src/OpenCortex.Tools.Memory/
COPY src/OpenCortex.McpServer/ src/OpenCortex.McpServer/

# Build and publish. The restore will be fast if packages are already cached.
# Note: We intentionally don't use --no-restore because the NuGet cache mounts
# are ephemeral in CI (GHA layer cache doesn't persist mount contents).
RUN --mount=type=cache,target=/root/.nuget/packages \
    --mount=type=cache,target=/root/.local/share/NuGet/v3-cache \
    dotnet publish src/OpenCortex.McpServer/OpenCortex.McpServer.csproj \
    -c Release \
    -o /app/publish \
    -p:UseAppHost=false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN groupadd -r opencortex && useradd -r -g opencortex opencortex

# Copy published output
COPY --from=build /app/publish .

# Set ownership
RUN chown -R opencortex:opencortex /app

# Switch to non-root user
USER opencortex

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "OpenCortex.McpServer.dll"]
