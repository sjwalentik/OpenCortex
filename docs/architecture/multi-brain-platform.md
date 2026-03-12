# Multi-Brain Platform Model

## Overview

OpenCortex runs as one service that hosts many isolated brains.

A brain is the primary retrieval and indexing boundary. It can represent a team, customer, internal domain, product area, or personal knowledge base.

## Core Entities

### Customer

An optional organizational boundary used for cloud and multi-customer deployments.

- a customer can own many brains
- a self-hosted deployment may use brains without customers

### Brain

The core knowledge tenancy boundary.

- owns source roots
- owns retrieval scope
- owns indexing settings
- owns health and operational state

Required fields:

- `brainId`
- `name`
- `slug`
- `mode` (`filesystem` or `managed-content`)
- `status`
- `customerId` optional

### Source Root

A configured content location for a filesystem-backed brain.

Examples:

- local directory
- UNC path
- NAS share
- mapped drive path

Suggested fields:

- `sourceRootId`
- `brainId`
- `path`
- `pathType`
- `isWritable`
- `includePatterns`
- `excludePatterns`
- `watchMode`

## Isolation Rules

- every document, chunk, edge, embedding, and index run is scoped by `brainId`
- brains do not share retrieval results by default
- OQL targets exactly one brain in v0
- cross-brain retrieval must be an explicit future feature, not an accidental side effect

## Brain Modes

### Filesystem Mode

Used for self-hosted and open source scenarios.

- Markdown files live outside OpenCortex on a local disk, NAS, or shared drive
- OpenCortex indexes and optionally edits those files later through the authoring UI
- the filesystem remains the canonical source of truth

### Managed-Content Mode

Used for hosted/cloud scenarios.

- OpenCortex stores and edits the canonical document content
- file import/export can exist later, but the managed store is authoritative
- browser editing, review workflows, and permissions become first-class concerns

## Why Both Modes Matter

The self-hosted story and the cloud story should share one mental model:

- both have brains
- both produce documents, chunks, links, embeddings, and context packs
- both are queried through OQL and MCP
- both can be managed through the admin surface

The main difference is where the canonical content lives.

## Recommended v0 Decisions

- support both brain modes in the model now
- implement filesystem mode first
- design managed-content mode into the schema and docs now, even if execution comes later
- use a shared Postgres database with strong `brainId` scoping

## Hosted SaaS Model

The cloud-hosted product uses managed-content brains exclusively for tenant users. Filesystem brains remain operator-only in v1.

### Individual Accounts

- every user gets one personal workspace (customer) on signup
- one personal brain created by default (mode: managed-content)
- documents are created and edited in the browser
- filesystem is not exposed to cloud users in v1

### Team Accounts (Phase 13)

- a workspace can have multiple members
- shared brains are accessible to all workspace members according to role
- individual personal brains still exist per member
- roles: owner, admin, editor, viewer

### Deployment Mode Split

| Aspect | Self-Hosted (Filesystem) | Hosted SaaS (Managed-Content) |
|---|---|---|
| Content source of truth | Filesystem / NAS | Postgres (`managed_documents`) |
| Editing | External editor (Obsidian, VSCode) | Browser (React + Tiptap editor) |
| Indexing trigger | Manual, scheduled | On save + manual reindex |
| Auth | Operator-managed | Firebase Auth + tenant JWT |
| Billing | None | Stripe subscriptions |
| MCP access | Operator config | Personal API tokens (`oct_xxx`) |
| Available to cloud users | No (v1) | Yes |

### Tenant Isolation Rules (Hosted)

- every API request resolves `customerId` from the authenticated session
- all brain reads, writes, and indexing operations filter by `customerId`
- users cannot access or enumerate brains from other customers
- OQL query execution checks brain ownership before executing
- operator/admin surface requires separate elevated role and is not reachable from the public domain

See `docs/architecture/auth-and-identity.md` for identity and tenancy design.
