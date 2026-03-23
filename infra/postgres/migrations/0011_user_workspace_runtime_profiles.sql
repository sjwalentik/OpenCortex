CREATE TABLE IF NOT EXISTS opencortex.user_workspace_runtime_profiles (
    user_id TEXT PRIMARY KEY,
    profile_id TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE OR REPLACE FUNCTION opencortex.update_user_workspace_runtime_profiles_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_user_workspace_runtime_profiles_updated_at
    ON opencortex.user_workspace_runtime_profiles;
CREATE TRIGGER trg_user_workspace_runtime_profiles_updated_at
    BEFORE UPDATE ON opencortex.user_workspace_runtime_profiles
    FOR EACH ROW
    EXECUTE FUNCTION opencortex.update_user_workspace_runtime_profiles_updated_at();

COMMENT ON TABLE opencortex.user_workspace_runtime_profiles IS
    'User-global workspace runtime profile preferences used to select managed agent runtime images.';

INSERT INTO opencortex.schema_migrations (migration_id)
VALUES ('0011_user_workspace_runtime_profiles')
ON CONFLICT (migration_id) DO NOTHING;
