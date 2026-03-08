# OQL And Retrieval

## Purpose

OQL is the agent-facing query language for OpenCortex.

It is exposed primarily through the MCP server and represents the stable product-level query surface. The backing storage engine may evolve, but OQL should remain the OpenCortex-native contract.

## Goals

- provide a consistent query surface for agents
- express brain-scoped retrieval without exposing raw SQL
- support hybrid retrieval over metadata, text, vectors, and graph relationships
- produce explainable results that can be turned into context packs

## v0 Constraints

- query-only, not a mutation language
- exactly one brain per query
- no cross-brain joins
- no arbitrary user-defined functions

## Current Implementation Snapshot

- OQL is parsed and executed today through the API and MCP-adjacent runtime layers
- queries are brain-scoped and support `SEARCH`, `WHERE`, `RANK`, and `LIMIT`
- current `WHERE` support is intentionally narrow: equality filters joined by `AND`
- current metadata filters cover document `tag`, `title`, `path`, and `type`
- rank modes currently include `keyword`, `semantic`, and `hybrid`
- hybrid retrieval combines text signals, pgvector similarity, and graph-aware boosts from persisted link edges
- results already return reasons and scores, but explainability depth still needs improvement

## Conceptual Query Shape

```text
FROM brain("customer-a")
SEARCH "retention strategy"
WHERE tag = "roadmap" AND updated >= -30d
RANK hybrid
LIMIT 10
```

The final grammar can change, but v0 should preserve these concepts:

- target brain
- search terms or semantic intent
- metadata filters
- ranking mode
- limits and ordering

## Retrieval Inputs

- keyword terms
- document path constraints
- title constraints
- tags and document type filters
- recency filters
- link-aware operators such as backlink or neighbor bias
- rank mode: keyword, semantic, or hybrid

## Retrieval Pipeline

### Parser

Converts OQL text into an AST.

### Planner

Builds an execution plan from the AST.

Plan outputs may include:

- metadata filters
- text search clauses
- vector lookup requirements
- graph traversal or edge boosts
- ranking and limit settings

### Executor

Runs the plan against the storage providers.

In the default implementation this means Postgres plus pgvector plus graph-aware ranking logic.

### Formatter

Returns ranked, explainable results that can be emitted directly or assembled into context packs.

## Explainability

OpenCortex should explain why a result appeared, for example:

- keyword match on title
- semantic similarity to query
- linked from a high-relevance document
- recently updated and tag-matched

The current implementation already returns a reason string per result, but richer score breakdowns and clearer hybrid explanations remain active follow-up work.

## Storage Independence

OQL must not depend on Postgres-specific syntax.

The system should be structured so that:

- OQL stays stable
- planner contracts stay stable
- executor/provider implementations can change later

## MCP Alignment

MCP tools should use OQL directly or compile structured tool inputs into OQL-compatible plans.

Likely tool shapes:

- `query_brain`
- `get_document`
- `get_related_documents`
- `build_context_pack`

## Remaining v0 Retrieval Work

- deepen hybrid score breakdowns so results explain keyword, semantic, and graph contributions more clearly
- expand executable grammar coverage as new OQL clauses are added
- shape ranked results into more intentional context-pack outputs
- preserve storage independence while keeping Postgres plus pgvector the default implementation
