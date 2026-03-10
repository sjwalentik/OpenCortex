# Hosted Workspace Local Setup

## Purpose

Use this guide when you want to run the current hosted/customer OpenCortex stack locally:

- `OpenCortex.Api`
- `OpenCortex.Portal`
- `OpenCortex.McpServer`
- local Postgres with all current migrations
- Firebase-backed customer auth
- optional Stripe billing

This is the fastest path to a working local customer workspace with:

- browser sign-in
- managed-content document authoring
- Markdown preview
- version history and restore
- MCP token management

## What You Need

Before starting, have these ready:

- Docker Desktop or another Docker runtime
- .NET 8 SDK
- `psql` available locally, or use the PowerShell migration helper
- a Firebase project
- optionally a Stripe account with a Pro price configured

## Step 1: Start Postgres

Start the local Postgres container:

```powershell
docker compose -f infra/compose/docker-compose.yml up -d
```

Default local database settings:

- host: `localhost`
- port: `5432`
- database: `opencortex`
- username: `opencortex`
- password: `opencortex`

## Step 2: Apply SQL Migrations

OpenCortex does **not** auto-apply migrations on startup yet.

Apply all migrations with the helper:

```powershell
./scripts/apply-postgres-migrations.ps1 -ConnectionString "postgresql://opencortex:opencortex@localhost:5432/opencortex"
```

Current migration set:

- `0001_initial_schema.sql`
- `0002_identity_and_tenancy.sql`
- `0003_billing_schema.sql`
- `0004_managed_content.sql`
- `0005_api_tokens.sql`
- `0006_managed_document_versions.sql`

## Step 3: Initialize User Secrets

Initialize secrets for the projects you plan to run:

```powershell
dotnet user-secrets init --project src/OpenCortex.Api
dotnet user-secrets init --project src/OpenCortex.McpServer
dotnet user-secrets init --project src/OpenCortex.Workers
dotnet user-secrets init --project src/OpenCortex.Portal
```

## Step 4: Configure Database Access

Set the same connection string for API, MCP server, and workers:

```powershell
dotnet user-secrets set "OpenCortex:Database:ConnectionString" "Host=localhost;Port=5432;Database=opencortex;Username=opencortex;Password=opencortex" --project src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Database:ConnectionString" "Host=localhost;Port=5432;Database=opencortex;Username=opencortex;Password=opencortex" --project src/OpenCortex.McpServer
dotnet user-secrets set "OpenCortex:Database:ConnectionString" "Host=localhost;Port=5432;Database=opencortex;Username=opencortex;Password=opencortex" --project src/OpenCortex.Workers
```

## Step 5: Configure Embeddings

For local development, keep the deterministic provider:

```powershell
dotnet user-secrets set "OpenCortex:Embeddings:Provider" "deterministic" --project src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Embeddings:Provider" "deterministic" --project src/OpenCortex.McpServer
dotnet user-secrets set "OpenCortex:Embeddings:Provider" "deterministic" --project src/OpenCortex.Workers
```

You do **not** need OpenAI or another embedding provider just to get the hosted workspace running locally.

Important:

- `infra/postgres/migrations/0001_initial_schema.sql` creates `opencortex.embeddings.vector` as `vector(1536)`
- your effective `OpenCortex:Embeddings:Dimensions` must match that column size
- the deterministic local default is `1536`
- if you switch to an `openai-compatible` model that returns `768` dimensions, such as `nomic-embed-text`, you must either:
  - keep the database aligned at `1536` by using a `1536`-dimension model instead
  - or alter `opencortex.embeddings.vector` to `vector(768)` and rebuild the vector index before indexing managed content or filesystem brains

Example `768`-dimension adjustment:

```sql
DROP INDEX IF EXISTS opencortex.ix_embeddings_vector;
DELETE FROM opencortex.embeddings;
ALTER TABLE opencortex.embeddings
  ALTER COLUMN vector TYPE vector(768);
CREATE INDEX IF NOT EXISTS ix_embeddings_vector
  ON opencortex.embeddings
  USING ivfflat (vector vector_cosine_ops)
  WITH (lists = 100);
```

## Step 6: Configure Firebase For Hosted Auth

The hosted customer path requires Firebase.

In Firebase, set up:

- a Firebase project
- Email/Password sign-in enabled
- Google sign-in enabled if you want Google browser login in the portal
- a Web app so you can obtain the Firebase Web API key

