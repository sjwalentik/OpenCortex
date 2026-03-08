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
| `GET /indexing/plans` | Preview all brain indexing plans |
| `GET /indexing/preview/{brainId}` | Preview a single brain's indexing plan |
| `GET /indexing/run/{brainId}` | Trigger an index run for a brain |
| `GET /indexing/runs` | List recent index runs |
| `GET /indexing/runs/{runId}/errors` | Inspect errors for a specific run |
| `POST /query` | Execute an OQL query |

A lightweight admin console is served from `/admin/`. Navigation to `/admin` (no trailing slash) redirects to `/admin/` cleanly without a redirect loop.

The current console supports:
- browsing all registered brains with per-brain health chips (Healthy / Indexing / Needs Attention / Not Indexed)
- each brain card shows latest run status, started/completed timestamps, document counts, and error summaries
- triggering indexing and previewing index plans per brain
- inspecting index run history and per-run errors
- smoke-testing OQL queries against any brain
- graceful fallback messaging when individual API sections fail to load

Health chip state is derived exclusively from the latest run for each brain; older stale runs with `running` status in history do not pollute the current health indicator.

Brain creation, source root CRUD, and schedule management are still future admin workflows.

### v1 Admin Capabilities

- brain CRUD
- source root CRUD
- indexing schedule management
- index run history and status
- brain health dashboard
- retrieval smoke-test and operator diagnostics

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

That sequence is still holding: runtime, indexing, retrieval, and the first admin inspection surface (including per-brain health) exist now, while richer admin workflows (brain/source root CRUD) and authoring remain ahead.

## Future Considerations

- collaborative editing
- wiki link autocomplete
- backlinks and graph navigation
- document version history
- review and publishing workflows
- customer-scoped branding or management experiences
