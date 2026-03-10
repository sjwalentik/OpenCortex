# Auth And Identity

## Overview

All hosted cloud API routes require authentication. The operator/admin surface requires a separate elevated role. Self-hosted deployments have no user auth requirement.

## Identity Provider

**Firebase Auth** is the chosen identity provider.

Reasons:
- very generous free tier (10k+ MAU free, then pay-per-use)
- Google social login is first-class
- standard JWKS endpoint: JWT validation in ASP.NET Core middleware requires no custom SDK
- does not require adopting any other Firebase service
- no ongoing per-seat cost for individual user plans

## Authentication Flow

1. user signs up or logs in via Firebase Auth
2. Firebase issues a short-lived ID token (JWT, 1-hour TTL)
3. browser sends `Authorization: Bearer <firebase-id-token>` on every API request
4. ASP.NET Core JWT middleware validates against Firebase's JWKS endpoint
5. middleware extracts `sub` (Firebase UID) and attaches to request context
6. request handler resolves `user_id`, `customer_id`, `role`, `plan_id` from DB

Current repo status:

- `OpenCortex.Api` validates Firebase ID tokens server-side for all `/tenant/*` routes
- `OpenCortex.Portal` now owns the browser auth flow for the hosted customer workspace
- the current portal auth slice uses portal-backed Firebase email/password login, Firebase-native Google popup sign-in, and refresh-token renewal
- a richer hosted account shell is still pending

## User Provisioning

On first login (triggered by Firebase `user.created` webhook or lazy on first API call):

1. create `users` record keyed by `external_id = firebase_uid`
2. create personal `customers` record (`plan_id = 'free'`)
3. create `customer_memberships` record (`role = 'owner'`)
4. create default personal `brains` record (`mode = 'managed-content'`, `status = 'active'`)
5. create or ensure mirrored `subscriptions` record (`plan_id = 'free'`, `status = 'active'`)

This gives every new user a ready-to-use personal workspace with one brain.

## Request Context

Every authenticated tenant API handler receives a resolved context containing:

- `UserId` — internal user ID
- `CustomerId` — active workspace ID
- `Role` — user's role in that workspace (owner/admin/editor/viewer)
- `PlanId` — current subscription plan (free/pro/teams/enterprise)

`PlanId` resolves from the mirrored `subscriptions` row, with `customers.plan_id` retained only as legacy bootstrap metadata. It is never derived from the request body or URL.

## User Model

```sql
CREATE TABLE opencortex.users (
    user_id         text PRIMARY KEY,
    external_id     text NOT NULL UNIQUE, -- Firebase Auth UID
    email           text NOT NULL UNIQUE,
    display_name    text NOT NULL,
    avatar_url      text NULL,
    status          text NOT NULL DEFAULT 'active',
    created_at      timestamptz NOT NULL DEFAULT now(),
    last_seen_at    timestamptz NULL,
    updated_at      timestamptz NOT NULL DEFAULT now()
);
```

## Customer Membership Model

Links users to workspaces. A user can be a member of multiple workspaces (personal + team accounts).

```sql
CREATE TABLE opencortex.customer_memberships (
    membership_id   text PRIMARY KEY,
    user_id         text NOT NULL REFERENCES opencortex.users(user_id),
    customer_id     text NOT NULL REFERENCES opencortex.customers(customer_id),
    role            text NOT NULL DEFAULT 'owner',
    invited_by      text NULL REFERENCES opencortex.users(user_id),
    joined_at       timestamptz NOT NULL DEFAULT now(),
    UNIQUE(user_id, customer_id)
);
```

## Roles

| Role | Read | Write | Invite | Manage billing | Manage brains |
|---|---|---|---|---|---|
| Viewer | Yes | No | No | No | No |
| Editor | Yes | Yes | No | No | No |
| Admin | Yes | Yes | Yes | No | Yes |
| Owner | Yes | Yes | Yes | Yes | Yes |

## Operator Role

Operator is a separate concept from tenant roles. Operator access:
- is not issued through Firebase Auth to end users
- requires a separate elevated JWT or internal-only routing
- grants access to `/admin/*` and `/indexing/*` routes
- is never granted to tenant users

## Route Auth Requirements

| Route group | Auth required | Auth type |
|---|---|---|
| `GET /health` | No | — |
| `GET /` | No | — |
| `/documents/*` | Yes | Firebase JWT |
| `/brains/*` (tenant) | Yes | Firebase JWT |
| `/query` | Yes | Firebase JWT or API token |
| `/billing/*` | Yes | Firebase JWT |
| `/tokens/*` | Yes | Firebase JWT |
| MCP endpoint | Yes | Personal API token (`oct_xxx`) |
| `/admin/*` | Yes | Operator role |
| `/indexing/*` | Yes | Operator role |
| `/webhooks/stripe` | No JWT | Stripe signature |
| `/webhooks/identity` | No JWT | Provider secret |

## Multi-Workspace

- a user can be a member of multiple workspaces
- default active workspace is their personal workspace
- workspace switch does not require re-authentication: resolve new `customer_id` from membership records
- all API requests are scoped to the active `customer_id`

## Security Rules

- JWTs validated server-side on every request using Firebase's public JWKS
- expired or invalid tokens return `401`
- subscription and quota state resolved from DB, never from JWT claims
- `customer_id` is never accepted from the request body for access control decisions
- rate limiting applied per `user_id` on write endpoints
