# Source Roots And Indexing

## Purpose

OpenCortex must index knowledge from configurable locations rather than hardcoded machine-specific paths.

The indexing design should work for local directories, NAS paths, shared drives, and future managed-content brains.

## Source Root Rules

- a brain can have one or more source roots
- source roots are configured per brain
- filesystem roots can be local, UNC, NAS, or mapped drive paths
- source roots should be validated before indexing starts
- inaccessible roots should fail clearly without corrupting brain state

## v0 Indexing Model

Recommended order:

- manual reindex
- scheduled reindex
- filesystem watch later

This avoids depending too early on file-watch behavior across NAS and network shares.

## Indexing Lifecycle

### 1. Discover

- enumerate configured roots for a brain
- resolve include and exclude patterns
- detect Markdown files and supported metadata files

### 2. Parse

- read file contents
- parse frontmatter if present
- normalize path and source metadata

### 3. Analyze

- extract wiki links
- extract headings and sections
- compute document metadata
- determine chunk boundaries

### 4. Persist

- upsert documents
- upsert chunks
- upsert link edges
- upsert embeddings
- record index run status and timings

### 5. Reconcile

- detect deleted files
- mark stale documents
- remove or archive outdated chunks and edges

## Filesystem Mode Notes

- the filesystem remains the canonical source of truth
- edits outside OpenCortex are expected
- indexing must be resilient to transient path failures and file locks

## Managed-Content Mode Notes

- OpenCortex becomes the canonical source of truth
- document content is stored in the `managed_documents` table in Postgres
- indexing is triggered on document save via background job, not by filesystem scan
- filesystem discovery (step 1) is replaced by enumeration of `managed_documents` records
- `source_root_id` is null on all `documents` records for managed-content brains
- full reindex still available as a manual operation

### Managed-Content Indexing Trigger

```
User saves document in browser
    → PUT /documents/:id (tenant API)
    → content saved to managed_documents
    → background job queued
    → job runs indexing pipeline against managed_documents record
    → documents, chunks, embeddings, link_edges upserted
```

Filesystem mode remains for self-hosted operator deployments. Hosted cloud users use managed-content mode exclusively in v1.

## Suggested Indexing Records

- `index_runs`
- `index_run_errors`
- `source_root_snapshots` later if needed

## Operational Priorities

- clear failure reporting per brain and per source root
- incremental reindexing later, full scan first
- deterministic path normalization across Windows and network shares
