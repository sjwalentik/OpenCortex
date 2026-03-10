CREATE TABLE IF NOT EXISTS opencortex.managed_document_versions (
    managed_document_version_id text PRIMARY KEY,
    managed_document_id text NOT NULL REFERENCES opencortex.managed_documents(managed_document_id) ON DELETE CASCADE,
    brain_id text NOT NULL REFERENCES opencortex.brains(brain_id) ON DELETE CASCADE,
    customer_id text NOT NULL REFERENCES opencortex.customers(customer_id) ON DELETE CASCADE,
    title text NOT NULL,
    slug text NOT NULL,
    content text NOT NULL,
    frontmatter jsonb NOT NULL DEFAULT '{}'::jsonb,
    content_hash text NOT NULL,
    status text NOT NULL,
    word_count integer NOT NULL DEFAULT 0,
    snapshot_kind text NOT NULL,
    snapshot_by text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_managed_document_versions_document_created_at
    ON opencortex.managed_document_versions(managed_document_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_managed_document_versions_customer_brain
    ON opencortex.managed_document_versions(customer_id, brain_id, managed_document_id);
