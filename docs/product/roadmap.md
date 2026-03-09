# OpenCortex Roadmap

## Product Direction

OpenCortex is a multi-brain memory runtime for AI agents and human knowledge workflows.

The platform hosts many isolated brains behind one runtime. Each brain has its own source roots, indexing lifecycle, retrieval scope, and operational settings.

OpenCortex supports two content models:

- **Filesystem-first brains** for open source and self-hosted deployments, where Markdown remains the source of truth on local disks, NAS paths, or shared drives
- **Managed-content brains** for cloud deployments, where OpenCortex becomes the primary editing and storage system of record

Both models share the same runtime concepts, retrieval pipeline, and agent-facing query layer.

## Deployment Models

### Self-Hosted / Open Source

- operator configures brain source roots pointing at local paths, NAS, or UNC shares
- filesystem is the canonical source of truth
- AGPL-3.0 licensed core
- no per-user billing or auth infrastructure required

### Hosted SaaS / Cloud

- users sign up, create brains, and edit Markdown in the browser
- OpenCortex manages document storage and indexing
- individual subscriptions (free and pro) with Stripe-powered billing
- team and enterprise plans with shared brains and workspace membership
- AI agents connect via MCP using personal API tokens

## Core Personas

- **operator**: configures the runtime, storage, indexing, and deployments (self-hosted)
- **admin**: creates brains, manages customers, sets source roots, monitors index health
- **user**: signs up for hosted cloud, writes and retrieves knowledge, connects AI agents
- **editor**: writes and curates Markdown knowledge (hosted or self-hosted)
- **agent**: queries knowledge through MCP using OQL

## Product Layers

- **core runtime**: ingestion, indexing, storage, retrieval, OQL, MCP
- **operator/admin surface**: brain management, source root management, customer segmentation, index operations
- **tenant API**: user accounts, document CRUD, billing, API token management
- **authoring surface**: browser Markdown editor, document browser, wiki-link navigation
- **MCP surface**: brain-scoped OQL tools, accessible via personal API tokens

## Principles

- brains are isolated by default
- OpenCortex owns the query language, not the database vendor
- Postgres plus pgvector is the default backend, not the permanent constraint
- self-hosted and cloud modes share the same conceptual model
- explainable context packs are more important than opaque ranking
- filesystem mode and managed-content mode are first-class equals
- agents are first-class citizens alongside human users

## Current Status

- architecture, storage, indexing, retrieval, and MCP foundation are in place
- operator-managed multi-brain filesystem indexing works against Postgres plus pgvector
- OQL supports single-brain retrieval with text, metadata, semantic, hybrid, and graph-aware ranking
- retrieval results carry a per-signal `ScoreBreakdown` (keyword, semantic, graph) alongside the combined score
- the `Reason` string is built from actual signal values, e.g. `"title match (2.00) + semantic similarity (0.85) + graph boost ×3 (0.45)"`
- `OqlQueryExecutionResult` exposes an `ExecutionSummary` with per-signal result counts and score range
- brain and source root CRUD fully implemented: `GET/POST/PUT/DELETE /admin/brains/{id}` and `POST/PUT/DELETE /admin/brains/{id}/source-roots/{id}`
- admin console surfaces per-brain health chips, Create Brain form, inline Add Source Root form with Retire button, and OQL smoke-test
- dedicated `opencortex` Kubernetes namespace with pgvector-capable Postgres and SMB NAS volume mounted
- SaaS architecture established: Firebase Auth, Stripe billing, personal API tokens for MCP

## Phases

### Phase 0: Product Definition (complete)

- locked vocabulary: customer, brain, source root, document, chunk, index run, context pack
- defined v0 scope and non-goals
- confirmed filesystem-first and managed-content-first are both first-class future modes
- established SaaS model: free, pro, teams, and enterprise plans
- established MCP security model: personal API tokens with `oct_` prefix

### Phase 1: Architecture Specs (complete)

- documented the multi-brain platform model
- defined OQL vision and v0 grammar
- defined storage abstractions and Postgres default implementation
- defined indexing lifecycle and retrieval pipeline
- defined MCP surface, admin surface, and authoring surface roadmap
- documented SaaS architecture: auth, billing, quotas, managed content, MCP security

### Phase 2: Domain Contracts (complete)

- created domain model for brains, source roots, documents, chunks, links, embeddings, and index runs
- defined brain isolation rules with `brainId` across all core content tables
- defined customer segmentation as optional parent boundary for brains

### Phase 3: Configuration And Brain Registration (complete)

- operator-managed brain definitions
- admin-created brains through API and UI
- local paths, UNC paths, NAS locations, and shared drives as source roots
- per-brain indexing and retrieval settings

### Phase 4: Storage And Infrastructure (complete)

- Postgres and pgvector in Docker Compose and Kubernetes
- database migrations and repository/provider interfaces
- default metadata, link, vector, and index-run stores
- dedicated `opencortex` Kubernetes namespace with SMB NAS volume mounted at `/mnt/obsidian`

### Phase 5: Ingestion And Indexing (complete)

