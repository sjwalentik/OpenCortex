# Admin And Authoring Surfaces

## Overview

OpenCortex needs two human-facing surfaces in addition to MCP:

- an admin surface for operating brains and customers
- an authoring surface for editing and curating Markdown knowledge

These should be planned now even if they ship after the runtime and MCP layers.

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
- quotas and billing metadata
- user and role management
- per-brain access policies

## Authoring Surface

### Goals

- browse documents inside a brain
- open Markdown documents
- edit and preview Markdown in the browser
- preserve link-aware authoring workflows

### Filesystem-Backed Authoring

- browser editing writes back to the configured source root
- OpenCortex triggers a targeted reindex after save
- conflict handling and lock awareness will matter later

### Managed-Content Authoring

- OpenCortex stores canonical content directly
- version history, publishing, and review workflows become easier to add
- this is the likely model for hosted/cloud usage

## Why Both Surfaces Matter Early

- admin requirements affect how brains are created and persisted
- authoring requirements affect document identity, path handling, and versioning
- both surfaces rely on the same brain model and indexing lifecycle

## Recommended Delivery Order

- runtime and storage first
- OQL and MCP second
- admin API and UI third
- authoring UI fourth

That sequence is still holding: runtime, indexing, retrieval, admin inspection, and brain/source root CRUD all exist now, while authoring remains ahead.

## Future Considerations

- collaborative editing
- wiki link autocomplete
- backlinks and graph navigation
- document version history
- review and publishing workflows
- customer-scoped branding or management experiences
