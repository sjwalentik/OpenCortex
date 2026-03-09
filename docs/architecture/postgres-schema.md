# Postgres Schema Draft

## Purpose

Postgres is the default backend for OpenCortex v0, with `pgvector` used for embedding storage and similarity search.

The schema should support strict brain isolation, future customer segmentation, filesystem-backed brains, and later managed-content brains.

## Design Rules

- every content record is scoped by `brain_id`
- customer scoping is optional and typically applied through brains
- schema should support both filesystem and managed-content brain modes
- provider interfaces should isolate storage logic from OQL and MCP contracts

## Core Tables

### `customers`

- `customer_id`
- `slug`
- `name`
- `status`
- `created_at`
- `updated_at`

### `brains`

- `brain_id`
- `customer_id` nullable
- `slug`
- `name`
- `mode` (`filesystem`, `managed-content`)
- `status`
- `description` nullable
- `created_at`
- `updated_at`

Indexes:

- unique on `slug`
- index on `customer_id`

### `source_roots`

- `source_root_id`
- `brain_id`
- `path`
- `path_type`
- `is_writable`
- `include_patterns` jsonb nullable
- `exclude_patterns` jsonb nullable
- `watch_mode`
- `is_active`
- `created_at`
- `updated_at`

Indexes:

- index on `brain_id`

### `documents`

- `document_id`
- `brain_id`
- `source_root_id` nullable for managed-content mode
- `canonical_path`
- `title`
- `document_type` nullable
- `frontmatter` jsonb nullable
- `content_hash`
- `source_updated_at` nullable
- `indexed_at`
- `is_deleted`

Indexes:

- unique on (`brain_id`, `canonical_path`)
- index on `brain_id`
- index on `title`

### `chunks`

- `chunk_id`
- `brain_id`
- `document_id`
- `chunk_index`
- `heading_path` nullable
- `content`
- `token_count` nullable
- `metadata` jsonb nullable

Indexes:

- unique on (`document_id`, `chunk_index`)
- index on `brain_id`
- index on `document_id`

### `link_edges`

- `link_edge_id`
- `brain_id`
- `from_document_id`
- `to_document_id` nullable
- `target_ref`
- `link_text` nullable
- `link_type`

Indexes:

- index on `brain_id`
- index on `from_document_id`
- index on `to_document_id`

### `embeddings`

- `embedding_id`
- `brain_id`
- `chunk_id`
- `model`
- `dimensions`
- `vector`
- `created_at`

Indexes:

- index on `brain_id`
- unique on `chunk_id`
- vector index on `vector`

### `index_runs`

- `index_run_id`
- `brain_id`
- `trigger_type`
- `status`
- `started_at`
- `completed_at` nullable
- `documents_seen`
- `documents_indexed`
- `documents_failed`
- `error_summary` nullable

Indexes:

- index on `brain_id`
- index on `status`
- index on `started_at`

### `index_run_errors`

- `index_run_error_id`
- `index_run_id`
- `source_root_id` nullable
- `document_path` nullable
- `error_code`
- `error_message`
- `created_at`

Indexes:

- index on `index_run_id`

## Managed-Content Extensions

Likely later tables:

- `document_versions`
- `drafts`
- `reviews`
- `editor_sessions`

These should still carry `brain_id` either directly or through document relationships.

## Query Implications

OQL execution should rely on this schema shape conceptually, but not directly expose it.

Typical execution needs:

- resolve target `brain_id`
- filter documents and chunks by metadata and path
- run keyword and vector matching within one brain
- join graph edges for related-document boosts
- return explainable result metadata

## Recommended v0 Scope

- implement `customers` even if optional
- implement both `filesystem` and `managed-content` brain modes on `brains`
- fully implement filesystem-backed indexing first
- defer versioning and collaborative editing tables until authoring work begins

## SaaS Schema Additions

The following tables are added in later migrations to support the hosted cloud product. All SQL lives in `infra/postgres/migrations/`.

### Migration 0002: Identity And Tenancy

**`users`** — one record per authenticated human, keyed by Firebase Auth UID via `external_id`.

**`customer_memberships`** — links users to workspaces with a role (`owner`, `admin`, `editor`, `viewer`). Unique on `(user_id, customer_id)`.

`customers` gains: `owner_user_id`, `stripe_customer_id`, `plan_id`.

### Migration 0003: Billing

**`subscriptions`** — mirrors Stripe subscription state per customer. Fields: `plan_id` (free/pro/teams/enterprise), `status`, `stripe_customer_id`, `stripe_subscription_id`, `current_period_end`, `cancel_at_period_end`.

**`subscription_events`** — immutable append-only log of all Stripe webhook events. Deduplicated on `stripe_event_id`.

**`usage_counters`** — keyed by `(customer_id, counter_key)`. Counter keys: `documents.active`, `mcp.queries.YYYY-MM`, `indexing.runs.YYYY-MM-DD`. Upserted atomically on every billable action.

### Migration 0004: Managed Content

**`managed_documents`** — canonical document storage for hosted cloud brains. Fields: `brain_id`, `customer_id`, `title`, `slug`, `content` (Markdown text), `content_hash`, `word_count`, `frontmatter` (jsonb), `status` (draft/published/archived), `created_by`, `updated_by`, `is_deleted`. Unique index on `(brain_id, slug)` where `is_deleted = false`.

Indexing pipeline reads from `managed_documents` instead of filesystem for managed-content brains. `source_root_id` is null on resulting `documents` records.

### Migration 0005: API Tokens

**`api_tokens`** — personal API tokens for MCP and programmatic access. Fields: `user_id`, `customer_id`, `name` (user label), `token_hash` (SHA-256, never stored plaintext), `token_prefix` (first 8 chars for display), `scopes` (array: `mcp:read`, `mcp:write`), `expires_at` (nullable), `last_used_at`, `revoked_at`.

Token format: `oct_<32+ bytes base62-encoded random>`. Shown once at creation. Validated by hashing the presented token and looking up `token_hash`.

See `docs/architecture/auth-and-identity.md`, `docs/architecture/billing-and-quotas.md`, and `docs/architecture/mcp-security.md` for full design.
