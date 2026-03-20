ALTER TABLE opencortex.users
    ADD COLUMN IF NOT EXISTS memory_brain_id text NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_users_memory_brain_id'
          AND connamespace = 'opencortex'::regnamespace
    ) THEN
        ALTER TABLE opencortex.users
            ADD CONSTRAINT fk_users_memory_brain_id
            FOREIGN KEY (memory_brain_id)
            REFERENCES opencortex.brains(brain_id)
            ON DELETE SET NULL;
    END IF;
END $$;

COMMENT ON COLUMN opencortex.users.memory_brain_id IS
'Preferred managed-content brain for agent memory storage. NULL means auto-select the only active managed-content brain or require configuration.';

INSERT INTO opencortex.schema_migrations (migration_id)
VALUES ('0009_user_memory_brain')
ON CONFLICT (migration_id) DO NOTHING;