You need these values:

- Firebase project ID
- Firebase Web API key

### API settings

Enable hosted auth on the API:

```powershell
dotnet user-secrets set "OpenCortex:HostedAuth:Enabled" "true" --project src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:HostedAuth:FirebaseProjectId" "your-firebase-project-id" --project src/OpenCortex.Api
```

### Portal settings

Point the portal at the API and give it the Firebase browser-auth settings:

```powershell
dotnet user-secrets set "Portal:ApiBaseUrl" "https://localhost:7092/" --project src/OpenCortex.Portal
dotnet user-secrets set "Portal:Auth:FirebaseProjectId" "your-firebase-project-id" --project src/OpenCortex.Portal
dotnet user-secrets set "Portal:Auth:FirebaseApiKey" "your-firebase-web-api-key" --project src/OpenCortex.Portal
```

Notes:

- `Portal:ApiBaseUrl` must point at `OpenCortex.Api`
- the portal uses Firebase email/password REST endpoints for login/register/refresh
- Google sign-in in the portal uses the Firebase browser SDK and only requires that Google is enabled in Firebase Authentication for the same project
- if these settings are missing, the portal UI may load but sign-in and tenant routes will not work

## Step 7: Stripe Configuration (Optional)

You only need this if you want working local billing checkout, Stripe Customer Portal, and webhook processing right now.

Set these on the API:

```powershell
dotnet user-secrets set "OpenCortex:Billing:Stripe:Enabled" "true" --project src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Billing:Stripe:SecretKey" "sk_test_..." --project src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Billing:Stripe:WebhookSecret" "whsec_..." --project src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Billing:Stripe:AppBaseUrl" "https://localhost:7092" --project src/OpenCortex.Api
dotnet user-secrets set "OpenCortex:Billing:Stripe:PriceIds:pro" "price_..." --project src/OpenCortex.Api
```

If you are **not** testing billing yet, leave Stripe disabled.

The hosted workspace, tokens, authoring, preview, and version history still work without Stripe.

## Step 8: Run The Services

### Option A: Start the API directly

```powershell
dotnet run --project src/OpenCortex.Api
```

Then start the portal in another terminal:

```powershell
dotnet run --project src/OpenCortex.Portal
```

If you also want MCP:

```powershell
dotnet run --project src/OpenCortex.McpServer
```

### Option B: Use Aspire AppHost

```powershell
dotnet run --project src/OpenCortex.AppHost
```

The AppHost starts the .NET services together, but you still need Postgres running separately first.

## Step 9: First Local Smoke Test

Once the API and portal are running:

1. open the portal
2. create a Firebase-backed account or sign in
3. let the first `/tenant/me` call provision your personal workspace
4. create a managed-content document
5. verify the live preview updates
6. save the document
7. verify version history appears
8. restore a prior version and confirm the document updates
9. create an MCP token from the portal

## What Is Required Vs Optional

Required for the hosted customer workspace:

- Postgres running
- migrations applied
- API database connection string
- API hosted auth enabled with Firebase project ID
- portal API base URL
- portal Firebase project ID
- portal Firebase Web API key

Optional for now:

- Stripe settings
- openai-compatible embeddings
- filesystem brain/source-root config

## Common Failure Cases

### Portal loads but sign-in fails

Check:

- `Portal:Auth:FirebaseProjectId`
- `Portal:Auth:FirebaseApiKey`
- Firebase Email/Password provider is enabled

### Portal signs in but workspace calls fail

Check:

- `Portal:ApiBaseUrl`
- API is running
- API has `OpenCortex:HostedAuth:Enabled=true`
- API has the correct `OpenCortex:HostedAuth:FirebaseProjectId`

### API starts but tenant calls fail against Postgres

Check:

- connection string on API/MCP/workers
- local Postgres container is running
- all migrations through `0006` were applied

### Billing routes fail

Check:

- Stripe is enabled only when all Stripe values are present
- webhook secret matches your Stripe listener
- `PriceIds:pro` is set

## Related Docs

- [README](../../README.md)
- [manual-postgres-setup.md](./manual-postgres-setup.md)
- [roadmap.md](../product/roadmap.md)
- [auth-and-identity.md](../architecture/auth-and-identity.md)
- [billing-and-quotas.md](../architecture/billing-and-quotas.md)
- [mcp-security.md](../architecture/mcp-security.md)
