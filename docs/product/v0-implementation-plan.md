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
- basic admin-ready brain model, without a full admin UI yet

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
- [ ] define admin-facing brain registration contracts

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
- [ ] define OQL grammar v0 in executable tests
- [x] implement Postgres-backed text and metadata filtering
- [x] implement pgvector similarity retrieval
- [x] implement hybrid ranking and explainability

### 5. Surfaces

- [x] create API scaffold with health and brain endpoints
- [x] create MCP scaffold with OQL planning endpoint
- [x] create worker scaffold for scheduled brain indexing
- [ ] add admin API endpoints for brain management
- [ ] add first web UI shell for admin and authoring work

### 6. Quality

- [x] add initial unit test projects
- [x] add parser, validation, and planner tests
- [ ] add integration tests for filesystem indexing and retrieval

## Recommended Build Order

1. finalize OQL grammar and parser tests
2. define Postgres schema and migrations
3. implement filesystem indexing for one brain
4. implement retrieval over one brain
5. promote to multi-brain execution and worker scheduling
6. add admin API and UI shell

## Current Scaffold Outcome

The repository now has:

- a .NET solution with core, indexer, retrieval, API, MCP server, workers, and tests
- starter OpenCortex configuration binding and validation
- a basic OQL parser and retrieval planner skeleton
- core persistence interfaces and a Postgres persistence scaffold
- a first filesystem ingestion slice with discovery, frontmatter parsing, wiki-link extraction, and chunk generation
- a first Postgres-backed retrieval slice for brain-scoped keyword and metadata search
- deterministic local embeddings and initial pgvector-backed semantic retrieval plumbing
- embedding provider abstraction, openai-compatible provider support, and graph-aware hybrid ranking boosts
- starter API and MCP endpoints
- a first manual API path for ingestion preview and indexing execution
- local Postgres plus pgvector compose infrastructure
- plain SQL migrations and a manual migration workflow for secret-backed deployments
