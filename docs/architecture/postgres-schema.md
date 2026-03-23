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

Current migration status:

- `0001_initial_schema.sql` creates `vector` as `vector(1536)`
- the application also persists the logical embedding size in `dimensions`
- operators must keep the embedding provider output size, `OpenCortex:Embeddings:Dimensions`, and the `pgvector` column definition aligned
- if an operator changes providers from a `1536`-dimension model to a `768`-dimension model, the `opencortex.embeddings.vector` column and vector index must be rebuilt to the new size before further indexing
- the API, MCP server, and workers now validate `OpenCortex:Embeddings:Dimensions` against `opencortex.embeddings.vector` at startup and refuse to start if they do not match

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

Current repo status: migration `0003_billing_schema.sql` exists and creates these tables, but Stripe webhook and quota-enforcement application logic is still pending.

### Migration 0004: Managed Content

**`managed_documents`** — canonical document storage for hosted cloud brains. Fields: `brain_id`, `customer_id`, `title`, `slug`, `content` (Markdown text), `content_hash`, `word_count`, `frontmatter` (jsonb), `status` (draft/published/archived), `created_by`, `updated_by`, `is_deleted`. Unique index on `(brain_id, slug)` where `is_deleted = false`.

Indexing pipeline reads from `managed_documents` instead of filesystem for managed-content brains. `source_root_id` is null on resulting `documents` records.

Current repo status: migration `0004_managed_content.sql` exists and the API now has tenant-scoped list/get/create/update/delete routes backed directly by `managed_documents`. Save-triggered indexing currently runs inline as a full managed-brain rebuild; background queueing is still pending.

**`managed_document_versions`** — immutable snapshots for managed-content document history and restore. Fields: `managed_document_version_id`, `managed_document_id`, `brain_id`, `customer_id`, `title`, `slug`, `content`, `frontmatter`, `content_hash`, `status`, `word_count`, `snapshot_kind` (`created`/`updated`/`deleted`/`restored`), `snapshot_by`, `created_at`.

Current repo status: migration `0006_managed_document_versions.sql` exists and the tenant API now supports list/get/restore routes for managed-document versions. Every managed-content create, update, delete, and restore persists a snapshot row before the request returns.

### Migration 0005: API Tokens

**`api_tokens`** — personal API tokens for MCP and programmatic access. Fields: `user_id`, `customer_id`, `name` (user label), `token_hash` (SHA-256, never stored plaintext), `token_prefix` (first 8 chars for display), `scopes` (array: `mcp:read`, `mcp:write`), `expires_at` (nullable), `last_used_at`, `revoked_at`.

Token format: `oct_<32+ bytes base62-encoded random>`. Shown once at creation. Validated by hashing the presented token and looking up `token_hash`.

See `docs/architecture/auth-and-identity.md`, `docs/architecture/billing-and-quotas.md`, and `docs/architecture/mcp-security.md` for full design.

### Migration 0006: Managed Document Versions

**Additional table for document versioning** — extends the managed-content schema with version tracking and restore capabilities.

**Purpose:** Enables version history, audit trails, and document restoration for managed-content brains. All version table operations are scoped by `managed_document_id`.

**Tables:**

#### `managed_document_versions`
- `managed_document_version_id` (text PRIMARY KEY)
- `managed_document_id` (text, NOT NULL, references `managed_documents.managed_document_id` ON DELETE CASCADE)
- `brain_id` (text, NOT NULL, references `brains.brain_id` ON DELETE CASCADE)
- `customer_id` (text, NOT NULL, references `customers.customer_id` ON DELETE CASCADE)
- `title` (text, nullable)
- `slug` (text, nullable)
- `content` (text, nullable)
- `frontmatter` (jsonb, nullable)
- `content_hash` (text, nullable)
- `status` (text, nullable) — draft/published/archived
- `word_count` (int, nullable)
- `snapshot_kind` (text, NOT NULL) — created/updated/deleted/restored
- `snapshot_by` (text, nullable, references `users.user_id` ON DELETE SET NULL)
- `created_at` (timestamptz, NOT NULL, default now())

**Indexes:**
- `ix_mdv_version_by_document` on `(managed_document_id, created_at DESC)`
- `ix_mdv_customer_snapshots` on `(customer_id, created_at)`
- Unique index on `(managed_document_id, snapshot_kind)` — prevents duplicate kinds for same document

**Behavior:**
- Trigger fires automatically on managed_documents INSERT/UPDATE/DELETE
- Each write operation creates a snapshot row before the mutation completes
- Snapshot contains full document state at point of mutation
- Snapshot tracks actor via `snapshot_by`
- Snapshot tracks type of change via `snapshot_kind`
- Soft-deleted documents create "deleted" snapshots for audit

**Current repo status:** migration `0006_managed_document_versions.sql` exists and the tenant API now supports list/get/restore routes for managed-document versions. Every managed-content create, update, delete, and restore persists a snapshot row before the request returns.

