# OpenCortex Roadmap

## Product Direction

OpenCortex is a multi-brain memory runtime for AI agents and human knowledge workflows.

The platform hosts many isolated brains behind one runtime. Each brain has its own source roots, indexing lifecycle, retrieval scope, and operational settings.

OpenCortex is intended to support two content models over time:

- filesystem-first brains for open source and self-hosted deployments, where Markdown remains the source of truth on local disks, NAS paths, or shared drives
- managed-content brains for cloud deployments, where OpenCortex becomes the primary editing and storage system of record

Both models share the same runtime concepts, retrieval pipeline, and agent-facing query layer.

## Core Personas

- operator: configures the runtime, storage, indexing, and deployments
- admin: creates brains, manages customers, sets source roots, and monitors index health
- editor: writes and curates Markdown knowledge
- agent: queries knowledge through MCP using OQL

## Product Layers

- core runtime: ingestion, indexing, storage, retrieval, OQL, MCP
- admin surface: brain management, source root management, customer segmentation, index operations
- authoring surface: Markdown browsing, editing, preview, link-aware authoring, and publishing workflows

## Principles

- brains are isolated by default
- OpenCortex owns the query language, not the database vendor
- Postgres plus pgvector is the default backend, not the permanent constraint
- self-hosted and cloud modes share the same conceptual model
- explainable context packs are more important than opaque ranking

## Current Status

- architecture, storage, indexing, retrieval, and MCP foundation are in place
- operator-managed multi-brain filesystem indexing works against Postgres plus pgvector
- OQL supports single-brain retrieval with text, metadata, semantic, hybrid, and graph-aware ranking inputs
- retrieval results now carry a per-signal `ScoreBreakdown` (keyword, semantic, graph) alongside the combined score
- the `Reason` string is built from actual signal values, e.g. `"title match (2.00) + semantic similarity (0.85) + graph boost ×3 (0.45)"`
- `OqlQueryExecutionResult` exposes an `ExecutionSummary` with per-signal result counts and score range
- brain and source root CRUD is fully implemented: `GET/POST/PUT/DELETE /admin/brains/{id}` and `POST/PUT/DELETE /admin/brains/{id}/source-roots/{id}`
- orphaned source root rows are cleaned up on every config sync; brains removed from config are soft-retired
- the admin console surfaces per-brain health chips, retired brain indicators, Create Brain form, and inline Add Source Root form with Retire button
- the API exposes health, brain listing, CRUD, indexing preview/run, index run history, run errors, and query execution
- end-to-end integration tests cover the full pipeline from filesystem discovery through OQL query execution and score breakdown validation without requiring a database
- the next focus is hardening MCP tool contracts and adding the first authoring-surface browsing slice

## Phases

### Phase 0: Product Definition

- lock vocabulary for customer, brain, source root, document, chunk, index run, and context pack
- define v0 scope and non-goals
- confirm filesystem-first and managed-content-first are both first-class future modes

### Phase 1: Architecture Specs

- document the multi-brain platform model
- define the OQL vision and v0 grammar
- define storage abstractions and Postgres default implementation
- define indexing lifecycle and retrieval pipeline
- define MCP surface, admin surface, and authoring surface roadmap

### Phase 2: Domain Contracts

- create the domain model for brains, source roots, documents, chunks, links, embeddings, and index runs
- define brain isolation rules with `brainId` across all core content tables
- define customer segmentation as an optional parent boundary for brains

### Phase 3: Configuration And Brain Registration

- support operator-managed brain definitions first
- allow future admin-created brains through an API and UI
- support local paths, UNC paths, NAS locations, and shared drives as source roots
- define per-brain indexing and retrieval settings

### Phase 4: Storage And Infrastructure

- stand up Postgres and pgvector with Docker Compose
- add migrations and repository/provider interfaces
- implement the default metadata, link, vector, and index-run stores

### Phase 5: Ingestion And Indexing

- scan source roots per brain
- parse Markdown and frontmatter
- extract wiki links and graph relationships
- chunk content and generate embeddings
- persist documents, chunks, edges, and index metadata
- support manual and scheduled indexing first

### Phase 6: Retrieval And OQL

- implement OQL parser, AST, planner, executor, and result formatter
- combine metadata filtering, keyword search, vector search, and graph-aware ranking
- return explainable context packs and ranked results

### Phase 7: MCP Server

- expose brain-scoped tools for listing brains, querying OQL, fetching documents, and building context packs
- keep one brain per query in v0
- defer cross-brain retrieval until explicitly designed

### Phase 8: Admin Surface

- create and manage brains
- assign brains to customers
- manage source roots and indexing settings
- run reindex jobs and inspect status, health, and failures

### Phase 9: Authoring Surface

- browse documents inside a brain
- open, edit, preview, and save Markdown
- support filesystem-backed editing for self-hosted brains
- support managed-content editing for cloud brains
- add future workflows for backlink discovery, review, and publishing

### Phase 10: Security And Hardening

- add auth, roles, and audit trails
- validate filesystem and network source roots
- add observability, failure handling, and operational safeguards

### Phase 11: Packaging And Commercialization

- define self-hosted packaging
- define cloud deployment model
- define customer segmentation, quotas, and operational boundaries for hosted usage

## Milestones

- [x] M1: architecture documentation complete
- [x] M2: Postgres schema and core contracts complete
- [x] M3: single-brain indexing works end to end
- [x] M4: multi-brain indexing works with strict isolation
- [x] M5: OQL query path works end to end
- [~] M6: MCP server exposes stable brain-scoped tools
- [x] M7: admin API and UI support brain management, per-brain health, and CRUD
- [ ] M8: authoring UI supports Markdown editing
- [ ] M9: hosted/cloud operational model is ready

## Near-Term Deliverables

- harden MCP tool contracts around OQL execution, brain scoping, and result formatting
- prepare the first authoring-surface slice for document browsing inside a brain
- continue graph-aware retrieval tuning and context-pack shaping
- add production-readiness work: auth, observability, and operational hardening
