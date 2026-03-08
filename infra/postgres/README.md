# Postgres Local Development

OpenCortex uses Postgres plus `pgvector` as the default local and self-hosted backend.

## Start Local Infrastructure

```bash
docker compose -f infra/compose/docker-compose.yml up -d
```

## Default Connection Settings

- host: `localhost`
- port: `5432`
- database: `opencortex`
- username: `opencortex`
- password: `opencortex`

These defaults match the starter `appsettings.json` files in the API, MCP server, and worker projects.

## Migrations

Migration SQL lives in `infra/postgres/migrations/`.

Apply it manually with either:

```powershell
./scripts/apply-postgres-migrations.ps1 -ConnectionString "postgresql://user:password@host:5432/opencortex"
```

or plain `psql`:

```bash
psql "postgresql://user:password@host:5432/opencortex" -v ON_ERROR_STOP=1 -f infra/postgres/migrations/0001_initial_schema.sql
```

For cluster-managed or secret-backed setups, keep the real connection string in user secrets or deployment secrets rather than committed config.
