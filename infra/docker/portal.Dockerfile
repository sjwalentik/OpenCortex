# OpenCortex Portal Dockerfile
# Multi-stage build with React frontend and ASP.NET backend

# Stage 1: Build React frontend
FROM node:20-alpine AS frontend-build
WORKDIR /frontend

# Copy package files for better caching
COPY src/OpenCortex.Portal/Frontend/package*.json ./

# Install dependencies
RUN npm ci

# Copy frontend source
COPY src/OpenCortex.Portal/Frontend/ ./

# Build production bundle
RUN npm run build

# Stage 2: Build .NET backend
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /src

# Copy solution and project files
COPY OpenCortex.sln ./
COPY src/OpenCortex.Core/OpenCortex.Core.csproj src/OpenCortex.Core/
COPY src/OpenCortex.Portal/OpenCortex.Portal.csproj src/OpenCortex.Portal/

# Restore dependencies
RUN dotnet restore src/OpenCortex.Portal/OpenCortex.Portal.csproj

# Copy source code
COPY src/OpenCortex.Core/ src/OpenCortex.Core/
COPY src/OpenCortex.Portal/ src/OpenCortex.Portal/

# Copy frontend build output to wwwroot (Vite outputs to ../wwwroot/app relative to Frontend dir)
COPY --from=frontend-build /wwwroot/app src/OpenCortex.Portal/wwwroot/app/

# Build and publish
RUN dotnet publish src/OpenCortex.Portal/OpenCortex.Portal.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    -p:UseAppHost=false

# Stage 3: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN groupadd -r opencortex && useradd -r -g opencortex opencortex

# Copy published output
COPY --from=backend-build /app/publish .

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

ENTRYPOINT ["dotnet", "OpenCortex.Portal.dll"]
