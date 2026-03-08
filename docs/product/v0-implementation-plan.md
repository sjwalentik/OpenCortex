# OpenCortex v0 Implementation Plan

## Goal

Deliver the first end-to-end OpenCortex slice for multi-brain, filesystem-backed knowledge retrieval through MCP.

## v0 Scope

- one runtime hosting many brains
- brains configured with local or network filesystem roots
- strict brain isolation with `brainId`
- Postgres plus pgvector as the default backend
- OQL as the agent-facing MCP query surface
- manual and scheduled indexing
- operator-facing admin inspection APIs and a lightweight browser admin shell

## Out Of Scope

- cross-brain retrieval
- collaborative browser editing
- full hosted customer management
- production-grade auth and RBAC
- file watching on NAS as a required path

## Workstreams

### 1. Contracts And Configuration

- [x] create initial solution and project structure
- [x] create starter brain and source-root configuration model
- [x] define core persistence contracts for documents, chunks, links, embeddings, and index runs
- [x] define operator-managed brain registration and validation contracts

### 2. Infrastructure

- [x] add local Docker Compose for Postgres plus pgvector
- [x] add database migrations project or migration workflow
- [x] add local bootstrap scripts

### 3. Indexing

- [x] create indexing planner skeleton
- [x] implement filesystem discovery
- [x] implement Markdown parsing and frontmatter extraction
- [x] implement wiki-link extraction
- [x] implement chunking and embedding pipeline
- [x] persist index outputs to Postgres

### 4. Retrieval

- [x] create OQL parser skeleton
- [x] create retrieval planner skeleton
- [x] define OQL grammar v0 in executable tests
- [x] implement Postgres-backed text and metadata filtering
- [x] implement pgvector similarity retrieval
- [x] implement hybrid ranking and explainability
- [x] add graph-aware retrieval boosts and wiki-link resolution persistence
- [x] add per-signal `ScoreBreakdown` (keyword, semantic, graph) to `RetrievalResultRecord`
- [x] build `Reason` string from actual signal values in C# instead of SQL CASE expressions
- [x] add `ExecutionSummary` to `OqlQueryExecutionResult` (per-signal result counts, score range)

### 5. Surfaces

- [x] create API scaffold with health and brain endpoints
- [x] create MCP scaffold with OQL planning endpoint
- [x] create worker scaffold for scheduled brain indexing
- [x] add operator-facing admin API endpoints for indexing, runs, errors, and querying
- [x] add first web UI shell for admin inspection and smoke testing
- [x] add `/admin/brains/health` endpoint combining brain summaries with latest run state
- [x] fix admin redirect loop (`/admin` → `/admin/` only, no `/admin/` → `/admin/` loop)
- [x] update admin console to use health endpoint with per-brain health chips and run detail
- [x] add graceful error handling in admin console per section (health, brains, runs)
- [x] add admin API endpoints for brain and source root CRUD (`GET/POST/PUT/DELETE /admin/brains/{id}`, `POST/PUT/DELETE /admin/brains/{id}/source-roots/{id}`)
- [x] fix source root orphan accumulation: delete rows no longer in config on every upsert
- [x] soft-retire persisted brains removed from config; surface `IsConfigured` flag in health endpoint
- [x] add Create Brain form, inline Add Source Root form, and Retire button to admin console
- [ ] add authoring UI shell

### 6. Quality

- [x] add initial unit test projects
- [x] add parser, validation, and planner tests
- [x] add `OpenCortex.Integration.Tests` with `InMemoryDocumentQueryStore` for database-free end-to-end coverage
- [x] add 14 integration tests covering keyword/semantic/hybrid ranking, graph boost, metadata filters, score breakdown, wiki-link resolution, limit enforcement, and brain isolation

## Current State

The repository now has:

- a .NET solution with core, indexer, retrieval, API, MCP server, workers, and tests
- starter OpenCortex configuration binding and validation for operator-managed brains
- a working OQL parser, planner, and Postgres-backed executor for single-brain queries
- brain-scoped keyword, semantic, hybrid, and graph-aware retrieval
- deterministic and openai-compatible embedding providers behind a shared abstraction
- filesystem ingestion with frontmatter parsing, heading chunking, wiki-link extraction, and link-edge persistence
- deletion reconciliation plus cleanup of stale chunks, edges, and embeddings on rescans
- index run persistence, run history, and run error inspection endpoints
- full brain and source root CRUD: `GET/POST/PUT/DELETE /admin/brains/{id}` and `POST/PUT/DELETE /admin/brains/{id}/source-roots/{id}`
- source root sync correctness: orphaned rows are deleted on every config upsert; removed brains are soft-retired
- `IsConfigured` flag on `BrainHealthSummary` distinguishes active config-backed brains from retired historical ones
- per-signal `ScoreBreakdown` (keyword, semantic, graph) on every `RetrievalResultRecord`
- `Reason` string built from actual signal values rather than inferred from SQL CASE expressions
- `ExecutionSummary` on `OqlQueryExecutionResult` with per-signal counts and score range
- admin console with Create Brain form, inline Add Source Root form, Retire button, and score breakdown in OQL smoke-test
- `OpenCortex.Integration.Tests` project with `InMemoryDocumentQueryStore` for database-free end-to-end coverage
- 36 tests total across unit, planner, coordinator, and integration layers
- local Postgres plus pgvector compose infrastructure and manual SQL migration workflow

## Remaining v0 Priorities

1. harden MCP tool contracts around OQL execution, brain scoping, and result formatting
2. add the first authoring-surface browsing slice (document listing inside a brain)
3. continue graph-aware retrieval tuning and context-pack shaping
4. add production-readiness work: auth, observability, and operational hardening
