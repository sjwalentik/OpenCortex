CREATE TABLE IF NOT EXISTS opencortex.managed_documents (
    managed_document_id text PRIMARY KEY,
    brain_id text NOT NULL REFERENCES opencortex.brains(brain_id) ON DELETE CASCADE,
    customer_id text NOT NULL REFERENCES opencortex.customers(customer_id) ON DELETE CASCADE,
    title text NOT NULL,
    slug text NOT NULL,
    content text NOT NULL,
    content_hash text NOT NULL,
    word_count integer NOT NULL DEFAULT 0,
    frontmatter jsonb NULL,
    status text NOT NULL DEFAULT 'draft',
    created_by text NOT NULL REFERENCES opencortex.users(user_id),
    updated_by text NOT NULL REFERENCES opencortex.users(user_id),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    is_deleted boolean NOT NULL DEFAULT false
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_managed_documents_brain_slug_active
    ON opencortex.managed_documents(brain_id, slug)
    WHERE is_deleted = false;

CREATE INDEX IF NOT EXISTS ix_managed_documents_customer_brain
    ON opencortex.managed_documents(customer_id, brain_id);

CREATE INDEX IF NOT EXISTS ix_managed_documents_updated_at
    ON opencortex.managed_documents(updated_at DESC);

INSERT INTO opencortex.schema_migrations (migration_id)
VALUES ('0004_managed_content')
ON CONFLICT (migration_id) DO NOTHING;
