# Billing And Quotas

## Overview

OpenCortex uses Stripe for payment processing and subscription management. The app mirrors Stripe state into Postgres so quota and access decisions can be made without a live Stripe API call on every request.

## Plans

| Plan | Documents | Brains | MCP | Team members | Price |
|---|---|---|---|---|---|
| Free | 10 | 1 | Read-only, 100 queries/month | None | $0 |
| Pro | 500 | 3 | Full (read + write), unlimited | None | ~$12/month |
| Teams | 2000+ | 10+ | Full, unlimited | Per seat | ~$10-15/seat/month |
| Enterprise | Custom | Custom | Custom | Custom | Negotiated |

## Stripe Integration

### Checkout Flow

1. user clicks upgrade in the app
2. backend creates a Stripe Checkout Session
3. user is redirected to Stripe-hosted Checkout
4. on success, Stripe redirects to `/billing/success`
5. backend waits for the webhook to confirm and update subscription state

Never trust the redirect alone. Always wait for the webhook.

### Customer Portal

- user opens billing settings
- backend creates a Stripe Billing Portal session
- user is redirected to the Stripe-hosted portal for self-service billing management

### Webhook Events

All events hit `POST /webhooks/stripe` and are verified via the `Stripe-Signature` header.

| Event | Action |
|---|---|
| `checkout.session.completed` | Link Stripe customer to app customer; activate subscription |
| `customer.subscription.created` | Create or update subscription record; lift document quota |
| `customer.subscription.updated` | Update plan, status, cancellation flag, and billing period dates |
| `customer.subscription.deleted` | Mark cancelled; revert to free limits once paid access ends |
| `invoice.payment_succeeded` | Confirm active status and extend billing period dates |
| `invoice.payment_failed` | Mark `past_due` and preserve billing period dates for downgrade evaluation |

All webhook handlers are idempotent. Deduplicate on `stripe_event_id`.

## Subscription Table

Mirrors Stripe state and is read by every quota check.

```sql
CREATE TABLE opencortex.subscriptions (
    subscription_id         text PRIMARY KEY,
    customer_id             text NOT NULL UNIQUE REFERENCES opencortex.customers(customer_id),
    plan_id                 text NOT NULL DEFAULT 'free',
    status                  text NOT NULL DEFAULT 'active', -- active, past_due, cancelled, trialing
    stripe_customer_id      text NULL UNIQUE,
    stripe_subscription_id  text NULL UNIQUE,
    seat_count              integer NOT NULL DEFAULT 1,
    current_period_start    timestamptz NULL,
    current_period_end      timestamptz NULL,
    cancel_at_period_end    boolean NOT NULL DEFAULT false,
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now()
);
```

## Usage Counters

Tracked per customer via upsert on billable actions and reconciliations.

```sql
CREATE TABLE opencortex.usage_counters (
    customer_id     text NOT NULL REFERENCES opencortex.customers(customer_id),
    counter_key     text NOT NULL,
    value           bigint NOT NULL DEFAULT 0,
    reset_at        timestamptz NULL,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (customer_id, counter_key)
);
```

Counter key examples:
- `documents.active` - current live document count
- `mcp.queries.2026-03` - hosted query count for the month
- `indexing.runs.2026-03-08` - index runs for the day

## Quota Enforcement

### Document Check Flow

Before any hosted document create:

1. resolve the effective `planId` from mirrored subscription state
2. load plan limits from config
3. compare the active managed-document count against the plan limit
4. if usage is at or above the limit, return `402` with a structured upgrade response
5. on success, create the document and reconcile `documents.active`

### Hosted Query Check Flow

Before any hosted tenant query:

1. resolve the effective `planId` from mirrored subscription state
2. load `mcpQueriesPerMonth` from config
3. increment `mcp.queries.YYYY-MM` atomically for capped plans
4. if usage exceeds the limit, return `402` with a structured upgrade response
5. otherwise execute the query

### Quota Error Response (HTTP 402)

```json
{
  "type": "quota_exceeded",
  "title": "Document limit reached",
  "detail": "Your free plan allows 10 documents. Upgrade to Pro to add more.",
  "currentUsage": 10,
  "limit": 10,
  "planId": "free",
  "upgradeUrl": "/billing/upgrade"
}
```

### Plan Entitlements (Config-Backed)

Stored in config for v1 rather than a DB table.

```json
{
  "plans": {
    "free":  { "maxDocuments": 10,  "maxBrains": 1, "mcpQueriesPerMonth": 100,  "mcpWrite": false },
    "pro":   { "maxDocuments": 500, "maxBrains": 3, "mcpQueriesPerMonth": -1,   "mcpWrite": true  },
    "teams": { "maxDocuments": 2000,"maxBrains": 10,"mcpQueriesPerMonth": -1,   "mcpWrite": true  }
  }
}
```

`-1` means unlimited.

Current repo status:

- plan entitlements are config-backed under `OpenCortex:Billing:Plans`
- hosted tenant bootstrap ensures a mirrored `subscriptions` row exists for every personal workspace
- `GET /tenant/billing/plan` returns the workspace's effective plan, subscription mirror details, active document usage, and monthly hosted query usage
- effective quota resolution now handles grace-period cancellation and expired paid-plan downgrade behavior
- `POST /tenant/billing/upgrade` creates a Stripe Checkout session for the configured Pro price and reuses the linked Stripe customer when one exists
- `POST /tenant/billing/portal` creates a Stripe Customer Portal session for linked workspaces
- `POST /webhooks/stripe` verifies the `Stripe-Signature` header, deduplicates via `subscription_events`, and mirrors checkout, subscription, and invoice events into `subscriptions`
- Stripe subscription and invoice webhook handlers persist `current_period_start` and `current_period_end`
- `POST /tenant/brains/{brainId}/documents` enforces `maxDocuments` and reconciles `documents.active`
- `POST /tenant/query` increments `mcp.queries.YYYY-MM` and enforces `mcpQueriesPerMonth` on capped plans
- authenticated MCP `query_brain` calls now increment the same monthly counter after token-based customer resolution
- managed-content MCP write tools now reuse the same effective-plan checks for `mcp:write`; `save_document` and `create_document` enforce `maxDocuments` on create, and document counters are reconciled after create and delete
- MCP overage is enforced in the tool layer today; custom transport-level `429` shaping is still pending

## Grace Period On Cancellation

- cancellation uses Stripe `cancel_at_period_end = true`
- access continues through the end of the paid billing period
- once the paid period ends, effective quota falls back to `free`
- excess documents above free limit are soft-archived later rather than hard-deleted automatically

## Downgrade Handling

- expired `cancelled`, `incomplete_expired`, `past_due`, and `unpaid` paid subscriptions fall back to free-plan quota enforcement once paid access has ended
- excess documents should be archived rather than deleted
- standalone MCP access will use the same effective billing state once token-based identity exists

## Security

- `STRIPE_SECRET_KEY` and `STRIPE_WEBHOOK_SECRET` are stored in Kubernetes Secrets
- secrets are never committed to git or embedded in container images
- production should use least-privilege Stripe keys
- the webhook endpoint is protected by `Stripe-Signature` HMAC verification, not JWT

## Test Mode

Use Stripe test mode for all non-production environments.

```powershell
stripe listen --forward-to https://your-dev-url/webhooks/stripe
```

Test card: `4242 4242 4242 4242`, any future expiry, any CVC.
