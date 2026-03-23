-- User provider configurations
-- Users configure their own provider credentials (API keys or OAuth tokens)

CREATE TABLE IF NOT EXISTS opencortex.user_provider_configs (
    config_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    provider_id TEXT NOT NULL,

    -- Authentication type: 'api_key' or 'oauth'
    auth_type TEXT NOT NULL DEFAULT 'api_key',

    -- Encrypted credentials (encrypted at application layer)
    encrypted_api_key TEXT,
    encrypted_access_token TEXT,
    encrypted_refresh_token TEXT,
    token_expires_at TIMESTAMPTZ,

    -- Provider-specific settings as JSON
    settings_json JSONB,

    is_enabled BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ,

    -- Each user can only have one config per provider
    CONSTRAINT uq_user_provider UNIQUE (user_id, provider_id)
);

-- Index for fast lookups by user
CREATE INDEX IF NOT EXISTS idx_user_provider_configs_user_id ON opencortex.user_provider_configs(user_id);
CREATE INDEX IF NOT EXISTS idx_user_provider_configs_customer_id ON opencortex.user_provider_configs(customer_id);

-- Trigger to auto-update updated_at
CREATE OR REPLACE FUNCTION opencortex.update_user_provider_configs_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_user_provider_configs_updated_at ON opencortex.user_provider_configs;
CREATE TRIGGER trg_user_provider_configs_updated_at
    BEFORE UPDATE ON opencortex.user_provider_configs
    FOR EACH ROW
    EXECUTE FUNCTION opencortex.update_user_provider_configs_updated_at();

COMMENT ON TABLE opencortex.user_provider_configs IS 'User-specific LLM provider configurations with encrypted credentials';
COMMENT ON COLUMN opencortex.user_provider_configs.encrypted_api_key IS 'API key encrypted with application-level encryption';
COMMENT ON COLUMN opencortex.user_provider_configs.settings_json IS 'Provider-specific settings like default model, base URL, etc.';

INSERT INTO opencortex.schema_migrations (migration_id)
VALUES ('0008_user_provider_configs')
ON CONFLICT (migration_id) DO NOTHING;
