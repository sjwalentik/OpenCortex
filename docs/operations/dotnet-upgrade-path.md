# OpenCortex .NET Upgrade Path

## Purpose

This document captures a practical upgrade path for OpenCortex from the current `.NET 8` baseline to a repo state that can build and run cleanly on `.NET 10`.

It separates two different goals:

1. unblock local builds on machines that only have the `.NET 10 SDK`
2. perform a real repo migration from `net8.0` to `net10.0`

Those are related, but they are not the same change.

## Current State

As of March 24, 2026:

- the repo is pinned by [`global.json`](/workspace/OpenCortex/global.json) to SDK `8.0.100`
- all app, library, and test projects currently target `net8.0`
- the solution can build under SDK `10.0.201` when the `global.json` pin is bypassed
- the main blocker for normal local builds on this machine is the SDK pin, not an immediate compile incompatibility

Current project layout:

- app projects: `OpenCortex.Api`, `OpenCortex.McpServer`, `OpenCortex.Portal`, `OpenCortex.Workers`, `OpenCortex.AppHost`
- library projects: `OpenCortex.Core`, `OpenCortex.Indexer`, `OpenCortex.Retrieval`, `OpenCortex.Persistence.Postgres`
- test projects: `OpenCortex.Api.Tests`, `OpenCortex.Core.Tests`, `OpenCortex.Indexer.Tests`, `OpenCortex.Integration.Tests`, `OpenCortex.McpServer.Tests`, `OpenCortex.Retrieval.Tests`

Version-coupled areas already visible in the repo:

- ASP.NET packages pinned to `8.0.x`
- `Microsoft.Extensions.Hosting` pinned to `8.0.1`
- `Npgsql` pinned to `8.0.3`
- Aspire AppHost packages pinned to `8.2.2`
- test stack pinned to older `Microsoft.NET.Test.Sdk` and `xunit` versions
- `ModelContextProtocol.AspNetCore` pinned to `1.1.0`

## Recommendation

Use a two-phase plan.

### Phase 1: Unblock SDK 10 Builds Without Retargeting

Goal:

- keep the repo targeting `net8.0`
- allow development on machines that only have SDK `10.x`

Why:

- this is the lowest-risk path
- it removes the immediate local build friction
- it avoids mixing "tooling unblocked" with "framework migration"

Proposed changes:

- update [`global.json`](/workspace/OpenCortex/global.json) so the repo can use an installed SDK `10.x`
- verify `dotnet build` and `dotnet test` from the repo root
- update local setup docs that currently state ".NET 8 SDK" as a hard requirement

Options for `global.json` handling:

- remove `global.json` entirely if strict SDK pinning is not important
- update the SDK version to a `10.0.x` baseline
- relax the pinning strategy if the team wants to allow newer feature bands intentionally

Exit criteria:

- `dotnet build OpenCortex.sln` succeeds from the repo root on SDK `10.x`
- `dotnet test OpenCortex.sln` succeeds from the repo root on SDK `10.x`
- README and setup docs no longer direct developers to install only `.NET 8`

Observed validation on March 24, 2026:

- `dotnet build OpenCortex.sln` succeeds from the repo root on SDK `10.0.201`
- `dotnet test OpenCortex.sln` does not yet pass from the repo root on SDK `10.0.201`
- current failing areas are `OpenCortex.McpServer.Tests` and `OpenCortex.Api.Tests`
- the failures are in ASP.NET TestHost JSON response writing with `System.InvalidOperationException: The PipeWriter 'ResponseBodyPipeWriter' does not implement PipeWriter.UnflushedBytes.`

Implication:

- changing `global.json` is enough to unblock local builds on SDK 10
- additional test-infrastructure or ASP.NET package work is still needed before Phase 1 can be considered fully complete

### Phase 2: Retarget The Repo To `net10.0`

Goal:

- move project target frameworks from `net8.0` to `net10.0`
- align dependent packages and runtime behavior with that move

Why this is a separate phase:

- compile success under SDK 10 does not prove runtime support on `net10.0`
- package compatibility, AppHost behavior, tests, and deployment assumptions all need validation

## Detailed Work Plan

### Step 1: Baseline The Repo Before Any Migration

Record the current baseline on the branch:

- `dotnet --info`
- `dotnet build OpenCortex.sln`
- `dotnet test OpenCortex.sln`
- `dotnet run --project src/OpenCortex.Api`
- `dotnet run --project src/OpenCortex.McpServer`
- `dotnet run --project src/OpenCortex.Portal`
- `dotnet run --project src/OpenCortex.AppHost`

Capture:

- current warnings
- current failing or flaky tests
- any package restore warnings
- whether Aspire AppHost runs correctly on the current toolchain

Reason:

- without a baseline, the migration will blur existing issues with upgrade regressions

### Step 2: Centralize Version Management

Before retargeting, reduce version sprawl.

Recommended refactor:

- introduce a shared `Directory.Build.props` for common `TargetFramework`, nullable, implicit usings, and common compiler settings
- consider `Directory.Packages.props` for package version management

