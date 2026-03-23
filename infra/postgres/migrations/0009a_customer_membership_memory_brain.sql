ALTER TABLE opencortex.customer_memberships
    ADD COLUMN IF NOT EXISTS memory_brain_id text NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_customer_memberships_memory_brain_id'
          AND connamespace = 'opencortex'::regnamespace
    ) THEN
        ALTER TABLE opencortex.customer_memberships
            ADD CONSTRAINT fk_customer_memberships_memory_brain_id
            FOREIGN KEY (memory_brain_id)
            REFERENCES opencortex.brains(brain_id)
            ON DELETE SET NULL;
    END IF;
END $$;

UPDATE opencortex.customer_memberships AS membership
SET memory_brain_id = users.memory_brain_id
FROM opencortex.users AS users
JOIN opencortex.brains AS brains
    ON brains.brain_id = users.memory_brain_id
   AND brains.customer_id = membership.customer_id
WHERE membership.user_id = users.user_id
  AND membership.memory_brain_id IS NULL
  AND users.memory_brain_id IS NOT NULL;

COMMENT ON COLUMN opencortex.customer_memberships.memory_brain_id IS
'Preferred managed-content brain for agent memory storage within this customer membership. NULL means auto-select the only active managed-content brain or require configuration.';

INSERT INTO opencortex.schema_migrations (migration_id)
VALUES ('0009a_customer_membership_memory_brain')
ON CONFLICT (migration_id) DO NOTHING;
