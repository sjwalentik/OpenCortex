# OpenCortex

OpenCortex is a Markdown-first memory runtime for AI agents.

It is designed to turn human-maintained knowledge into an agent-accessible memory layer by combining linked Markdown documents, structural graph relationships, semantic retrieval, and a single MCP interface.

## Status

OpenCortex is in active implementation. The core runtime, admin surface, hosted auth, tenant APIs, managed-content storage, MCP token auth, and the first customer portal slices are all live in-repo.

The primary product and architecture plan now lives in-repo under `docs/product/` and `docs/architecture/`.

Private NAS or Obsidian notes can still hold draft thinking and working notes, but the repo docs are the shareable implementation baseline.

If you want to keep local document paths near the repo without committing them, use `opencortex.local.json` or files under `.opencortex/`. See `opencortex.local.example.json` for a simple example.

## What OpenCortex Aims To Provide

- Markdown as the source of truth for durable knowledge
- Linked-document graph traversal using wiki-style relationships
- Embeddings and hybrid retrieval for semantic context discovery
- Explainable context packs instead of opaque search results
- A single MCP server interface for agent access

## Planned Repository Shape

```text
OpenCortex/
  docs/
  src/
  tests/
  knowledge/
  infra/
  scripts/
```

## Current Bootstrap Artifacts

- product roadmap: `docs/product/roadmap.md`
- v0 implementation plan: `docs/product/v0-implementation-plan.md`
- architecture docs: `docs/architecture/`
- SaaS planning reduced into repo docs for auth, tenancy, billing, managed content, and MCP security
- solution scaffold: `OpenCortex.sln`
- local Postgres compose: `infra/compose/docker-compose.yml`
- manual Postgres setup guide: `docs/operations/manual-postgres-setup.md`
- hosted workspace local setup guide: `docs/operations/hosted-local-setup.md`

## Initial Architecture Direction

- `.NET / C#` for the runtime and MCP server
- `Postgres` as the primary metadata store
- `pgvector` for embeddings
- Markdown parsing, chunking, link extraction, and indexing pipelines
- Hybrid retrieval that combines keyword, semantic, and graph signals
- Docker-based local development infrastructure

## Local Development Bootstrap

```bash
docker compose -f infra/compose/docker-compose.yml up -d
dotnet build OpenCortex.sln
dotnet test OpenCortex.sln
dotnet run --project src/OpenCortex.Api
```

## Aspire Local Orchestration

To start the local .NET services together with the Aspire dashboard:

```bash
dotnet run --project src/OpenCortex.AppHost
```

This AppHost starts:

- `src/OpenCortex.Api`
- `src/OpenCortex.McpServer`
- `src/OpenCortex.Workers`

The local Aspire dashboard opens at `http://127.0.0.1:18888`.

The AppHost does not provision Postgres yet, so start the local database separately first:

```bash
docker compose -f infra/compose/docker-compose.yml up -d
```

For secret-backed or cluster-managed databases, keep the real connection string in user secrets or deployment secrets and apply SQL migrations manually from `infra/postgres/migrations/`.

Embedding provider settings also live under `OpenCortex:Embeddings`. The default open source setup uses the deterministic provider, while hosted or operator-managed deployments can switch to an `openai-compatible` endpoint with secrets stored outside the repo.

If you switch embedding models, keep `OpenCortex:Embeddings:Dimensions` aligned with the Postgres `pgvector` column size. The initial migration creates `opencortex.embeddings.vector` as `vector(1536)`, so `768`-dimension models such as `nomic-embed-text` require a matching schema change before indexing.

Once the API is running, you can inspect indexing operations through `GET /indexing/runs`, `GET /indexing/runs?brainId=<brainId>`, `GET /indexing/runs/<indexRunId>`, and `GET /indexing/runs/<indexRunId>/errors`.

Filesystem indexing now reconciles deletions per source root, so Markdown files removed from the configured knowledge roots are marked deleted in Postgres and excluded from retrieval.

During each index run, stale chunks, link edges, and embeddings for rescanned documents are also cleaned up so changed documents do not leave old retrieval artifacts behind.

The API now serves a lightweight admin console at `/admin/` for operator/debug workflows such as browsing brains, triggering indexing, inspecting run history/errors, and smoke-testing OQL queries from the browser.

Customer-facing workspace flows now live in the separate `OpenCortex.Portal` project, with the React portal serving as the primary shell at `/app`. Legacy root paths now redirect into the React portal for compatibility.

The portal direction is now active rather than aspirational: React + TypeScript is the customer shell. Tiptap for rich document editing and React Flow for graph-aware interaction remain the next major frontend steps. See `docs/architecture/frontend-portal-direction.md`.

The current portal is split into dedicated views for:

- Sign In
- Documents
- Account
- Usage
- Tools

Managed-content authoring is now path-aware, so documents can use filenames like `daily/2026-03-10` and render in a folder-style document rail instead of a flat list.

The portal Tools view can now show live workspace context, smoke-test retrieval, inspect ranked snippets, fetch the full stored document behind a result, and link to the published MCP tool manifest without leaving the diagnostics surface.

For MCP write flows, agents should prefer `save_document` for create/update and can use `delete_document` by canonical path for removal. Both path-first tools can infer the target brain when the workspace has exactly one active managed-content brain.

## Licensing

OpenCortex is licensed under `AGPL-3.0`.

That keeps the code open while requiring network-hosted modifications to remain available under the same license.

## Contributions

OpenCortex is currently limiting outside code contributions while the architecture, licensing, and commercialization strategy are still being defined.

See `CONTRIBUTING.md` for the current contribution policy and `CLA.md` for the contributor assignment terms expected for accepted contributions.

