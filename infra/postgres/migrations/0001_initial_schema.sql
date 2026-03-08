CREATE EXTENSION IF NOT EXISTS vector;

CREATE SCHEMA IF NOT EXISTS opencortex;

CREATE TABLE IF NOT EXISTS opencortex.schema_migrations (
    migration_id text PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS opencortex.customers (
    customer_id text PRIMARY KEY,
    slug text NOT NULL UNIQUE,
    name text NOT NULL,
    status text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS opencortex.brains (
    brain_id text PRIMARY KEY,
    customer_id text NULL REFERENCES opencortex.customers(customer_id),
    slug text NOT NULL UNIQUE,
    name text NOT NULL,
    mode text NOT NULL,
    status text NOT NULL,
    description text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_brains_customer_id ON opencortex.brains(customer_id);

CREATE TABLE IF NOT EXISTS opencortex.source_roots (
    source_root_id text PRIMARY KEY,
    brain_id text NOT NULL REFERENCES opencortex.brains(brain_id) ON DELETE CASCADE,
    path text NOT NULL,
    path_type text NOT NULL,
    is_writable boolean NOT NULL DEFAULT false,
    include_patterns jsonb NULL,
    exclude_patterns jsonb NULL,
    watch_mode text NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_source_roots_brain_id ON opencortex.source_roots(brain_id);

CREATE TABLE IF NOT EXISTS opencortex.documents (
    document_id text PRIMARY KEY,
    brain_id text NOT NULL REFERENCES opencortex.brains(brain_id) ON DELETE CASCADE,
    source_root_id text NULL REFERENCES opencortex.source_roots(source_root_id) ON DELETE SET NULL,
    canonical_path text NOT NULL,
    title text NOT NULL,
    document_type text NULL,
    frontmatter jsonb NULL,
    content_hash text NOT NULL,
    source_updated_at timestamptz NULL,
    indexed_at timestamptz NOT NULL,
    is_deleted boolean NOT NULL DEFAULT false
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_documents_brain_path ON opencortex.documents(brain_id, canonical_path);
CREATE INDEX IF NOT EXISTS ix_documents_brain_id ON opencortex.documents(brain_id);
CREATE INDEX IF NOT EXISTS ix_documents_title ON opencortex.documents(title);

CREATE TABLE IF NOT EXISTS opencortex.chunks (
    chunk_id text PRIMARY KEY,
    brain_id text NOT NULL REFERENCES opencortex.brains(brain_id) ON DELETE CASCADE,
    document_id text NOT NULL REFERENCES opencortex.documents(document_id) ON DELETE CASCADE,
    chunk_index integer NOT NULL,
    heading_path text NULL,
    content text NOT NULL,
    token_count integer NULL,
    metadata jsonb NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_chunks_document_index ON opencortex.chunks(document_id, chunk_index);
CREATE INDEX IF NOT EXISTS ix_chunks_brain_id ON opencortex.chunks(brain_id);
CREATE INDEX IF NOT EXISTS ix_chunks_document_id ON opencortex.chunks(document_id);

CREATE TABLE IF NOT EXISTS opencortex.link_edges (
    link_edge_id text PRIMARY KEY,
    brain_id text NOT NULL REFERENCES opencortex.brains(brain_id) ON DELETE CASCADE,
    from_document_id text NOT NULL REFERENCES opencortex.documents(document_id) ON DELETE CASCADE,
    to_document_id text NULL REFERENCES opencortex.documents(document_id) ON DELETE SET NULL,
    target_ref text NOT NULL,
    link_text text NULL,
    link_type text NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_link_edges_brain_id ON opencortex.link_edges(brain_id);
CREATE INDEX IF NOT EXISTS ix_link_edges_from_document_id ON opencortex.link_edges(from_document_id);
CREATE INDEX IF NOT EXISTS ix_link_edges_to_document_id ON opencortex.link_edges(to_document_id);

CREATE TABLE IF NOT EXISTS opencortex.embeddings (
    embedding_id text PRIMARY KEY,
    brain_id text NOT NULL REFERENCES opencortex.brains(brain_id) ON DELETE CASCADE,
    chunk_id text NOT NULL UNIQUE REFERENCES opencortex.chunks(chunk_id) ON DELETE CASCADE,
    model text NOT NULL,
    dimensions integer NOT NULL,
    vector vector(1536) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_embeddings_brain_id ON opencortex.embeddings(brain_id);
CREATE INDEX IF NOT EXISTS ix_embeddings_vector ON opencortex.embeddings USING ivfflat (vector vector_cosine_ops) WITH (lists = 100);

CREATE TABLE IF NOT EXISTS opencortex.index_runs (
    index_run_id text PRIMARY KEY,
    brain_id text NOT NULL REFERENCES opencortex.brains(brain_id) ON DELETE CASCADE,
    trigger_type text NOT NULL,
    status text NOT NULL,
    started_at timestamptz NOT NULL,
    completed_at timestamptz NULL,
    documents_seen integer NOT NULL DEFAULT 0,
    documents_indexed integer NOT NULL DEFAULT 0,
    documents_failed integer NOT NULL DEFAULT 0,
    error_summary text NULL
);

CREATE INDEX IF NOT EXISTS ix_index_runs_brain_id ON opencortex.index_runs(brain_id);
CREATE INDEX IF NOT EXISTS ix_index_runs_status ON opencortex.index_runs(status);
CREATE INDEX IF NOT EXISTS ix_index_runs_started_at ON opencortex.index_runs(started_at);

CREATE TABLE IF NOT EXISTS opencortex.index_run_errors (
    index_run_error_id text PRIMARY KEY,
    index_run_id text NOT NULL REFERENCES opencortex.index_runs(index_run_id) ON DELETE CASCADE,
    source_root_id text NULL REFERENCES opencortex.source_roots(source_root_id) ON DELETE SET NULL,
    document_path text NULL,
    error_code text NOT NULL,
    error_message text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_index_run_errors_index_run_id ON opencortex.index_run_errors(index_run_id);

INSERT INTO opencortex.schema_migrations (migration_id)
VALUES ('0001_initial_schema')
ON CONFLICT (migration_id) DO NOTHING;
