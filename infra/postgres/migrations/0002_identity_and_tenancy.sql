CREATE TABLE IF NOT EXISTS opencortex.users (
    user_id text PRIMARY KEY,
    external_id text NOT NULL UNIQUE,
    email text NOT NULL UNIQUE,
    display_name text NOT NULL,
    avatar_url text NULL,
    status text NOT NULL DEFAULT 'active',
    created_at timestamptz NOT NULL DEFAULT now(),
    last_seen_at timestamptz NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE opencortex.customers
    ADD COLUMN IF NOT EXISTS owner_user_id text NULL,
    ADD COLUMN IF NOT EXISTS stripe_customer_id text NULL,
    ADD COLUMN IF NOT EXISTS plan_id text NOT NULL DEFAULT 'free';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_customers_owner_user_id'
          AND connamespace = 'opencortex'::regnamespace
    ) THEN
        ALTER TABLE opencortex.customers
            ADD CONSTRAINT fk_customers_owner_user_id
            FOREIGN KEY (owner_user_id)
            REFERENCES opencortex.users(user_id);
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_customers_owner_user_id
    ON opencortex.customers(owner_user_id)
    WHERE owner_user_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_customers_stripe_customer_id
    ON opencortex.customers(stripe_customer_id)
    WHERE stripe_customer_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS opencortex.customer_memberships (
    membership_id text PRIMARY KEY,
    user_id text NOT NULL REFERENCES opencortex.users(user_id) ON DELETE CASCADE,
    customer_id text NOT NULL REFERENCES opencortex.customers(customer_id) ON DELETE CASCADE,
    role text NOT NULL DEFAULT 'owner',
    invited_by text NULL REFERENCES opencortex.users(user_id),
    joined_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE(user_id, customer_id)
);

CREATE INDEX IF NOT EXISTS ix_customer_memberships_user_id
    ON opencortex.customer_memberships(user_id);

CREATE INDEX IF NOT EXISTS ix_customer_memberships_customer_id
    ON opencortex.customer_memberships(customer_id);

INSERT INTO opencortex.schema_migrations (migration_id)
VALUES ('0002_identity_and_tenancy')
ON CONFLICT (migration_id) DO NOTHING;
