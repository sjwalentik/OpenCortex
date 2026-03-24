# Afterthought: Codex CLI Observability & Streaming Reasoning

**Priority**: Nice-to-Have (post-P4)
**Effort**: ~1 week
**Motivation**: Improve visibility into what the Codex CLI is doing inside the workspace pod — particularly reasoning/thinking content — to help with debugging, user trust, and long-running task transparency.

---

## Background

The Codex CLI provider (`CodexCliModelProvider`) currently:

1. Runs `codex exec --json ...` inside the workspace pod via `ExecuteCommandAsync`
2. **Buffers the entire stdout** until the process exits
3. Parses two event types from the JSON output: `item.completed` (agent message) and `turn.completed` (usage)
4. Yields the full response in a single shot via `StreamAsync` → `CompleteAsync`

This means:
- No intermediate progress during execution (only the SSE heartbeat shows "Running...")
- Reasoning tokens (if emitted by the model) are silently discarded
- Tool calls / file operations performed by Codex are invisible to the user
- Failures surface only as a generic error; the actual Codex stdout/stderr is not shown

---

## Goal

Stream the Codex CLI's JSON output line-by-line as the process runs, surfacing:

| Event Type | What to surface |
|------------|-----------------|
| `reasoning` / thinking tokens | Collapsible **Reasoning** block (same as Ollama `<think>`) |
| `item.created` / `function_call` | Tool call activity (file writes, shell commands) |
| `item.completed` / `agent_message` | Final response text |
| `turn.completed` | Token usage |

---

## User Stories

### US-OBS-001 — Stream Codex JSON output in real-time

**As a** user talking to the Codex agent,
**I want** to see live progress as Codex works (tool calls, file writes, shell commands),
**so that** I know it is making progress and what it is doing.

**Acceptance Criteria:**
- `CodexCliModelProvider` reads stdout line-by-line while the process runs instead of waiting for exit
- Each recognised JSON event is mapped to an appropriate `StreamChunk` (`ContentDelta`, `ThinkingDelta`, or a new `ActivityDelta`)
- Unrecognised lines are silently skipped
- Process exit code is still checked; non-zero emits an error

**Tasks:**
- Replace `ExecuteCommandAsync` call in `CompleteAsync` with a dedicated streaming reader in `StreamAsync`
- Wire `StandardOutput` to a `StreamReader` that yields lines via `IAsyncEnumerable`
- Map JSON events to stream chunks as they arrive

---

### US-OBS-002 — Surface Codex reasoning tokens

**As a** user,
**I want** to see the model's reasoning/thinking shown in a collapsible **Reasoning** block,
**so that** I can understand why the agent made certain decisions.

**Acceptance Criteria:**
- Codex JSON events containing reasoning content are emitted as `ThinkingDelta`
- The frontend renders them identically to the Ollama `<think>` block

**Notes:**
- Requires knowing the exact Codex CLI JSON event type for reasoning (likely `item.created` with `type: "reasoning"` or a `reasoning_summary` field — needs empirical testing against the running CLI)
- May be model-dependent (`gpt-5.4` vs `codex-mini-latest`)

**Tasks:**
- Run `codex exec --json` manually in the pod and capture a full session log
- Identify all reasoning-related event types
- Add parsing in the streaming reader

---

### US-OBS-003 — Surface Codex tool / file activity

**As a** user,
**I want** to see what files and commands Codex is running in my workspace,
**so that** I have visibility and trust in what the agent is doing.

**Acceptance Criteria:**
- `function_call` events (shell commands, file writes) appear as tool call activity in the stream
- Displayed in the existing agentic tool call UI (collapsible, with arguments)
- Does not require changes to the agentic iteration model — these are Codex-internal tool calls, not orchestration-level ones

**Tasks:**
- Map `item.created` / `function_call` JSON events to a new `CodexToolCallDelta` or reuse existing `AgenticToolCallStartEvent`
- Emit from the streaming reader and forward through `AgenticOrchestrationEngine.StreamCodexNativeAgenticAsync`
- Wire to the frontend SSE handler

---

### US-OBS-004 — Improve Codex error reporting

**As a** developer or user,
**I want** to see meaningful error output when Codex fails,
**so that** I can understand whether the issue is auth, model availability, quota, or a runtime error.

**Acceptance Criteria:**
- When Codex exits non-zero, `StandardError` is included in the error SSE event (redacted for safety)
- Partial stdout (any JSON lines received before failure) is preserved and returned
- Timeout is surfaced as a specific message rather than a generic error

**Tasks:**
- Capture `stderr` alongside the streaming stdout reader
- On non-zero exit, log full stderr internally and emit a safe redacted message to the client
- Add specific handling for `OperationCanceledException` from command timeout vs request cancellation

---

## Implementation Approach

### Current (buffered)

```
codex exec --json ... | [wait for exit] → parse full stdout → single StreamChunk
```

### Target (streaming)

```
codex exec --json ... | line-by-line reader:
  {"type": "reasoning", ...}    → ThinkingDelta chunk
  {"type": "function_call", ...} → tool activity event
  {"type": "agent_message", ...} → ContentDelta chunk
  {"type": "turn.completed", ...} → usage + IsComplete chunk
  [process exits non-zero]       → error chunk
```

### Key Change

Replace the `ExecuteCommandAsync` path in `CodexCliModelProvider` with a new `StreamCodexOutputAsync` private method that:

1. Starts the process with `RedirectStandardOutput = true`
2. Reads lines asynchronously via `process.StandardOutput.ReadLineAsync(cancellationToken)`
3. Yields parsed `StreamChunk` objects as lines arrive
4. After the last line, awaits `process.WaitForExitAsync` and checks exit code

This requires **not** going through `IWorkspaceManager.ExecuteCommandAsync` (which buffers), and instead using `IWorkspaceManager.EnsureRunningAsync` for workspace setup then running the process directly for local mode, or adapting the Kubernetes/Docker managers to support a streaming exec path.

> **Note**: The Kubernetes path (`kubectl exec`) supports streaming stdout natively. The Docker path (`docker exec`) similarly streams. Both managers already read stdout to a string — adapting them to yield lines is the main work.

---

## Scope Boundary

This is intentionally scoped to **observability only** — no changes to how Codex makes decisions or what tools it can use. The goal is a window into existing behaviour, not new capabilities.

---

## Dependencies

- No new migrations required
- No schema changes
- Builds on the `ThinkingDelta` field already added to `StreamChunk` (implemented 2026-03-23)
- Builds on the agentic SSE stream heartbeat and event infrastructure (implemented 2026-03-23)
