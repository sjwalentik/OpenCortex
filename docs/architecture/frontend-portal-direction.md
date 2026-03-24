# Frontend Portal Direction

## Decision

OpenCortex should standardize the customer portal on **React + TypeScript**.

The current `OpenCortex.Portal` implementation now uses a React + TypeScript shell as the customer entrypoint. The original server-served HTML, CSS, and plain browser JavaScript portal has been removed, and legacy paths now redirect into the React shell for compatibility.

Blazor is not the recommended direction for the portal.

## Why This Decision Exists

The portal is no longer a simple settings page. It now needs to support:

- a richer managed-content document editor
- graph-aware navigation and interaction
- multi-view tenant workflows with shared client state
- browser-native auth and session refresh
- retrieval diagnostics, tool setup, and eventually collaboration-aware authoring

These are all stronger fits for the modern JavaScript UI ecosystem than for Blazor.

## Recommended Stack

- framework: React
- language: TypeScript
- graph canvas: React Flow
- rich text editor: Tiptap
- styling: keep repo-owned CSS initially; adopt component-local structure without introducing a heavy UI kit by default
- backend boundary: keep the existing ASP.NET tenant API and portal host

## Why React

- React is a better fit for highly interactive browser surfaces than continuing to grow ad hoc vanilla JS.
- React has the strongest path for the two hardest UI problems on the roadmap: graph interaction and rich text editing.
- React lets the portal move to component boundaries, testable state, and incremental migration without forcing a backend rewrite.
- React keeps the current .NET backend and routing model intact. This is a frontend change, not a platform rewrite.

## Why Not Blazor

- Blazor does not remove JavaScript from this product in practice. Best-in-class graph and editor tooling still lives in the JavaScript ecosystem.
- The portal already depends on browser-native behavior such as Firebase auth flows, MCP setup copy patterns, client-side document handling, and retrieval tooling. React matches that environment directly.
- Choosing Blazor here would optimize for language uniformity over product feel, library fit, and implementation speed.

If OpenCortex were primarily a forms-heavy internal line-of-business app, Blazor would be more compelling. That is not this portal.

## Current Cutover Status

The React cutover is now in place for the signed-in workspace.

- `/` and `/app` route to the React portal
- `/index.html` and `/legacy` redirect into the React portal for compatibility
- Documents, Account, Usage, and Tools now run through React
- the Tools view now includes live workspace context alongside retrieval smoke tests, MCP setup, full document fetch, and indexing activity
- signed-out auth now runs directly inside the React portal

## Graph Direction

Use **React Flow** for the first graph surface.

It fits the expected OpenCortex interaction model better than a generic charting library:

- node-based workspace or document navigation
- background grid and snap-to-grid behavior
- custom node rendering for documents, chunks, or related concepts
- handles, toolbars, mini-map, zoom, and pan as first-class primitives
- room for future editing interactions instead of read-only visualization only

If OpenCortex later needs dense analytical network visualization rather than a workspace-style node canvas, add Cytoscape.js for that specific problem instead of forcing one graph library to do both jobs.

## Editor Direction

Use **Tiptap** as the document editor layer.

Reasons:

- it gives a more natural editing experience than a raw textarea-driven Markdown editor
- it has a strong extension model for slash commands, mentions, wiki-link autocomplete, embeds, and richer document UX
- it leaves room for collaboration and structured document behavior later without committing to that immediately

OpenCortex should stay Markdown-first at the storage boundary. The editor can still provide a richer in-browser authoring experience as long as canonical document save/export remains predictable.

## Migration Strategy

Do not rewrite the whole portal in one step. Migrate in slices.

### Phase 1

Completed.

- kept `OpenCortex.Portal` as the host project
- added a frontend app entrypoint under the existing portal static assets
- introduced React + TypeScript build output for the customer-facing UI shell
- preserved current tenant API routes and auth backend contracts

### Phase 2

Substantially completed.

- migrated the shared portal shell, routing, and auth/session state
- moved Documents, Account, Usage, and Tools into React views
- kept the current Markdown editor behavior functionally equivalent during the first pass

### Phase 3

- replace the current document editor surface with Tiptap
- carry forward rendered content, version history, restore, import, export, and path-aware identity
- add wiki-link assistance and richer authoring affordances on top of the new editor

### Phase 4

- add React Flow for graph-aware navigation and document relationship exploration
- connect graph interactions to the same tenant document/query APIs instead of inventing a parallel backend

## Testing Implications

Backend route and service tests should continue regardless of frontend choice.

Frontend test infrastructure should follow the React cutover, not precede it.

- add unit/component tests around shared React view logic
- add a small number of browser-level end-to-end tests for sign-in, document editing, and retrieval tooling
- avoid rebuilding a parallel legacy shell now that React is the only customer portal surface

## Non-Goals

- replacing the tenant API
- replacing ASP.NET hosting
- moving MCP logic into the frontend
- adopting a heavy design system before the product interaction model is stable

## Current Recommendation For Repo Work

The React cutover is now in place. New portal work should target the React shell and focus the next frontend pass on Tiptap, graph-aware navigation, and frontend test coverage rather than rebuilding the removed legacy plain-JS surface.

