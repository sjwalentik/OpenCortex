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
