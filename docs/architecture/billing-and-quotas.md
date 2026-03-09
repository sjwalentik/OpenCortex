# Billing And Quotas

## Overview

OpenCortex uses Stripe for all payment processing and subscription management. The app mirrors Stripe state into its own database so quota and access decisions can be made without live Stripe API calls on every request.

## Plans

| Plan | Documents | Brains | MCP | Team members | Price |
|---|---|---|---|---|---|
| Free | 10 | 1 | Read-only, 100 queries/month | None | $0 |
| Pro | 500 | 3 | Full (read + write), unlimited | None | ~$12/month |
| Teams | 2000+ | 10+ | Full, unlimited | Per seat | ~$10–15/seat/month |
| Enterprise | Custom | Custom | Custom | Custom | Negotiated |

## Stripe Integration

### Checkout Flow

1. user clicks upgrade in the app
2. backend creates a Stripe Checkout Session
3. user redirected to Stripe-hosted Checkout
4. on success, Stripe redirects to `/billing/success`
5. backend waits for webhook to confirm and update subscription state

Never trust the redirect alone. Always wait for the webhook.

### Customer Portal

- user opens billing settings
- backend creates a Stripe Billing Portal session
- user redirected to Stripe-hosted portal for self-service: update payment method, view invoices, downgrade, cancel

### Webhook Events

All events hit `POST /webhooks/stripe`, verified via `Stripe-Signature` header.

| Event | Action |
|---|---|
| `checkout.session.completed` | Link Stripe customer to app customer; activate subscription |
| `customer.subscription.created` | Create/update subscription record; lift document quota |
| `customer.subscription.updated` | Update plan, status, current period end |
| `customer.subscription.deleted` | Mark cancelled; revert to free limits at period end |
| `invoice.payment_succeeded` | Extend current period end |
| `invoice.payment_failed` | Mark `past_due`; trigger dunning notification |

All webhook handlers are idempotent. Deduplicate on `stripe_event_id`.

## Subscription Table

Mirrors Stripe state. Read by every quota check.

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

Tracked per customer via upsert on every billable action.

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
- `documents.active` — current live document count
- `mcp.queries.2026-03` — MCP query count for the month
- `indexing.runs.2026-03-08` — index runs for the day

## Quota Enforcement

### Check Flow

Before any document create or import:

1. resolve `planId` from subscription record
2. load plan limits from config
3. read `documents.active` counter for `customerId`
4. if `counter >= limit`: return `402` with structured error
5. else: proceed, then increment counter atomically

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

Stored as config in v1, not a DB table. Simpler to reason about and change.

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

## Grace Period On Cancellation

- cancellation uses Stripe `cancel_at_period_end = true`
- access continues through the end of the paid billing period
- at period end: subscription status set to `cancelled`, plan reverts to `free`
- excess documents above free limit are soft-archived (not deleted)
- user sees a list of documents to prune; no automatic hard deletion

## Downgrade Handling

- excess documents marked `archived` status: visible to user, not queryable via OQL
- user can prune to get back under free limit
- background job recalculates `documents.active` counter after archival

## Security

- `STRIPE_SECRET_KEY` and `STRIPE_WEBHOOK_SECRET` stored in Kubernetes Secrets
- never committed to git or embedded in container images
- Stripe Restricted Keys used in production (least privilege)
- webhook endpoint not protected by JWT, only by `Stripe-Signature` HMAC verification

## Test Mode

Use Stripe test mode for all non-production environments.

```powershell
stripe listen --forward-to https://your-dev-url/webhooks/stripe
```

Test card: `4242 4242 4242 4242`, any future expiry, any CVC.