Why:

- the repo currently repeats `net8.0` and package versions across many project files
- centralization makes the migration smaller, more reviewable, and easier to maintain

Minimum acceptable outcome:

- one place for target framework management
- one place for package version management, or at least a clear package inventory

### Step 3: Audit Package Compatibility For `.NET 10`

Review each package for `.NET 10` support and decide whether to:

- keep version
- upgrade version
- replace package
- isolate risk and defer

Priority packages:

- Aspire packages in [`OpenCortex.AppHost.csproj`](/workspace/OpenCortex/src/OpenCortex.AppHost/OpenCortex.AppHost.csproj)
- ASP.NET packages in [`OpenCortex.Api.csproj`](/workspace/OpenCortex/src/OpenCortex.Api/OpenCortex.Api.csproj)
- `ModelContextProtocol.AspNetCore` in [`OpenCortex.McpServer.csproj`](/workspace/OpenCortex/src/OpenCortex.McpServer/OpenCortex.McpServer.csproj)
- `Npgsql` in [`OpenCortex.Persistence.Postgres.csproj`](/workspace/OpenCortex/src/OpenCortex.Persistence.Postgres/OpenCortex.Persistence.Postgres.csproj)
- test SDK and xUnit packages across `tests/`

Expected risk ranking:

- highest risk: Aspire package set
- medium risk: MCP package and test stack
- lower risk: core libraries that do not depend on framework-hosting features

### Step 4: Retarget Libraries First

Retarget the non-host libraries first:

- `OpenCortex.Core`
- `OpenCortex.Indexer`
- `OpenCortex.Retrieval`
- `OpenCortex.Persistence.Postgres`

Why:

- they should expose compatibility issues with less host/runtime noise
- this reduces the blast radius before touching the web and orchestration entry points

Validation:

- build only the libraries
- run unit tests covering those libraries

### Step 5: Retarget Service Projects

Retarget the service and app entry points next:

- `OpenCortex.Api`
- `OpenCortex.McpServer`
- `OpenCortex.Portal`
- `OpenCortex.Workers`
- `OpenCortex.AppHost`

Validation:

- local build
- local run
- startup logs reviewed for warnings or binding/runtime errors
- HTTP smoke tests against API and portal routes
- MCP endpoint smoke test
- worker startup smoke test
- Aspire AppHost smoke test

### Step 6: Upgrade The Test Stack

Retarget tests after the main projects compile cleanly.

Actions:

- update `Microsoft.NET.Test.Sdk`
- review xUnit package versions
- confirm `Microsoft.AspNetCore.Mvc.Testing` version alignment
- run the full test suite on SDK 10 with projects targeting `net10.0`

Reason:

- stale test infrastructure often becomes the noisiest part of framework migrations

### Step 7: Update Documentation And Delivery Assumptions

After migration, update:

- [`README.md`](/workspace/OpenCortex/README.md)
- [`docs/operations/hosted-local-setup.md`](/workspace/OpenCortex/docs/operations/hosted-local-setup.md)
- any deployment manifests, container images, CI workflows, or bootstrap scripts that assume `.NET 8`

Checks:

- local setup says `.NET 10 SDK`
- CI uses `.NET 10`
- any Dockerfiles or base images align with `.NET 10`

## Verification Matrix

Minimum verification before calling the migration complete:

- solution restore succeeds
- solution build succeeds
- full test suite succeeds
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

This is the area most likely to need coordinated package upgrades rather than a simple target framework edit.

### Test Infrastructure

Older `Microsoft.NET.Test.Sdk` and `xunit` pins may still compile, but they are not the place to be conservative during a framework migration. Expect package updates here.

### Hidden Runtime Differences

Compile success is not enough. Authentication, hosting, TLS behavior, JSON serialization defaults, and ASP.NET middleware behavior should be validated at runtime.

### Docs And CI Drift

If the repo is migrated but setup docs and CI remain on `.NET 8`, the team will keep rediscovering false failures.

## Suggested Delivery Sequence

If the goal is fast progress with low disruption:

1. change `global.json` to unblock SDK 10 builds while staying on `net8.0`
2. verify build, test, and local run paths
3. centralize target framework and package version management
4. perform package compatibility audit
5. retarget libraries
6. retarget apps and AppHost
7. retarget and refresh tests
8. update docs and CI

## Decision Gate

Before starting Phase 2, answer these explicitly:

- do we only need to build on machines with SDK 10, or do we want production/runtime support on `net10.0`?
- do we want a single-step migration, or a short-lived dual-support period?
- are we willing to upgrade Aspire and test packages as part of the same branch?
- do we want central package management first, or are we comfortable migrating with repeated version edits?

## Recommended Next Action

Start with Phase 1.

That gives the team immediate value and isolates the real migration work from the current SDK pin problem. Once that is merged, do the actual `net10.0` retarget as a separate branch with package audit, runtime smoke tests, and documentation updates included in scope.
