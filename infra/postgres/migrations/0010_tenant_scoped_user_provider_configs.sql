ALTER TABLE opencortex.user_provider_configs
    DROP CONSTRAINT IF EXISTS uq_user_provider;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'uq_user_provider'
          AND connamespace = 'opencortex'::regnamespace
    ) THEN
        ALTER TABLE opencortex.user_provider_configs
            ADD CONSTRAINT uq_user_provider UNIQUE (customer_id, user_id, provider_id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_user_provider_configs_customer_user_id
    ON opencortex.user_provider_configs(customer_id, user_id);

INSERT INTO opencortex.schema_migrations (migration_id)
VALUES ('0010_tenant_scoped_user_provider_configs')
ON CONFLICT (migration_id) DO NOTHING;
