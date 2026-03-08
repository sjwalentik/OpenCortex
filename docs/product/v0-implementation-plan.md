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
- [ ] add admin API endpoints for brain and source root CRUD
- [ ] add authoring UI shell

### 6. Quality

- [x] add initial unit test projects
- [x] add parser, validation, and planner tests
- [ ] add integration tests for filesystem indexing and retrieval

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
- a `/admin/brains/health` endpoint returning per-brain summaries with latest run status, document counts, and error info
- brain health status driven by latest run only (stale historical runs do not pollute current health)
- a lightweight admin console for browsing brains with health chips, triggering indexing, reviewing runs/errors, and smoke-testing OQL
- graceful error fallback messaging in the admin console when individual API sections fail to load
- local Postgres plus pgvector compose infrastructure and manual SQL migration workflow

## Known Issues

- source root counts shown per brain may not match expected values; the `SourceRootCount` field on `BrainSummary` reflects persisted records, which can diverge from the local config if brains were registered before source roots were configured correctly
- brains must be registered in Postgres before indexing can be triggered via the API; mismatch between config brain IDs and persisted slugs will cause 404s on indexing endpoints
- older index runs with `running` status in history are surfaced in the admin health detail to help operators identify stuck runs, but they do not affect the health chip state

## Remaining v0 Priorities

1. add brain and source root CRUD to the admin API and console
2. resolve source root count discrepancy between config and persisted brain records
3. improve hybrid scoring explainability and result reasoning output
4. continue graph-aware retrieval tuning and context-pack shaping
5. harden MCP tool contracts around the current OQL execution path
6. add deeper end-to-end integration coverage for indexing plus retrieval
7. start the first authoring-surface browsing slice after admin stabilizes
