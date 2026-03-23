# Manual Postgres Setup

## Purpose

OpenCortex keeps Postgres setup generic for open source usage, but operators can point the runtime at any Postgres instance they control, including a cluster-managed deployment.

## Recommended Operator Flow

1. create a Postgres database for OpenCortex
2. ensure the `pgvector` extension is available in that database
3. set the connection string in local user secrets or deployment secrets
4. choose the embedding provider settings in local secrets or deployment secrets
5. apply the SQL migrations manually
6. start the API, MCP server, and workers against that database

## Connection String

Use the `OpenCortex:Database:ConnectionString` setting.

For local development with user secrets:

```bash
dotnet user-secrets init --project src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Database:ConnectionString" "Host=your-host;Port=5432;Database=opencortex;Username=your-user;Password=your-password" --project src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Database:ConnectionString" "Host=your-host;Port=5432;Database=opencortex;Username=your-user;Password=your-password" --project src/OpenCortex.McpServer
dotnet user-secrets set "OpenCortex:Database:ConnectionString" "Host=your-host;Port=5432;Database=opencortex;Username=your-user;Password=your-password" --project src/OpenCortex.Workers
```

## Embedding Provider Settings

Default open source setup:

```bash
dotnet user-secrets set "OpenCortex:Embeddings:Provider" "deterministic" --project src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Embeddings:Provider" "deterministic" --project src/OpenCortex.McpServer
dotnet user-secrets set "OpenCortex:Embeddings:Provider" "deterministic" --project src/OpenCortex.Workers
```

OpenAI-compatible setup:

```bash
dotnet user-secrets set "OpenCortex:Embeddings:Provider" "openai-compatible" --project src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Embeddings:Model" "text-embedding-3-small" --project src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Embeddings:Dimensions" "1536" --project src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Embeddings:Endpoint" "https://your-provider/v1/embeddings" --project src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Embeddings:ApiKey" "your-secret" --project src/OpenCortex.Api
```

Repeat the same settings for `src/OpenCortex.McpServer` and `src/OpenCortex.Workers`.

Dimension alignment matters:

- `infra/postgres/migrations/0001_initial_schema.sql` creates `opencortex.embeddings.vector` as `vector(1536)`
- your configured `OpenCortex:Embeddings:Dimensions` must match the actual embedding size returned by your provider and the `pgvector` column dimension
- if you use a `768`-dimension model, for example `nomic-embed-text`, alter the `opencortex.embeddings.vector` column to `vector(768)` and recreate `ix_embeddings_vector` before indexing content

## Applying Migrations

Option 1: run the PowerShell helper.

```powershell
./scripts/apply-postgres-migrations.ps1 -ConnectionString "postgresql://user:password@host:5432/opencortex"
```

Option 2: apply the SQL files manually in order.

```bash
psql "postgresql://user:password@host:5432/opencortex" -v ON_ERROR_STOP=1 -f infra/postgres/migrations/0001_initial_schema.sql
```

## Current Migration Set

- `infra/postgres/migrations/0001_initial_schema.sql`
- `infra/postgres/migrations/0002_identity_and_tenancy.sql`
- `infra/postgres/migrations/0003_billing_schema.sql`
- `infra/postgres/migrations/0004_managed_content.sql`
- `infra/postgres/migrations/0005_api_tokens.sql`
- `infra/postgres/migrations/0006_managed_document_versions.sql`
- `infra/postgres/migrations/0007_conversations.sql`
- `infra/postgres/migrations/0008_user_provider_configs.sql`
- `infra/postgres/migrations/0009_user_memory_brain.sql`
- `infra/postgres/migrations/0009a_customer_membership_memory_brain.sql`
- `infra/postgres/migrations/0010_tenant_scoped_user_provider_configs.sql`

## Notes

- the migration files are intentionally plain SQL so operators can review and apply them in their own environments
- the runtime does not auto-apply migrations yet
- this keeps cluster-managed and self-hosted deployments predictable
