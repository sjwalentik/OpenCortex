# MCP Security

## Overview

The MCP server is the agent-facing interface to OpenCortex. Hosted cloud users connect AI clients (Claude Desktop, GPT, local models, custom agents) to their personal brain via MCP using personal API tokens.

Firebase Auth session tokens are not suitable for MCP because they are short-lived (1-hour TTL), require the Firebase SDK to refresh, and are designed for browser sessions. MCP clients run headless and long-lived.

## Personal API Tokens

Users generate long-lived opaque tokens from account settings. These are used exclusively for MCP and programmatic API access from non-browser clients.

### Token Format

```
oct_<base62-encoded 32+ random bytes>
```

Example: `oct_7hQkLpX2mNvRwZ9dYf3jCsAb8eGtUo1`

- prefix `oct_` identifies OpenCortex tokens in secret scanners and logs
- 32+ bytes of cryptographically random data
- base62 encoded for URL and config file safety

### Storage

- raw token shown **once** at creation and never stored
- `SHA-256(token)` stored in `api_tokens.token_hash`
- first 8 characters stored in `token_prefix` for display only (e.g. `oct_7hQk...`)
- on each request: hash the presented token, look up by `token_hash`

## Database Table

Added in migration 0005.

```sql
CREATE TABLE opencortex.api_tokens (
    api_token_id    text PRIMARY KEY,
    user_id         text NOT NULL REFERENCES opencortex.users(user_id) ON DELETE CASCADE,
    customer_id     text NOT NULL REFERENCES opencortex.customers(customer_id),
    name            text NOT NULL,
    token_hash      text NOT NULL UNIQUE,
    token_prefix    text NOT NULL,
    scopes          text[] NOT NULL DEFAULT ARRAY['mcp:read'],
    expires_at      timestamptz NULL,
    last_used_at    timestamptz NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    revoked_at      timestamptz NULL
);

CREATE INDEX ix_api_tokens_user_id    ON opencortex.api_tokens(user_id);
CREATE INDEX ix_api_tokens_token_hash ON opencortex.api_tokens(token_hash);
```

## Scopes

| Scope | Plans | Description |
|---|---|---|
| `mcp:read` | Free, Pro, Teams | Query brain via OQL, list documents |
| `mcp:write` | Pro, Teams | Trigger reindex, create/edit documents via MCP |

Free plan tokens are issued with `mcp:read` only. Attempting a write operation with a read-only token returns `403`.

## Authentication Flow

1. user generates token in account settings, copies it once
2. user configures MCP client with token:

```json
{
  "mcpServers": {
    "opencortex": {
      "url": "https://mcp.opencortex.app",
      "headers": {
        "Authorization": "Bearer oct_7hQkLpX2mNvRwZ9dYf3jCsAb8eGtUo1"
      }
    }
  }
}
```

3. MCP server middleware on every request:
   - extracts token from `Authorization: Bearer` header
   - computes `SHA-256(token)`
   - looks up `api_tokens` by `token_hash`
   - checks `revoked_at IS NULL` and `(expires_at IS NULL OR expires_at > now())`
   - resolves `user_id`, `customer_id`, `scopes`
   - updates `last_used_at`
   - attaches resolved context to request

4. all MCP tool calls are scoped to the resolved `customer_id` and their default brain

## MCP Endpoint

- exposed at `https://mcp.opencortex.app` or `/mcp` on the main domain
- HTTPS only via Traefik + cert-manager
- separate Kubernetes Ingress from the main app (allows independent rate limiting)
- returns `401` for missing or invalid tokens
- returns `403` for valid token with insufficient scope

## Token Management UI

Located at `/app/settings/tokens`.

- list all tokens: name, prefix, scopes, created date, last used date, revoked status
- create new token: enter name, select scopes, click generate
- token displayed once in a copy modal with "I have saved this" confirmation
- revoke: immediate, no grace period
- no way to view the full token after creation

Current repo status:

- migration `0005_api_tokens.sql` now creates `api_tokens`
- tenant token management routes are live at `GET /tenant/tokens`, `POST /tenant/tokens`, and `DELETE /tenant/tokens/{apiTokenId}`
- a separate `OpenCortex.Portal` project now hosts the first customer-facing workspace instead of reusing the admin/debug console
- the portal now handles Firebase email/password browser sign-in and refreshes its session before calling the tenant token routes
- the portal now covers token settings plus managed-content document browsing and browser-side CRUD, including Firebase-native Google sign-in when the Google provider is enabled in Firebase Authentication; broader account UI is still pending
- raw tokens are generated with the `oct_` prefix, SHA-256 hashed before storage, and returned only once on creation
- MCP is now mapped explicitly at `/mcp` and requires `Authorization: Bearer oct_...`
- MCP middleware validates token hash, expiry, revocation, and `mcp:read`, updates `last_used_at`, and attaches resolved `customer_id` to the request
- `list_brains`, `get_brain`, and `query_brain` are now scoped to the authenticated customer's brains instead of the global catalog
- `query_brain` now consumes the same monthly `mcp.queries.YYYY-MM` usage counter used by hosted tenant queries
- managed-content MCP write tools now exist: `create_document`, `update_document`, `delete_document`, and `reindex_brain`
- MCP write tools require both token scope `mcp:write` and an effective plan with `mcpWrite = true`
- MCP document create enforces the same effective `maxDocuments` limit as the hosted tenant API, and MCP create/delete/reindex reconcile `documents.active`
- MCP overage currently surfaces as a tool error message; transport-level `429` shaping and broader portal coverage are still pending

## Rate Limiting

| Plan | MCP queries/month | Enforcement |
|---|---|---|
| Free | 100 | Hard limit via `usage_counters` |
| Pro | Unlimited | No limit |
| Teams | Unlimited | No limit |

Target behavior is `429` with a structured upgrade response. Current repo behavior enforces overage in the tool layer and returns a query error message until custom MCP transport error shaping is added.

## Security Properties

- tokens are opaque: no user information encoded in the token itself
- SHA-256 storage: a DB breach does not expose raw tokens
- prefix display: users can identify tokens without the full value
- `last_used_at`: helps detect stale or unexpected token usage
- revocation is immediate: hash lookup fails instantly for revoked tokens
- scope and plan enforcement: read-only or free-plan tokens cannot trigger indexing or writes

## Error Responses

| Condition | HTTP Status | Response |
|---|---|---|
| Missing token | 401 | `{"type":"unauthorized","title":"API token required"}` |
| Invalid or expired token | 401 | `{"type":"unauthorized","title":"Invalid or expired token"}` |
| Revoked token | 401 | `{"type":"unauthorized","title":"Token has been revoked"}` |
| Insufficient scope | 403 | `{"type":"forbidden","title":"Insufficient token scope","requiredScope":"mcp:write"}` |
| Rate limit exceeded | 429 | `{"type":"quota_exceeded","title":"MCP query limit reached","upgradeUrl":"/billing/upgrade"}` |

## Future: OAuth 2.0

When MCP clients support OAuth 2.0 device flow or PKCE, Firebase Auth OIDC can be used directly without manual token generation. Personal tokens remain as a fallback for headless and CLI clients. This is not in scope for MVP.
