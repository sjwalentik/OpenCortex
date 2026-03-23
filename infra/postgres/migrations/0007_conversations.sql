-- Migration: 0007_conversations
-- Description: Add conversations and messages tables for multi-model orchestration

CREATE TABLE IF NOT EXISTS opencortex.conversations (
    conversation_id text PRIMARY KEY,
    brain_id text NULL REFERENCES opencortex.brains(brain_id) ON DELETE SET NULL,
    customer_id text NOT NULL REFERENCES opencortex.customers(customer_id) ON DELETE CASCADE,
    user_id text NULL REFERENCES opencortex.users(user_id) ON DELETE SET NULL,
    title text NULL,
    system_prompt text NULL,
    status text NOT NULL DEFAULT 'active',
    metadata jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    last_message_at timestamptz NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_conversations_customer_id
    ON opencortex.conversations(customer_id);

CREATE INDEX IF NOT EXISTS ix_conversations_user_id
    ON opencortex.conversations(user_id)
    WHERE user_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_conversations_brain_id
    ON opencortex.conversations(brain_id)
    WHERE brain_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_conversations_status
    ON opencortex.conversations(customer_id, status);

CREATE INDEX IF NOT EXISTS ix_conversations_last_message
    ON opencortex.conversations(customer_id, last_message_at DESC NULLS LAST)
    WHERE status = 'active';

CREATE TABLE IF NOT EXISTS opencortex.messages (
    message_id text PRIMARY KEY,
    conversation_id text NOT NULL REFERENCES opencortex.conversations(conversation_id) ON DELETE CASCADE,
    parent_message_id text NULL REFERENCES opencortex.messages(message_id) ON DELETE SET NULL,
    role text NOT NULL,
    content text NULL,
    provider_id text NULL,
    model_id text NULL,
    tool_calls jsonb NULL,
    token_usage jsonb NULL,
    latency_ms int NULL,
    metadata jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_messages_conversation_id
    ON opencortex.messages(conversation_id, created_at);

CREATE INDEX IF NOT EXISTS ix_messages_parent
    ON opencortex.messages(parent_message_id)
    WHERE parent_message_id IS NOT NULL;

-- Function to update conversation.updated_at on message insert
CREATE OR REPLACE FUNCTION opencortex.update_conversation_timestamp()
RETURNS TRIGGER AS $$
BEGIN
    UPDATE opencortex.conversations
    SET updated_at = now(),
        last_message_at = NEW.created_at
    WHERE conversation_id = NEW.conversation_id;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Trigger to auto-update conversation timestamp
DROP TRIGGER IF EXISTS trg_message_update_conversation ON opencortex.messages;
CREATE TRIGGER trg_message_update_conversation
    AFTER INSERT ON opencortex.messages
    FOR EACH ROW
    EXECUTE FUNCTION opencortex.update_conversation_timestamp();

-- Rolling summary table for long conversations
CREATE TABLE IF NOT EXISTS opencortex.conversation_summaries (
    summary_id text PRIMARY KEY,
    conversation_id text NOT NULL REFERENCES opencortex.conversations(conversation_id) ON DELETE CASCADE,
    summary_text text NOT NULL,
    message_range_start text NOT NULL REFERENCES opencortex.messages(message_id),
    message_range_end text NOT NULL REFERENCES opencortex.messages(message_id),
    message_count int NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_conversation_summaries_conversation
    ON opencortex.conversation_summaries(conversation_id, created_at);

INSERT INTO opencortex.schema_migrations (migration_id)
VALUES ('0007_conversations')
ON CONFLICT (migration_id) DO NOTHING;