- scan source roots per brain
- parse Markdown and frontmatter
- extract wiki links and graph relationships
- chunk content and generate embeddings
- persist documents, chunks, edges, and index metadata
- deletion reconciliation and stale artifact cleanup on rescan

### Phase 6: Retrieval And OQL (complete)

- OQL parser, AST, planner, executor, and result formatter
- keyword, vector, and graph-aware ranking combined
- explainable context packs with per-signal `ScoreBreakdown`
- `ExecutionSummary` with per-signal result counts and score range

### Phase 7: MCP Server (in progress)

- brain-scoped tools for listing brains, querying OQL, fetching documents, and building context packs
- harden MCP tool contracts around OQL execution and result formatting
- add secure MCP access via personal API tokens for hosted SaaS users

### Phase 8: Admin Surface (complete)

- create and manage brains
- assign brains to customers
- manage source roots and indexing settings
- run reindex jobs and inspect status, health, and failures
- per-brain health dashboard with health chips, run history, and error inspection

### Phase 9: Authoring Surface (next)

- browser Markdown editor using TipTap (WYSIWYG with source mode toggle)
- managed-content document storage: create, edit, delete documents entirely in the browser
- `managed_documents` table stores canonical content in Postgres
- indexing triggered automatically on document save
- document browser: list, filter, open documents within a brain
- import single `.md` file
- export single document as `.md`

### Phase 10: Auth And Identity

- Firebase Auth integration: email/password and Google social login
- JWT validation middleware on all tenant-facing routes
- `users` and `customer_memberships` tables (migration 0002)
- auto-provision personal workspace, brain, and free subscription on first login
- operator/admin surface separated from tenant surface at the routing layer
- role model: owner, admin, editor, viewer

See `docs/architecture/auth-and-identity.md` for full design.

### Phase 11: Billing And Quotas

- Stripe Checkout for Pro upgrade
- Stripe Customer Portal for self-service billing management
- Stripe webhook handling: subscription lifecycle and payment failures
- `subscriptions`, `subscription_events`, `usage_counters` tables (migration 0003)
- free plan: 10 active documents, `mcp:read` only, 100 MCP queries/month
- pro plan: 500 documents, full MCP access including reindex trigger
- structured 402 responses with upgrade prompt on quota hit

See `docs/architecture/billing-and-quotas.md` for full design.

### Phase 12: Secure MCP Access

- personal API tokens with `oct_` prefix, stored as SHA-256 hash
- `api_tokens` table: name, hash, scopes, last used, revoked at
- MCP server validates tokens, resolves user and brain, scopes all tool calls
- token management UI: create, label, revoke from account settings
- free plan: `mcp:read` scope only; pro plan: `mcp:read mcp:write`
- MCP endpoint exposed over HTTPS via Traefik

See `docs/architecture/mcp-security.md` for full design.

### Phase 13: Teams And Shared Brains

- workspace creation separate from personal workspace
- team plan subscription: per-seat pricing via Stripe
- member invite flow via email
- role enforcement: owner, admin, editor, viewer
- shared brains within workspace accessible to all members by role
- personal brains remain per-user within a workspace

### Phase 14: Observability And Hardening

- structured logging with per-request trace IDs and user context
- error tracking (Sentry or equivalent)
- Prometheus metrics and Grafana dashboards for API latency, indexing throughput, quota usage
- alerting for payment failures, quota abuse, and pod crashes
- rate limiting hardened per user and per token
- audit log for significant user actions

### Phase 15: Enterprise And Self-Hosted Packaging

- SAML/SSO integration
- BYOC (bring your own cluster) deployment guide
- Helm chart for self-hosted installer
- private MCP endpoints
- enterprise billing (invoiced, not card)
- dedicated infrastructure option

## Milestones

- [x] M1: architecture documentation complete
- [x] M2: Postgres schema and core contracts complete
- [x] M3: single-brain indexing works end to end
- [x] M4: multi-brain indexing works with strict isolation
- [x] M5: OQL query path works end to end
- [~] M6: MCP server exposes stable brain-scoped tools
- [x] M7: admin API and UI support brain management, per-brain health, and CRUD
- [x] M8: cluster infrastructure: dedicated pgvector Postgres, SMB NAS mount, app secrets
- [ ] M9: authoring surface supports managed-content Markdown creation and editing in browser
- [ ] M10: auth and identity: Firebase Auth, user provisioning, tenant-scoped API
- [ ] M11: billing: Stripe subscriptions, free/pro quota enforcement
- [ ] M12: secure MCP: personal API tokens, scoped access, token management UI
- [ ] M13: teams: shared brains, workspace membership, roles
- [ ] M14: hosted SaaS publicly available

## Near-Term Deliverables

- harden MCP tool contracts around OQL execution, brain scoping, and result formatting
- implement managed-content document CRUD and browser Markdown editor (Phase 9)
- add Firebase Auth JWT middleware and user/workspace provisioning (Phase 10)
- add Stripe billing and document quota enforcement (Phase 11)
- add personal API token system for secure MCP access (Phase 12)
