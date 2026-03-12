# OpenCortex API Dockerfile
# Multi-stage build for optimized production image

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first for better layer caching
COPY OpenCortex.sln ./
COPY src/OpenCortex.Core/OpenCortex.Core.csproj src/OpenCortex.Core/
COPY src/OpenCortex.Persistence.Postgres/OpenCortex.Persistence.Postgres.csproj src/OpenCortex.Persistence.Postgres/
COPY src/OpenCortex.Indexer/OpenCortex.Indexer.csproj src/OpenCortex.Indexer/
COPY src/OpenCortex.Retrieval/OpenCortex.Retrieval.csproj src/OpenCortex.Retrieval/
COPY src/OpenCortex.Api/OpenCortex.Api.csproj src/OpenCortex.Api/

# Restore dependencies
RUN dotnet restore src/OpenCortex.Api/OpenCortex.Api.csproj

# Copy source code
COPY src/OpenCortex.Core/ src/OpenCortex.Core/
COPY src/OpenCortex.Persistence.Postgres/ src/OpenCortex.Persistence.Postgres/
COPY src/OpenCortex.Indexer/ src/OpenCortex.Indexer/
COPY src/OpenCortex.Retrieval/ src/OpenCortex.Retrieval/
COPY src/OpenCortex.Api/ src/OpenCortex.Api/

# Build and publish
RUN dotnet publish src/OpenCortex.Api/OpenCortex.Api.csproj \
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

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "OpenCortex.Api.dll"]
