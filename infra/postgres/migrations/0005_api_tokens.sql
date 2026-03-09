CREATE TABLE IF NOT EXISTS opencortex.api_tokens (
    api_token_id text PRIMARY KEY,
    user_id text NOT NULL REFERENCES opencortex.users(user_id) ON DELETE CASCADE,
    customer_id text NOT NULL REFERENCES opencortex.customers(customer_id) ON DELETE CASCADE,
    name text NOT NULL,
    token_hash text NOT NULL UNIQUE,
    token_prefix text NOT NULL,
    scopes text[] NOT NULL DEFAULT ARRAY['mcp:read'],
    expires_at timestamptz NULL,
    last_used_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    revoked_at timestamptz NULL
);

CREATE INDEX IF NOT EXISTS ix_api_tokens_user_id
    ON opencortex.api_tokens(user_id);

CREATE INDEX IF NOT EXISTS ix_api_tokens_customer_id
    ON opencortex.api_tokens(customer_id);

CREATE INDEX IF NOT EXISTS ix_api_tokens_token_hash
    ON opencortex.api_tokens(token_hash);

INSERT INTO opencortex.schema_migrations (migration_id)
VALUES ('0005_api_tokens')
ON CONFLICT (migration_id) DO NOTHING;
