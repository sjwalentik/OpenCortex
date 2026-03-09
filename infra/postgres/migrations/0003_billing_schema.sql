CREATE TABLE IF NOT EXISTS opencortex.subscriptions (
    subscription_id text PRIMARY KEY,
    customer_id text NOT NULL UNIQUE REFERENCES opencortex.customers(customer_id) ON DELETE CASCADE,
    plan_id text NOT NULL DEFAULT 'free',
    status text NOT NULL DEFAULT 'active',
    stripe_customer_id text NULL UNIQUE,
    stripe_subscription_id text NULL UNIQUE,
    seat_count integer NOT NULL DEFAULT 1,
    current_period_start timestamptz NULL,
    current_period_end timestamptz NULL,
    cancel_at_period_end boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_subscriptions_plan_id
    ON opencortex.subscriptions(plan_id);

CREATE INDEX IF NOT EXISTS ix_subscriptions_status
    ON opencortex.subscriptions(status);

CREATE TABLE IF NOT EXISTS opencortex.subscription_events (
    subscription_event_id text PRIMARY KEY,
    customer_id text NOT NULL REFERENCES opencortex.customers(customer_id) ON DELETE CASCADE,
    stripe_event_id text NOT NULL UNIQUE,
    event_type text NOT NULL,
    payload jsonb NULL,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    processed_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_subscription_events_customer_id
    ON opencortex.subscription_events(customer_id);

CREATE INDEX IF NOT EXISTS ix_subscription_events_event_type
    ON opencortex.subscription_events(event_type);

CREATE TABLE IF NOT EXISTS opencortex.usage_counters (
    customer_id text NOT NULL REFERENCES opencortex.customers(customer_id) ON DELETE CASCADE,
    counter_key text NOT NULL,
    value bigint NOT NULL DEFAULT 0,
    reset_at timestamptz NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (customer_id, counter_key)
);

CREATE INDEX IF NOT EXISTS ix_usage_counters_counter_key
    ON opencortex.usage_counters(counter_key);

INSERT INTO opencortex.schema_migrations (migration_id)
VALUES ('0003_billing_schema')
ON CONFLICT (migration_id) DO NOTHING;