### Migration 0007: Conversations

**Purpose:** Multi-model orchestration support for chat/conversational AI patterns.

**Tables:**

#### `conversations`
- `conversation_id` (text PRIMARY KEY)
- `brain_id` (text, nullable, references `brains.brain_id` ON DELETE SET NULL)
- `customer_id` (text, NOT NULL, references `customers.customer_id` ON DELETE CASCADE)
- `user_id` (text, nullable, references `users.user_id` ON DELETE SET NULL)
- `title` (text, nullable)
- `system_prompt` (text, nullable)
- `status` (text, default 'active')
- `metadata` (jsonb, nullable)
- `created_at` (timestamptz, default now())
- `last_message_at` (timestamptz, nullable)
- `updated_at` (timestamptz, NOT NULL, default now())

**Indexes:**
- `ix_conversations_customer_id` on `customer_id`
- `ix_conversations_user_id` on `user_id` (filtered WHERE user_id IS NOT NULL)
- `ix_conversations_brain_id` on `brain_id` (filtered WHERE brain_id IS NOT NULL)
- `ix_conversations_status` on `(customer_id, status)`
- `ix_conversations_last_message` on `(customer_id, last_message_at DESC NULLS LAST)` (filtered WHERE status = 'active')

#### `messages`
- `message_id` (text PRIMARY KEY)
- `conversation_id` (text, NOT NULL, references `conversations.conversation_id` ON DELETE CASCADE)
- `parent_message_id` (text, nullable, references `messages.message_id` ON DELETE SET NULL)
- `role` (text, NOT NULL)
- `content` (text, nullable)
- `provider_id` (text, nullable)
- `model_id` (text, nullable)
- `tool_calls` (jsonb, nullable)
- `token_usage` (jsonb, nullable)
- `latency_ms` (int, nullable)
- `metadata` (jsonb, nullable)
- `created_at` (timestamptz, NOT NULL, default now())

**Indexes:**
- `ix_messages_conversation_id` on `(conversation_id, created_at)`
- `ix_messages_parent` on `parent_message_id` (filtered WHERE parent_message_id IS NOT NULL)

#### `conversation_summaries`
- `summary_id` (text PRIMARY KEY)
- `conversation_id` (text, NOT NULL, references `conversations.conversation_id` ON DELETE CASCADE)
- `summary_text` (text, NOT NULL)
- `message_range_start` (text, NOT NULL, references `messages.message_id`)
- `message_range_end` (text, NOT NULL, references `messages.message_id`)
- `message_count` (int, NOT NULL)
- `created_at` (timestamptz, NOT NULL, default now())

**Indexes:**
- `ix_conversation_summaries_conversation` on `(conversation_id, created_at)`

**Triggers:**
- `opencortex.update_conversation_timestamp()` triggers on messages table INSERT
- Automatically updates `conversations.updated_at` and `conversations.last_message_at` when new messages arrive

**Current repo status:** Migration exists. Chat/conversation application logic still pending.

### Migration 0008: User Provider Configs

**Purpose:** Allows users to configure their own LLM provider credentials (API keys, tokens, OAuth) instead of relying solely on platform-managed providers.

**Table: `user_provider_configs`**

- `config_id` (text PRIMARY KEY)
- `customer_id` (text, NOT NULL, references `customers.customer_id` ON DELETE CASCADE)
- `user_id` (text, NOT NULL, references `users.user_id` ON DELETE CASCADE)
- `provider_id` (text, NOT NULL)
- `auth_type` (text, NOT NULL) — enum like `api_key`, `oauth_token`, `bearer_token`
- `encrypted_api_key` (text, nullable)
- `encrypted_access_token` (text, nullable)
- `encrypted_refresh_token` (text, nullable)
- `token_expires_at` (timestamptz, nullable)
- `settings_json` (jsonb, nullable) — provider-specific settings (region, custom endpoint, etc.)
- `is_enabled` (boolean, default true)
- `created_at` (timestamptz, NOT NULL, default now())
- `updated_at` (timestamptz, NOT NULL, default now())

**Indexes:**
- `ix_user_provider_configs_customer_user` on `(customer_id, user_id, provider_id)`
- `ix_user_provider_configs_customer` on `customer_id`

**Design notes:**
- All sensitive credential fields are encrypted at rest
- Users can configure their own credentials per provider
- Platform can still fall back to platform-managed providers when user provides none
- Enables BYOK (Bring Your Own Key) pattern for enterprise customers

**Current repo status:** The base provider-config table shipped in `0008_user_provider_configs.sql`. Tenant scoping was corrected in `0010_tenant_scoped_user_provider_configs.sql`, so provider configs are now stored and resolved by `(customer_id, user_id, provider_id)` instead of only `(user_id, provider_id)`.
