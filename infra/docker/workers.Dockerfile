# OpenCortex Workers Dockerfile
# Multi-stage build for optimized production image

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first for better layer caching
COPY OpenCortex.sln ./
COPY src/OpenCortex.Core/OpenCortex.Core.csproj src/OpenCortex.Core/
COPY src/OpenCortex.Persistence.Postgres/OpenCortex.Persistence.Postgres.csproj src/OpenCortex.Persistence.Postgres/
COPY src/OpenCortex.Indexer/OpenCortex.Indexer.csproj src/OpenCortex.Indexer/
COPY src/OpenCortex.Retrieval/OpenCortex.Retrieval.csproj src/OpenCortex.Retrieval/
COPY src/OpenCortex.Workers/OpenCortex.Workers.csproj src/OpenCortex.Workers/

# Restore dependencies. Retry transient NuGet failures on CI runners.
RUN for attempt in 1 2 3; do \
      dotnet restore src/OpenCortex.Workers/OpenCortex.Workers.csproj --disable-parallel && exit 0; \
      if [ "$attempt" -eq 3 ]; then exit 1; fi; \
      sleep $((attempt * 10)); \
    done

# Copy source code
COPY src/OpenCortex.Core/ src/OpenCortex.Core/
COPY src/OpenCortex.Persistence.Postgres/ src/OpenCortex.Persistence.Postgres/
COPY src/OpenCortex.Indexer/ src/OpenCortex.Indexer/
COPY src/OpenCortex.Retrieval/ src/OpenCortex.Retrieval/
COPY src/OpenCortex.Workers/ src/OpenCortex.Workers/

# Build and publish
RUN dotnet publish src/OpenCortex.Workers/OpenCortex.Workers.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
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

# Set environment variables
ENV DOTNET_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "OpenCortex.Workers.dll"]
