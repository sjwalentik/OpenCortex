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
- results include a `ScoreBreakdown` with separate `KeywordScore`, `SemanticScore`, and `GraphScore` fields
- each result carries a human-readable `Reason` string built in C# from active signals, e.g. `"title match (2.00) + semantic similarity (0.85) + graph boost ×3 (0.45)"`
- `OqlQueryExecutionResult` includes an `ExecutionSummary` with per-signal result counts and min/max score across the result set

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

OpenCortex explains why each result appeared. The current implementation provides:

- `ScoreBreakdown` per result: separate `KeywordScore`, `SemanticScore`, and `GraphScore` values
- `Reason` string per result constructed in C# from active signals, e.g. `"title match (2.00) + semantic similarity (0.85) + graph boost ×3 (0.45)"`
- `ExecutionSummary` on the query result: counts of keyword-matched, semantic-matched, and graph-boosted results, plus min/max score across the result set

The Postgres implementation selects these scores as separate columns rather than building opaque reason strings in SQL, making the breakdown testable in isolation.

Examples of signals that appear in reason strings:

- keyword match on title or content
- semantic similarity to query
- graph boost from linked document edges
- combinations of the above with per-signal weights

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

The expected read workflow is two-step: use `query_brain` to discover ranked candidates, then call `get_document` with the returned `documentId` or `canonicalPath` when the agent needs the full stored Markdown instead of a snippet.

For managed-content write workflows, prefer `save_document` with a canonical path instead of forcing the agent to create first and then remember a managed document ID for future updates. Deletion can now follow the same path-first model through `delete_document` with `canonical_path`.

## Remaining v0 Retrieval Work

- expand executable grammar coverage as new OQL clauses are added
- shape ranked results into more intentional context-pack outputs
- preserve storage independence while keeping Postgres plus pgvector the default implementation
- harden MCP tool contracts around OQL execution (brain scoping, result formatting, error handling)
