# OpenCortex

OpenCortex is a Markdown-first memory runtime for AI agents.

It is designed to turn human-maintained knowledge into an agent-accessible memory layer by combining linked Markdown documents, structural graph relationships, semantic retrieval, and a single MCP interface.

## Status

OpenCortex is in planning and bootstrap mode.

The working build plan lives outside the repo in a local, user-configurable notes location.

The core concept notes currently live outside the repo alongside the rest of the local planning documents.

## What OpenCortex Aims To Provide

- Markdown as the source of truth for durable knowledge
- Linked-document graph traversal using wiki-style relationships
- Embeddings and hybrid retrieval for semantic context discovery
- Explainable context packs instead of opaque search results
- A single MCP server interface for agent access

## Planned Repository Shape

```text
OpenCortex/
  docs/
  src/
  tests/
  knowledge/
  infra/
  scripts/
```

## Initial Architecture Direction

- `.NET / C#` for the runtime and MCP server
- `Postgres` as the primary metadata store
- `pgvector` for embeddings
- Markdown parsing, chunking, link extraction, and indexing pipelines
- Hybrid retrieval that combines keyword, semantic, and graph signals
- Docker-based local development infrastructure

## Licensing

OpenCortex is licensed under `AGPL-3.0`.

That keeps the code open while requiring network-hosted modifications to remain available under the same license.

## Contributions

OpenCortex is currently limiting outside code contributions while the architecture, licensing, and commercialization strategy are still being defined.

See `CONTRIBUTING.md` for the current contribution policy and `CLA.md` for the contributor assignment terms expected for accepted contributions.
