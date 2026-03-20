# Multi-Agent Orchestration - Work Tracker

This document tracks the implementation status of all user stories across the multi-agent orchestration roadmap.

**Last Updated**: 2026-03-20

---

## Status Legend

| Status | Meaning |
|--------|---------|
| `[ ]` | Not Started |
| `[~]` | In Progress |
| `[x]` | Completed |
| `[!]` | Blocked |

---

## Summary

| Priority | Epic | Stories | Completed | Progress |
|----------|------|---------|-----------|----------|
| P1 | Agent Memory | 7 | 0 | Foundation in progress |
| P2 | Task Persistence | 17 | 0 | 0% |
| P3 | Agent Delegation | 17 | 0 | 0% |
| P4 | Shared Workspace | 11 | 0 | 0% |
| **Total** | | **52** | **0** | **0%** |

---

## P1: Agent Memory Layer

**Epic**: EPIC-001 - Agent Memory System
**Document**: [01-priority-agent-memory.md](./01-priority-agent-memory.md)
**Estimated Effort**: 1-2 weeks

### Feature 1: Memory Brain Resolution

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[~]` | US-001 | Resolve Memory Brain | | `IMemoryBrainResolver`, `IUserMemoryPreferenceStore`, and customer-scoped memory-brain persistence are in progress |

### Feature 2: Memory Tools

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[~]` | US-002 | Save Memory Tool | | Initial handler, definitions, hosted agentic guidance, MCP exposure, and immediate-reindex coverage are in progress |
| `[~]` | US-003 | Recall Memories Tool | | Initial OQL-backed recall handler and tests are in progress; hosted agentic chat now appends explicit memory-tool guidance when memory tools are available, and MCP consumers can now use recall_memories directly |
| `[~]` | US-004 | Forget Memory Tool | | Initial delete handler, MCP exposure, and immediate-reindex coverage are in progress |

