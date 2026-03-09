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

## Rate Limiting

| Plan | MCP queries/month | Enforcement |
|---|---|---|
| Free | 100 | Hard limit via `usage_counters` |
| Pro | Unlimited | No limit |
| Teams | Unlimited | No limit |

Rate limit exceeded returns `429` with a structured response including `upgradeUrl`.

## Security Properties

- tokens are opaque: no user information encoded in the token itself
- SHA-256 storage: a DB breach does not expose raw tokens
- prefix display: users can identify tokens without the full value
- `last_used_at`: helps detect stale or unexpected token usage
- revocation is immediate: hash lookup fails instantly for revoked tokens
- scope enforcement: free plan tokens cannot trigger indexing or writes

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
