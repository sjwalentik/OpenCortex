# OpenCortex .NET Upgrade Path

## Purpose

This document records the upgrade path OpenCortex used to move from the prior `.NET 8` baseline to a repo state that builds and tests cleanly on `.NET 10`.

It started as a two-phase plan:

1. unblock local builds on machines that only have the `.NET 10 SDK`
2. perform a real repo migration from `net8.0` to `net10.0`

Both of those steps are now complete on `feature/upgrade-dotnet-10`.

## Current State

As of March 24, 2026 on `feature/upgrade-dotnet-10`:

- [`global.json`](/workspace/OpenCortex/global.json) uses SDK `10.0.100` with `rollForward: latestFeature`
- all app, library, and test projects target `net10.0`
- `dotnet restore OpenCortex.sln` succeeds
- `dotnet build OpenCortex.sln` succeeds
- `dotnet test OpenCortex.sln --no-build` succeeds

Current project layout:

- app projects: `OpenCortex.Api`, `OpenCortex.McpServer`, `OpenCortex.Portal`, `OpenCortex.Workers`, `OpenCortex.AppHost`
- library projects: `OpenCortex.Core`, `OpenCortex.Indexer`, `OpenCortex.Retrieval`, `OpenCortex.Persistence.Postgres`
- test projects: `OpenCortex.Api.Tests`, `OpenCortex.Core.Tests`, `OpenCortex.Indexer.Tests`, `OpenCortex.Integration.Tests`, `OpenCortex.McpServer.Tests`, `OpenCortex.Retrieval.Tests`

Version-coupled areas still worth tracking:

- ASP.NET packages pinned to `8.0.x`
- `Microsoft.Extensions.Hosting` pinned to `8.0.1`
- `Npgsql` pinned to `8.0.3`
- Aspire AppHost packages pinned to `8.2.2`
- test stack pinned to older `Microsoft.NET.Test.Sdk` and `xunit` versions
- `ModelContextProtocol.AspNetCore` pinned to `1.1.0`

## What Worked

The migration path turned out to be simpler than expected:

1. update [`global.json`](/workspace/OpenCortex/global.json) so the repo uses SDK `10.x`
2. stabilize the ASP.NET test-host path for SDK 10
3. retarget all projects from `net8.0` to `net10.0`
4. rerun restore, build, and the full test suite

Observed validation on March 24, 2026:

- `dotnet restore OpenCortex.sln`: passed
- `dotnet build OpenCortex.sln`: passed
- `dotnet test OpenCortex.sln --no-build`: passed

The current package set restored and compiled on `net10.0` without requiring immediate package-version edits.

## Remaining Work

The branch has crossed the main migration threshold. Remaining work is cleanup and runtime validation, not framework retargeting.

### Runtime Smoke Tests

Still validate the main entry points directly:

- `dotnet run --project src/OpenCortex.Api`
- `dotnet run --project src/OpenCortex.McpServer`
- `dotnet run --project src/OpenCortex.Portal`
- `dotnet run --project src/OpenCortex.AppHost`

Capture:

- startup warnings
- runtime exceptions
- HTTPS and user-secret behavior
- AppHost orchestration behavior on the current toolchain

### Package Hygiene

Even though restore/build/test are green, package review still matters.

Priority areas:

- Aspire packages in [`OpenCortex.AppHost.csproj`](/workspace/OpenCortex/src/OpenCortex.AppHost/OpenCortex.AppHost.csproj)
- ASP.NET packages in [`OpenCortex.Api.csproj`](/workspace/OpenCortex/src/OpenCortex.Api/OpenCortex.Api.csproj)
- `ModelContextProtocol.AspNetCore` in [`OpenCortex.McpServer.csproj`](/workspace/OpenCortex/src/OpenCortex.McpServer/OpenCortex.McpServer.csproj)
- `Npgsql` in [`OpenCortex.Persistence.Postgres.csproj`](/workspace/OpenCortex/src/OpenCortex.Persistence.Postgres/OpenCortex.Persistence.Postgres.csproj)
- test SDK and xUnit packages across `tests/`

Current observation:

- `OpenCortex.AppHost` emits a transitive `NU1902` warning for `KubernetesClient 15.0.1` during restore/build

That warning should be reviewed as part of the Aspire/AppHost dependency chain before calling the upgrade fully hardened.

### Version Management

The repo still repeats `net10.0` and package versions across many project files.

Recommended follow-up:

- introduce a shared `Directory.Build.props` for common framework and compiler settings
- consider `Directory.Packages.props` for package version management

This is not required for the migration to work, but it will make future upgrades smaller and easier to review.

### Documentation And Delivery Assumptions

After the retarget, update the remaining delivery surfaces that may still assume `.NET 8`:

- CI workflow SDK selection
- container images or deployment base images
- any bootstrap scripts or environment docs that still describe `.NET 8`

## Verification Matrix

Completed on this branch:

- solution restore succeeds
- solution build succeeds
- full test suite succeeds

Still recommended before merge:

- API starts and core endpoints respond
- portal starts and can reach the API
- MCP server starts and serves expected routes
- workers start without framework/runtime errors
- Aspire AppHost starts and launches the expected local services
- local hosted setup remains workable with Postgres and current secrets flow

Recommended functional smoke tests:

- sign in through the portal
- create and save a managed-content document
- view preview/history flows
- create an MCP token
- run an indexing flow
- execute a retrieval query

## Risks

### Aspire/AppHost

This remains the area most likely to need coordinated package upgrades or runtime follow-up rather than further target-framework work.

### Hidden Runtime Differences

Compile and test success are strong signals, but authentication, hosting, TLS behavior, JSON serialization defaults, and ASP.NET middleware behavior should still be validated at runtime.

### Docs And CI Drift

If the repo targets `.NET 10` but CI, containers, or setup docs remain on `.NET 8`, the team will keep rediscovering false failures.

## Suggested Next Steps

1. keep the current `net10.0` retarget in place
2. run runtime smoke tests for API, MCP server, portal, workers, and AppHost
3. review Aspire/AppHost package updates and the transitive `KubernetesClient` warning
4. update CI, containers, and deployment assumptions to `.NET 10`
5. optionally centralize shared framework and package version management
