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

- the API already exposes operator-facing endpoints for health, brain listing, indexing preview, indexing runs, run history, run errors, and OQL execution
- a lightweight admin console is served from `/admin/`
- the current console supports browsing brains, triggering indexing, inspecting run history and errors, and smoke-testing OQL queries
- brain creation, source root CRUD, and schedule management are still future admin workflows

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

That sequence is still holding: runtime, indexing, retrieval, and the first admin inspection surface exist now, while richer admin workflows and authoring remain ahead.

## Future Considerations

- collaborative editing
- wiki link autocomplete
- backlinks and graph navigation
- document version history
- review and publishing workflows
- customer-scoped branding or management experiences