### Feature 3: Portal UI

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[~]` | US-005 | Memories Page | | Shared Markdown authoring surface is in place for memories, and Documents/Memories now share the same App-level workspace controller hook with portal integration coverage |
| `[~]` | US-006 | Hide Memories from Documents | | Backend `excludePathPrefix` support and portal wiring are in progress |
| `[~]` | US-007 | Memory Brain Configuration | | Hosted API and Account selector are in progress |

### P1 Migrations

| Status | Migration | Description |
|--------|-----------|-------------|
| `[~]` | `0009_user_memory_brain.sql` + `0009a_customer_membership_memory_brain.sql` | Adds and then scopes memory_brain_id to customer memberships |

### P1 Implementation Notes

- 2026-03-19: Started the P1 foundation slice on branch `multi-agent-orchestration`.
- 2026-03-19: Added `0009_user_memory_brain.sql` and a focused `IUserMemoryPreferenceStore` instead of introducing a generic user-settings subsystem.
- 2026-03-19: Added `IMemoryBrainResolver` to resolve an active managed-content brain or return a configuration-required result when multiple candidate brains exist.
- 2026-03-19: Added managed-document `pathPrefix` and `excludePathPrefix` filtering so `memories/*` can be hidden from the normal Documents experience.
- 2026-03-19: Added OQL `path_prefix` execution support for day-1 memory recall scoping.
- 2026-03-19: Started `OpenCortex.Tools.Memory` with `save_memory`, `recall_memories`, and `forget_memory` handlers wired for managed-document storage plus OQL recall.
- 2026-03-19: Added hosted memory-brain read/update endpoints and an Account UI selector for memory_brain_id.
- 2026-03-19: Reworked the hosted Memories page to use the same shared Markdown document workflow as Documents, so memories are editable managed-content docs under `memories/*` rather than a separate read-only inspector.
- 2026-03-19: Added App-level frontend coverage for the Memories fetch/load/delete flow so the portal shell wiring is tested above the view component.
- 2026-03-19: Extracted a shared managed-document workspace hook in the portal so Documents and Memories now reuse the same controller logic for list/detail/draft/version workflows, not just the same view surface.
- 2026-03-19: Updated hosted agentic chat request building so memory-tool guidance is injected into the system prompt when memory tools are available, and added endpoint tests to verify the real `/api/chat/completions/agentic` path.
- 2026-03-20: Exposed `save_memory`, `recall_memories`, and `forget_memory` through the local MCP server so MCP consumers can use the same memory workflow directly, and updated memory save/forget handlers to reindex immediately so recall can see new changes without waiting for a later background cycle.
- 2026-03-20: Corrected memory-brain preference scope from user-global storage to customer-membership storage with `0009a_customer_membership_memory_brain.sql`, plus resolver and endpoint coverage to prevent cross-workspace preference bleed.
- 2026-03-20: Improved `save_memory` quota-hit guidance so agentic chat and MCP consumers receive an explicit next step to review memories, forget one, or upgrade before retrying.

---

## P2: Task/Goal Persistence

**Epic**: EPIC-002 - Work Item Management System
**Document**: [02-priority-task-persistence.md](./02-priority-task-persistence.md)
**Estimated Effort**: 6-7 weeks

### Feature 1: Work Item Data Model & Storage

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[ ]` | US-008 | Work Item Entity Schema | | P2-US-001 in doc |
| `[ ]` | US-009 | Work Item Status & Transitions | | P2-US-002 in doc |
| `[ ]` | US-010 | Work Item Hierarchy Navigation | | P2-US-003 in doc |

### Feature 2: Work Item MCP/Agent Tools

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[ ]` | US-011 | Create Work Item Tool | | P2-US-004 in doc |
| `[ ]` | US-012 | Plan Epic Tool (AI-Assisted) | | P2-US-005 in doc |
| `[ ]` | US-013 | List & Query Work Items | | P2-US-006 in doc |
| `[ ]` | US-014 | Update Work Item Status | | P2-US-007 in doc |

### Feature 3: Work Item Context Integration

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[ ]` | US-015 | Work Item Context Injection | | P2-US-008 in doc |
| `[ ]` | US-016 | Conversation-Work Item Linking | | P2-US-009 in doc |

### Feature 4: Work Item API Endpoints

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[ ]` | US-017 | Work Item CRUD API | | P2-US-010 in doc |
| `[ ]` | US-018 | Work Item History API | | P2-US-011 in doc |

### Feature 5: Portal UI Integration

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[ ]` | US-019 | Kanban Board View (ADO-Style) | | P2-US-012 in doc |
| `[ ]` | US-020 | Backlog View | | P2-US-013 in doc |
| `[ ]` | US-021 | Sprint Planning View | | P2-US-014 in doc |
| `[ ]` | US-022 | Work Item Hierarchy View | | P2-US-015 in doc |
| `[ ]` | US-023 | Work Item Detail Panel | | P2-US-016 in doc |
| `[ ]` | US-024 | AI Planning Assistant | | P2-US-017 in doc |

### P2 Migrations

| Status | Migration | Description |
|--------|-----------|-------------|
| `[ ]` | `0010_work_items.sql` | Work items table + sequences |
| `[ ]` | `0011_sprints.sql` | Sprints table |

---

## P3: Agent Delegation

**Epic**: EPIC-003 - Multi-Agent Delegation System
**Document**: [03-priority-agent-delegation.md](./03-priority-agent-delegation.md)
**Estimated Effort**: 8-9 weeks

### Feature 1: Agent Registry & Profiles

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[ ]` | US-025 | Agent Profile Storage | | P3-US-016 in doc |
| `[ ]` | US-026 | Seed Default Agents | | P3-US-017 in doc |
| `[ ]` | US-027 | Agent Profile Management API | | P3-US-018 in doc |
| `[ ]` | US-028 | Custom Agent Creation | | P3-US-019 in doc |

### Feature 2: Delegation Tool

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[ ]` | US-029 | Delegate To Agent Tool | | P3-US-020 in doc |

### Feature 3: Sub-Agent Orchestration

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[ ]` | US-030 | Sub-Agent Execution Engine | | P3-US-021 in doc |
| `[ ]` | US-031 | Delegation Telemetry | | P3-US-022 in doc |

### Feature 4: Delegation Safety & Limits

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[ ]` | US-032 | Delegation Depth Limiting | | P3-US-023 in doc |
| `[ ]` | US-033 | Delegation Quotas | | P3-US-024 in doc |
| `[ ]` | US-034 | Delegation Timeout | | P3-US-025 in doc |

### Feature 5: Agent Configuration Portal UI

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[ ]` | US-035 | Agent List View | | P3-US-026 in doc |
| `[ ]` | US-036 | Agent Identity Configuration | | P3-US-027 in doc |
| `[ ]` | US-037 | Agent Soul (System Prompt) Config | | P3-US-028 in doc |
| `[ ]` | US-038 | Agent Tool Configuration | | P3-US-029 in doc |
| `[ ]` | US-039 | Agent Provider Access Config | | P3-US-030 in doc |
| `[ ]` | US-040 | Agent Limits Configuration | | P3-US-031 in doc |
| `[ ]` | US-041 | Agent Editor Page | | P3-US-032 in doc |

### P3 Migrations

| Status | Migration | Description |
|--------|-----------|-------------|
| `[ ]` | `0012_agent_profiles.sql` | Agent profiles + default seeding |

---

## P4: Shared Workspace Coordination

**Epic**: EPIC-004 - Multi-Agent Workspace Collaboration
**Document**: [04-priority-shared-workspace.md](./04-priority-shared-workspace.md)
**Estimated Effort**: 4-5 weeks

### Feature 1: Document Locking

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[ ]` | US-042 | Acquire Document Lock | | P4-US-033 in doc |
| `[ ]` | US-043 | Release Document Lock | | P4-US-034 in doc |
| `[ ]` | US-044 | Check Document Lock Status | | P4-US-035 in doc |
| `[ ]` | US-045 | Extend Lock Duration | | P4-US-036 in doc |

### Feature 2: Change Notifications

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[ ]` | US-046 | Document Change Events | | P4-US-037 in doc |
| `[ ]` | US-047 | Query Recent Changes | | P4-US-038 in doc |

### Feature 3: Task Workspace

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[ ]` | US-048 | Task Workspace Brain | | P4-US-039 in doc |
| `[ ]` | US-049 | Workspace Document Tools | | P4-US-040 in doc |

### Feature 4: Conflict Detection & Resolution

| Status | ID | User Story | Assignee | Notes |
|--------|-----|------------|----------|-------|
| `[ ]` | US-050 | Conflict Detection | | P4-US-041 in doc |
| `[ ]` | US-051 | Conflict Resolution Strategies | | P4-US-042 in doc |
| `[ ]` | US-052 | Conflict Notifications | | P4-US-043 in doc |

### P4 Migrations

| Status | Migration | Description |
|--------|-----------|-------------|
| `[ ]` | `0013_document_locks.sql` | Advisory locking table |
| `[ ]` | `0014_document_changes.sql` | Change tracking table |

---

## Implementation Order

```
Week 1-2:   P1 (Agent Memory) Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€“Âº
Week 3-4:                        P2 Foundation Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€“Âº
Week 5-6:                                         P2 Tools Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€“Âº
Week 7-9:                                                   P2 UI
Week 10-11:                      P3 Registry & Profiles Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€“Âº
Week 12-13:                                       P3 Delegation Ã¢â€“Âº
Week 14-16:                                              P3 UI Ã¢â€â‚¬Ã¢â€“Âº
Week 17-18:                      P4 Locking Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€“Âº
Week 19-20:                                   P4 Changes/Conflict
```

---

## Notes

- User story IDs in this tracker (US-001 to US-052) are globally unique
- Each story references its local ID in the priority document (e.g., "P2-US-001 in doc")
- Update this tracker as work progresses
- Dependencies: P1 Ã¢â€ â€™ P2 Ã¢â€ â€™ P3 Ã¢â€ â€™ P4

---

## Change Log

| Date | Change |
|------|--------|
| 2026-03-19 | Initial tracker created |







