# Admin And Authoring Surfaces

## Overview

OpenCortex has three human-facing surfaces in addition to MCP:

- an **operator/admin surface** for infrastructure operators managing brains and customers
- an **authoring surface** for editing and curating Markdown knowledge in the browser
- a **tenant API** for hosted cloud users managing their documents, brains, billing, and API tokens

These must be clearly separated in routing, auth middleware, and deployment topology.

## Admin Surface

### Goals

- create and manage brains
- assign brains to customers when needed
- configure source roots and indexing settings
- monitor health, status, and failures
- trigger reindex and smoke-test retrieval

### Current Implementation Snapshot

The API exposes operator-facing endpoints including:

| Endpoint | Purpose |
|----------|---------|
| `GET /health` | Service health and config validation |
| `GET /brains` | List registered brains |
| `GET /admin/brains/health` | Per-brain health summaries with latest run state, document counts, and error info |
| `GET /admin/brains/{id}` | Get full detail for a single brain including source roots |
| `POST /admin/brains` | Create a new brain |
| `PUT /admin/brains/{id}` | Update brain name or description |
| `DELETE /admin/brains/{id}` | Retire a brain (soft delete, preserves history) |
| `POST /admin/brains/{id}/source-roots` | Add a source root to a brain |
| `PUT /admin/brains/{id}/source-roots/{sourceRootId}` | Update a source root's path or settings |
| `DELETE /admin/brains/{id}/source-roots/{sourceRootId}` | Remove a source root from a brain |
| `GET /indexing/plans` | Preview all brain indexing plans |
| `GET /indexing/preview/{brainId}` | Preview a single brain's indexing plan |
| `POST /indexing/run/{brainId}` | Trigger an index run for a brain |
| `GET /indexing/runs` | List recent index runs |
| `GET /indexing/runs/{runId}/errors` | Inspect errors for a specific run |
| `POST /query` | Execute an OQL query |

A lightweight admin console is served from `/admin/`. Navigation to `/admin` (no trailing slash) redirects to `/admin/` cleanly without a redirect loop.

The current console supports:
- browsing all registered brains with per-brain health chips (Healthy / Indexing / Needs Attention / Not Indexed / Retired)
- each brain card shows latest run status, started/completed timestamps, document counts, and error summaries
- **Create Brain** form: name, description, brain ID, source root path, index mode
- **Add Source Root** toggle per brain card: inline form to append additional source roots
- **Retire Brain** button with confirmation dialog (soft-retires, preserves history)
- retired brains display a "Retired" chip; Run Index and Preview are disabled for retired brains
- triggering indexing and previewing index plans per brain
- inspecting index run history and per-run errors
- smoke-testing OQL queries against any brain; results show score breakdown chips (kw / sem / graph) and execution summary bar
- graceful fallback messaging when individual API sections fail to load

Health chip state is derived exclusively from the latest run for each brain; older stale runs with `running` status in history do not pollute the current health indicator. `BrainHealthSummary` carries `IsConfigured` to distinguish active config-backed brains from retired historical ones.

Schedule management remains a future admin workflow.

### v1 Admin Capabilities

- brain CRUD ✅
- source root CRUD ✅
- index run history and status ✅
- brain health dashboard ✅
- retrieval smoke-test and operator diagnostics ✅
- indexing schedule management

### Later Admin Capabilities

- customer management
- quotas and billing metadata: per-customer plan, document count, monthly query usage
- user and role management
- per-brain access policies

## Operator Surface Isolation

Before public launch the operator/admin surface must not be reachable on the public-facing domain.

Recommended approach:
- separate Kubernetes Ingress for `/admin/*` and `/indexing/*` restricted to internal cluster IPs
- or: separate deployment for operator-facing API on a non-public port

All `/admin/*` and `/indexing/*` routes must require an elevated operator role, not just any authenticated user token.

## Authoring Surface

### Goals

- create, browse, open, edit, preview, and save Markdown documents in the browser
- support managed-content mode as the primary hosted cloud editing model
- support filesystem-backed editing for self-hosted brains later

### Managed-Content Authoring (Hosted Cloud — Phase 9)

- users create and edit Markdown entirely in the browser
- OpenCortex stores canonical document content in the `managed_documents` table
- editor: TipTap (WYSIWYG with Markdown source mode toggle)
- indexing triggered automatically on document save via background job
- document list, create, edit, soft-delete, import single `.md`, export single `.md`

### Filesystem-Backed Authoring (Self-Hosted — Later)

- browser editing writes back to the configured source root on disk or NAS
- OpenCortex triggers a targeted reindex after save
- conflict handling and lock awareness deferred

## Tenant API Surface (Hosted Cloud)

The tenant API is distinct from the operator/admin surface. All routes are authenticated via Firebase Auth JWT and scoped to the requesting user's workspace.

### Document Routes

| Route | Method | Description |
|---|---|---|
| `/documents` | GET | List documents in active brain |
| `/documents` | POST | Create document (quota checked) |
| `/documents/:id` | GET | Get document content and metadata |
| `/documents/:id` | PUT | Update document content |
| `/documents/:id` | DELETE | Soft-delete document |
| `/documents/import` | POST | Import a single `.md` file |
| `/documents/export` | GET | Export document as `.md` |

### Brain Routes (Tenant)

| Route | Method | Description |
|---|---|---|
| `/brains` | GET | List brains in active workspace |
| `/brains` | POST | Create brain (quota checked) |
| `/brains/:id` | GET | Get brain details |
| `/brains/:id/reindex` | POST | Trigger full reindex |

### Query Routes

| Route | Method | Description |
|---|---|---|
| `/query` | POST | Execute OQL query scoped to authenticated user's brain |

### Billing Routes

| Route | Method | Description |
|---|---|---|
| `/billing/plan` | GET | Current plan and usage summary |
| `/billing/upgrade` | POST | Create Stripe Checkout session |
| `/billing/portal` | POST | Create Stripe Customer Portal session |

### API Token Routes (MCP Access)

| Route | Method | Description |
|---|---|---|
| `/tokens` | GET | List user's API tokens (name, prefix, scopes, last used) |
| `/tokens` | POST | Create a new API token (shown once at creation) |
| `/tokens/:id` | DELETE | Revoke a token immediately |

See `docs/architecture/mcp-security.md` for full MCP token design.

## Why Both Surfaces Matter

- admin requirements affect how brains are created and persisted
- authoring requirements affect document identity, path handling, and versioning
- both surfaces rely on the same brain model and indexing lifecycle
- the tenant API and operator surface must never share auth middleware

## Delivery Order

- runtime and storage: complete
- OQL and MCP: complete (hardening in progress)
- operator admin API and UI: complete
- managed-content authoring surface: Phase 9 (next)
- auth and tenant API: Phase 10
- billing and quotas: Phase 11
- secure MCP tokens: Phase 12

## Future Considerations

- collaborative real-time editing
- wiki-link autocomplete in editor
- backlinks panel and graph navigation
- document version history and restore
- review and publishing workflows
- customer-scoped branding
- public read-only brain sharing
