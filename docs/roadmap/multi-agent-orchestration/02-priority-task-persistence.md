# Priority 2: Task/Goal Persistence

## Epic Overview

**Epic**: Work Item Management System

Enable agents and users to plan, track, and execute work using a full hierarchy: Epic â†’ Feature â†’ User Story â†’ Task. Support both agentic task tracking and human-driven project planning.

---

## Implementation Overview

This priority adds a new work items system using existing database and API infrastructure patterns.

Migration numbering note: `0010_tenant_scoped_user_provider_configs.sql` and `0011_user_workspace_runtime_profiles.sql` now exist in the real migration chain. Planned task-persistence migrations in this document should therefore be treated as `0012_work_items.sql` and `0013_sprints.sql`.

### Files to Create

```
src/OpenCortex.Domain/WorkItems/
â”œâ”€â”€ WorkItem.cs                           # Domain entity
â”œâ”€â”€ WorkItemType.cs                       # Enum: Epic, Feature, UserStory, Task
â”œâ”€â”€ WorkItemStatus.cs                     # Enum: Backlog, Planned, InProgress, etc.
â”œâ”€â”€ WorkItemPriority.cs                   # Enum: Critical, High, Medium, Low
â”œâ”€â”€ WorkItemStatusTransitions.cs          # Valid status transitions
â”œâ”€â”€ WorkItemHierarchy.cs                  # Parent-child validation
â”œâ”€â”€ Sprint.cs                             # Sprint entity
â”œâ”€â”€ IWorkItemRepository.cs                # Repository interface
â””â”€â”€ ISprintRepository.cs                  # Sprint repository interface

src/OpenCortex.Persistence.Postgres/
â”œâ”€â”€ Migrations/
â”‚   â”œâ”€â”€ 0012_work_items.sql               # Work items table + sequences
â”‚   â””â”€â”€ 0013_sprints.sql                  # Sprints table
â”œâ”€â”€ Repositories/
â”‚   â”œâ”€â”€ PostgresWorkItemRepository.cs     # IWorkItemRepository implementation
â”‚   â””â”€â”€ PostgresSprintRepository.cs       # ISprintRepository implementation
â””â”€â”€ ServiceCollectionExtensions.cs        # ADD: Repository registration

src/OpenCortex.Tools.WorkItems/
â”œâ”€â”€ OpenCortex.Tools.WorkItems.csproj     # New project
â”œâ”€â”€ ServiceCollectionExtensions.cs        # DI registration
â”œâ”€â”€ WorkItemToolDefinitions.cs            # Tool JSON schemas
â”œâ”€â”€ PlanningPrompts.cs                    # LLM prompts for AI planning
â””â”€â”€ Handlers/
    â”œâ”€â”€ CreateWorkItemHandler.cs
    â”œâ”€â”€ ListWorkItemsHandler.cs
    â”œâ”€â”€ GetWorkItemHandler.cs
    â”œâ”€â”€ UpdateWorkItemHandler.cs
    â””â”€â”€ PlanEpicHandler.cs

src/OpenCortex.Orchestration/WorkItems/
â”œâ”€â”€ IWorkItemContextBuilder.cs            # Interface
â””â”€â”€ WorkItemContextBuilder.cs             # Injects work item context into agent prompts

src/OpenCortex.Api/
â”œâ”€â”€ WorkItemEndpoints.cs                  # NEW: REST API for work items
â”œâ”€â”€ SprintEndpoints.cs                    # NEW: REST API for sprints
â””â”€â”€ Program.cs                            # ADD: .MapWorkItemEndpoints(), .MapSprintEndpoints()

src/OpenCortex.Portal/Frontend/src/
â”œâ”€â”€ components/WorkItems/
â”‚   â”œâ”€â”€ WorkItemBoard.tsx                 # Kanban board view
â”‚   â”œâ”€â”€ WorkItemCard.tsx                  # Card component for board
â”‚   â”œâ”€â”€ BacklogView.tsx                   # Prioritized backlog list
â”‚   â”œâ”€â”€ SprintPlanningView.tsx            # Sprint planning
â”‚   â”œâ”€â”€ WorkItemTree.tsx                  # Hierarchy tree view
â”‚   â”œâ”€â”€ WorkItemDetailPanel.tsx           # Detail slide-out panel
â”‚   â”œâ”€â”€ AIPlanningModal.tsx               # AI breakdown assistant
â”‚   â”œâ”€â”€ TypeBadge.tsx                     # Type indicator (Epic/Feature/Story/Task)
â”‚   â”œâ”€â”€ StatusBadge.tsx                   # Status indicator
â”‚   â””â”€â”€ PriorityIndicator.tsx             # Priority indicator
â”œâ”€â”€ hooks/
â”‚   â”œâ”€â”€ useWorkItems.ts                   # Query hook for work items
â”‚   â”œâ”€â”€ useWorkItem.ts                    # Single item query
â”‚   â”œâ”€â”€ useCreateWorkItem.ts              # Create mutation
â”‚   â”œâ”€â”€ useUpdateWorkItem.ts              # Update mutation
â”‚   â”œâ”€â”€ useSprints.ts                     # Sprint queries
â”‚   â””â”€â”€ usePlanEpic.ts                    # AI planning mutation
â””â”€â”€ App.tsx                               # ADD: Work Items to viewDefinitions
```

### Existing Services to Use

| Service | Location | Usage |
|---------|----------|-------|
| `NpgsqlConnection` | DI from `AddPostgresStores()` | Database access |
| `IUserOrchestrationService` | `OpenCortex.Orchestration` | LLM calls for AI planning |
| `IToolHandler` | `OpenCortex.Tools` | Tool handler interface |
| `IToolDefinitionProvider` | `OpenCortex.Tools` | Tool definition provider |

### DI Registration Pattern

```csharp
// src/OpenCortex.Persistence.Postgres/ServiceCollectionExtensions.cs
// ADD to existing AddPostgresStores() method:
services.AddScoped<IWorkItemRepository, PostgresWorkItemRepository>();
services.AddScoped<ISprintRepository, PostgresSprintRepository>();
```

```csharp
// src/OpenCortex.Tools.WorkItems/ServiceCollectionExtensions.cs (NEW FILE)
namespace OpenCortex.Tools.WorkItems;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkItemTools(this IServiceCollection services)
    {
        // Tool handlers
        services.AddScoped<IToolHandler, CreateWorkItemHandler>();
        services.AddScoped<IToolHandler, ListWorkItemsHandler>();
        services.AddScoped<IToolHandler, GetWorkItemHandler>();
        services.AddScoped<IToolHandler, UpdateWorkItemHandler>();
        services.AddScoped<IToolHandler, PlanEpicHandler>();

        // Tool definitions provider
        services.AddSingleton<IToolDefinitionProvider, WorkItemToolDefinitionProvider>();

        // Context builder
        services.AddScoped<IWorkItemContextBuilder, WorkItemContextBuilder>();

        return services;
    }
}
```

```csharp
// src/OpenCortex.Api/Program.cs
// ADD after AddMemoryTools():
builder.Services.AddWorkItemTools();

// ADD after MapMemoryTools() in endpoint mapping:
app.MapWorkItemEndpoints();
app.MapSprintEndpoints();
```

---

## Epic Details

| Field | Value |
|-------|-------|
| Epic ID | `EPIC-002` |
| Priority | P2 |
| Dependencies | P1 (Agent Memory Layer) |
| Estimated Effort | 6-7 weeks |
| Business Value | High - Enables complex multi-session workflows and project planning |

### Problem Statement

Agents and users cannot currently:
- Track what they're working on across sessions
- Decompose complex goals into structured hierarchies
- Plan projects with Epics, Features, and User Stories
- Report progress on multi-step operations
- Resume interrupted work
- Collaborate on planning with AI assistance

### Success Criteria

- Full work item hierarchy persisted (Epic â†’ Feature â†’ User Story â†’ Task)
- Agents can create and manage work items via tools
- Users can view and manage work items in Portal UI
- AI can help break down epics into features/stories/tasks
- Work items integrate with conversation context

---

# Features

## Feature 1: Work Item Data Model & Storage

**Feature ID**: `FEAT-002-01`

Create the foundational data model and persistence layer for the complete work item hierarchy.

### User Stories

#### US-001: Work Item Entity Schema
**As a** system architect
**I want** a unified work item schema that supports all levels
**So that** we have a consistent data model for the hierarchy

**Acceptance Criteria**:
- Single `work_items` table supports all types (epic, feature, user_story, task)
- Parent-child relationships via `parent_id`
- Type-specific fields handled via `metadata` JSON
- Supports both user-created and agent-created items

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-001-01 | Create `work_items` table migration | 3h | Database |
| T-001-02 | Create `WorkItem` domain entity | 2h | Domain |
| T-001-03 | Create `IWorkItemRepository` interface | 1h | Abstractions |
| T-001-04 | Implement `PostgresWorkItemRepository` | 4h | Persistence |
| T-001-05 | Add repository to DI registration | 30m | Infrastructure |
| T-001-06 | Write repository unit tests | 3h | Testing |

**Slice Details**:

##### T-001-01: Create `work_items` table migration
```sql
-- Migration: 0012_work_items.sql
CREATE TABLE IF NOT EXISTS opencortex.work_items (
    work_item_id text PRIMARY KEY,

    -- Ownership
    customer_id text NOT NULL REFERENCES opencortex.customers(customer_id),
    user_id uuid NOT NULL REFERENCES opencortex.users(user_id),

    -- Hierarchy
    parent_id text REFERENCES opencortex.work_items(work_item_id),
    item_type text NOT NULL,  -- 'epic', 'feature', 'user_story', 'task'
    item_number integer,       -- Auto-incrementing per customer: EPIC-001, FEAT-001, etc.

    -- Core fields
    title text NOT NULL,
    description text,

    -- For user stories: "As a X, I want Y, so that Z"
    persona text,              -- "As a {persona}"
    goal text,                 -- "I want {goal}"
    benefit text,              -- "So that {benefit}"

    -- Status & priority
    status text NOT NULL DEFAULT 'backlog',
    priority text NOT NULL DEFAULT 'medium',

    -- Planning
    acceptance_criteria text[], -- List of criteria
    estimated_hours numeric(6,2),
    actual_hours numeric(6,2),

    -- Assignments
    assigned_to uuid REFERENCES opencortex.users(user_id),
    assigned_agent text,       -- agent_profile_id if delegated

    -- Results
    result text,
    result_summary text,

    -- Relationships
    conversation_id text REFERENCES opencortex.conversations(conversation_id),
    brain_id text,             -- Related brain for context

    -- Metadata
    tags text[] NOT NULL DEFAULT '{}',
    labels jsonb NOT NULL DEFAULT '{}',
    metadata jsonb NOT NULL DEFAULT '{}',

    -- Timestamps
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz,
    completed_at timestamptz,
    due_date timestamptz,

    -- Constraints
    CONSTRAINT valid_item_type CHECK (item_type IN ('epic', 'feature', 'user_story', 'task')),
    CONSTRAINT valid_status CHECK (status IN (
        'backlog', 'planned', 'in_progress', 'blocked',
        'in_review', 'done', 'cancelled', 'wont_do'
    )),
    CONSTRAINT valid_priority CHECK (priority IN ('critical', 'high', 'medium', 'low'))
);

-- Indexes
CREATE INDEX ix_work_items_customer_type ON opencortex.work_items(customer_id, item_type);
CREATE INDEX ix_work_items_parent ON opencortex.work_items(parent_id) WHERE parent_id IS NOT NULL;
CREATE INDEX ix_work_items_user_status ON opencortex.work_items(user_id, status);
CREATE INDEX ix_work_items_assigned ON opencortex.work_items(assigned_to) WHERE assigned_to IS NOT NULL;
CREATE INDEX ix_work_items_status ON opencortex.work_items(status) WHERE status NOT IN ('done', 'cancelled', 'wont_do');
CREATE INDEX ix_work_items_tags ON opencortex.work_items USING gin(tags);

-- Sequence for item numbers per type per customer
CREATE TABLE IF NOT EXISTS opencortex.work_item_sequences (
    customer_id text NOT NULL REFERENCES opencortex.customers(customer_id),
    item_type text NOT NULL,
    next_number integer NOT NULL DEFAULT 1,
    PRIMARY KEY (customer_id, item_type)
);

-- Function to get next item number
CREATE OR REPLACE FUNCTION opencortex.next_work_item_number(
    p_customer_id text,
    p_item_type text
) RETURNS integer AS $$
DECLARE
    v_number integer;
BEGIN
    INSERT INTO opencortex.work_item_sequences (customer_id, item_type, next_number)
    VALUES (p_customer_id, p_item_type, 2)
    ON CONFLICT (customer_id, item_type)
    DO UPDATE SET next_number = opencortex.work_item_sequences.next_number + 1
    RETURNING next_number - 1 INTO v_number;

    RETURN v_number;
END;
$$ LANGUAGE plpgsql;
```

##### T-001-02: Create `WorkItem` domain entity
```csharp
// src/OpenCortex.Domain/WorkItems/WorkItem.cs
public sealed class WorkItem
{
    public string WorkItemId { get; init; } = Ulid.NewUlid().ToString();

    // Ownership
    public string CustomerId { get; init; }
    public Guid UserId { get; init; }

    // Hierarchy
    public string? ParentId { get; init; }
    public WorkItemType ItemType { get; init; }
    public int? ItemNumber { get; set; }  // Set by DB

    // Core fields
    public string Title { get; set; }
    public string? Description { get; set; }

    // User story format
    public string? Persona { get; set; }      // "As a {persona}"
    public string? Goal { get; set; }          // "I want {goal}"
    public string? Benefit { get; set; }       // "So that {benefit}"

    // Status & priority
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Backlog;
    public WorkItemPriority Priority { get; set; } = WorkItemPriority.Medium;

    // Planning
    public IList<string> AcceptanceCriteria { get; set; } = new List<string>();
    public decimal? EstimatedHours { get; set; }
    public decimal? ActualHours { get; set; }

    // Assignments
    public Guid? AssignedTo { get; set; }
    public string? AssignedAgent { get; set; }

    // Results
    public string? Result { get; set; }
    public string? ResultSummary { get; set; }

    // Relationships
    public string? ConversationId { get; set; }
    public string? BrainId { get; set; }

    // Metadata
    public IList<string> Tags { get; set; } = new List<string>();
    public Dictionary<string, string> Labels { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();

    // Timestamps
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? DueDate { get; set; }

    // Navigation (not persisted, loaded separately)
    public IReadOnlyList<WorkItem> Children { get; set; } = [];
    public WorkItem? Parent { get; set; }

    // Computed
    public string FormattedId => ItemType switch
    {
        WorkItemType.Epic => $"EPIC-{ItemNumber:D3}",
        WorkItemType.Feature => $"FEAT-{ItemNumber:D3}",
        WorkItemType.UserStory => $"US-{ItemNumber:D3}",
        WorkItemType.Task => $"T-{ItemNumber:D3}",
        _ => WorkItemId[..8]
    };

    public string? UserStoryFormat => ItemType == WorkItemType.UserStory &&
        !string.IsNullOrEmpty(Persona) && !string.IsNullOrEmpty(Goal)
        ? $"As a {Persona}, I want {Goal}" +
          (!string.IsNullOrEmpty(Benefit) ? $", so that {Benefit}" : "")
        : null;
}

public enum WorkItemType { Epic, Feature, UserStory, Task }

public enum WorkItemStatus
{
    Backlog,      // Not yet planned
    Planned,      // In a sprint/iteration
    InProgress,   // Being worked on
    Blocked,      // Waiting on something
    InReview,     // Needs review/approval
    Done,         // Completed
    Cancelled,    // Won't be done
    WontDo        // Decided not to do
}

public enum WorkItemPriority { Critical, High, Medium, Low }
```

##### T-001-03: Repository interface
```csharp
// src/OpenCortex.Domain/WorkItems/IWorkItemRepository.cs
public interface IWorkItemRepository
{
    // CRUD
    Task<WorkItem?> GetByIdAsync(string workItemId, CancellationToken ct);
    Task<WorkItem?> GetByNumberAsync(string customerId, WorkItemType type, int number, CancellationToken ct);
    Task CreateAsync(WorkItem item, CancellationToken ct);
    Task UpdateAsync(WorkItem item, CancellationToken ct);
    Task DeleteAsync(string workItemId, CancellationToken ct);

    // Queries
    Task<IReadOnlyList<WorkItem>> GetByCustomerAsync(
        string customerId,
        WorkItemType? type = null,
        WorkItemStatus? status = null,
        int limit = 50,
        CancellationToken ct = default);

    Task<IReadOnlyList<WorkItem>> GetChildrenAsync(string parentId, CancellationToken ct);

    Task<WorkItem?> GetWithHierarchyAsync(string workItemId, int depth = 3, CancellationToken ct = default);

    Task<IReadOnlyList<WorkItem>> GetByTagAsync(string customerId, string tag, CancellationToken ct);

    Task<IReadOnlyList<WorkItem>> GetAssignedToUserAsync(Guid userId, CancellationToken ct);

    Task<IReadOnlyList<WorkItem>> GetAssignedToAgentAsync(string agentId, CancellationToken ct);

    Task<IReadOnlyList<WorkItem>> GetByConversationAsync(string conversationId, CancellationToken ct);

    Task<IReadOnlyList<WorkItem>> GetBySprintAsync(string sprintId, CancellationToken ct);

    // Stats
    Task<WorkItemStats> GetStatsAsync(string customerId, CancellationToken ct);
    Task<WorkItemStats> GetStatsForParentAsync(string parentId, CancellationToken ct);
}

public sealed class WorkItemStats
{
    public int Total { get; init; }
    public int Backlog { get; init; }
    public int InProgress { get; init; }
    public int Done { get; init; }
    public int Blocked { get; init; }
    public decimal? TotalEstimatedHours { get; init; }
    public decimal? TotalActualHours { get; init; }
}
```

##### T-001-04: PostgresWorkItemRepository implementation
```csharp
// src/OpenCortex.Persistence.Postgres/Repositories/PostgresWorkItemRepository.cs
using Npgsql;
using OpenCortex.Domain.WorkItems;

namespace OpenCortex.Persistence.Postgres.Repositories;

public sealed class PostgresWorkItemRepository : IWorkItemRepository
{
    private readonly NpgsqlConnection _connection;
    private readonly ILogger<PostgresWorkItemRepository> _logger;

    public PostgresWorkItemRepository(
        NpgsqlConnection connection,
        ILogger<PostgresWorkItemRepository> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task CreateAsync(WorkItem item, CancellationToken ct)
    {
        // Get next item number atomically
        var number = await GetNextItemNumberAsync(item.CustomerId, item.ItemType, ct);
        item.ItemNumber = number;

        const string sql = """
            INSERT INTO opencortex.work_items (
                work_item_id, customer_id, user_id, parent_id, item_type, item_number,
                title, description, persona, goal, benefit,
                status, priority, acceptance_criteria, estimated_hours,
                assigned_to, assigned_agent, conversation_id, brain_id, sprint_id,
                tags, labels, metadata, created_at, updated_at, due_date
            ) VALUES (
                @WorkItemId, @CustomerId, @UserId, @ParentId, @ItemType, @ItemNumber,
                @Title, @Description, @Persona, @Goal, @Benefit,
                @Status, @Priority, @AcceptanceCriteria, @EstimatedHours,
                @AssignedTo, @AssignedAgent, @ConversationId, @BrainId, @SprintId,
                @Tags, @Labels::jsonb, @Metadata::jsonb, @CreatedAt, @UpdatedAt, @DueDate
            )
            """;

        await using var cmd = new NpgsqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("WorkItemId", item.WorkItemId);
        cmd.Parameters.AddWithValue("CustomerId", item.CustomerId);
        cmd.Parameters.AddWithValue("UserId", item.UserId);
        cmd.Parameters.AddWithValue("ParentId", (object?)item.ParentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ItemType", item.ItemType.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("ItemNumber", item.ItemNumber!);
        cmd.Parameters.AddWithValue("Title", item.Title);
        cmd.Parameters.AddWithValue("Description", (object?)item.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("Persona", (object?)item.Persona ?? DBNull.Value);
        cmd.Parameters.AddWithValue("Goal", (object?)item.Goal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("Benefit", (object?)item.Benefit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("Status", item.Status.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("Priority", item.Priority.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("AcceptanceCriteria", item.AcceptanceCriteria.ToArray());
        cmd.Parameters.AddWithValue("EstimatedHours", (object?)item.EstimatedHours ?? DBNull.Value);
        cmd.Parameters.AddWithValue("AssignedTo", (object?)item.AssignedTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("AssignedAgent", (object?)item.AssignedAgent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ConversationId", (object?)item.ConversationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("BrainId", (object?)item.BrainId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("SprintId", (object?)item.SprintId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("Tags", item.Tags.ToArray());
        cmd.Parameters.AddWithValue("Labels", JsonSerializer.Serialize(item.Labels));
        cmd.Parameters.AddWithValue("Metadata", JsonSerializer.Serialize(item.Metadata));
        cmd.Parameters.AddWithValue("CreatedAt", item.CreatedAt);
        cmd.Parameters.AddWithValue("UpdatedAt", item.UpdatedAt);
        cmd.Parameters.AddWithValue("DueDate", (object?)item.DueDate ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation(
            "Created work item {FormattedId}: {Title}",
            item.FormattedId, item.Title);
    }

    private async Task<int> GetNextItemNumberAsync(
        string customerId, WorkItemType itemType, CancellationToken ct)
    {
        const string sql = """
            SELECT opencortex.next_work_item_number(@CustomerId, @ItemType)
            """;

        await using var cmd = new NpgsqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("CustomerId", customerId);
        cmd.Parameters.AddWithValue("ItemType", itemType.ToString().ToLowerInvariant());

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<WorkItem?> GetByIdAsync(string workItemId, CancellationToken ct)
    {
        const string sql = """
            SELECT * FROM opencortex.work_items WHERE work_item_id = @WorkItemId
            """;

        await using var cmd = new NpgsqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("WorkItemId", workItemId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return MapWorkItem(reader);
    }

    public async Task<IReadOnlyList<WorkItem>> GetChildrenAsync(string parentId, CancellationToken ct)
    {
        const string sql = """
            SELECT * FROM opencortex.work_items
            WHERE parent_id = @ParentId
            ORDER BY priority DESC, created_at
            """;

        await using var cmd = new NpgsqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("ParentId", parentId);

        var items = new List<WorkItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(MapWorkItem(reader));
        }
        return items;
    }

    private static WorkItem MapWorkItem(NpgsqlDataReader reader)
    {
        return new WorkItem
        {
            WorkItemId = reader.GetString(reader.GetOrdinal("work_item_id")),
            CustomerId = reader.GetString(reader.GetOrdinal("customer_id")),
            UserId = reader.GetGuid(reader.GetOrdinal("user_id")),
            ParentId = reader.IsDBNull(reader.GetOrdinal("parent_id"))
                ? null : reader.GetString(reader.GetOrdinal("parent_id")),
            ItemType = Enum.Parse<WorkItemType>(
                reader.GetString(reader.GetOrdinal("item_type")), ignoreCase: true),
            ItemNumber = reader.IsDBNull(reader.GetOrdinal("item_number"))
                ? null : reader.GetInt32(reader.GetOrdinal("item_number")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            Description = reader.IsDBNull(reader.GetOrdinal("description"))
                ? null : reader.GetString(reader.GetOrdinal("description")),
            Status = Enum.Parse<WorkItemStatus>(
                reader.GetString(reader.GetOrdinal("status")), ignoreCase: true),
            Priority = Enum.Parse<WorkItemPriority>(
                reader.GetString(reader.GetOrdinal("priority")), ignoreCase: true),
            // ... continue mapping all fields
        };
    }
}
```

---

#### US-002: Work Item Status & Transitions
**As a** user or agent
**I want** work item status to follow defined transitions
**So that** workflow is consistent and trackable

**Acceptance Criteria**:
- Status transitions validated
- Status change history recorded
- Timestamps updated automatically
- Parent status reflects children (optional rollup)

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-002-01 | Create `work_item_history` table | 1h | Database |
| T-002-02 | Implement status transition validation | 2h | Domain |
| T-002-03 | Add history recording service | 2h | Services |
| T-002-04 | Implement parent status rollup (optional) | 2h | Services |
| T-002-05 | Write status transition tests | 2h | Testing |

**Slice Details**:

##### T-002-01: History table
```sql
CREATE TABLE IF NOT EXISTS opencortex.work_item_history (
    history_id text PRIMARY KEY,
    work_item_id text NOT NULL REFERENCES opencortex.work_items(work_item_id) ON DELETE CASCADE,

    field_name text NOT NULL,        -- 'status', 'assigned_to', 'priority', etc.
    old_value text,
    new_value text,

    changed_by_type text NOT NULL,   -- 'user' or 'agent'
    changed_by_id text NOT NULL,
    changed_by_name text,

    comment text,

    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_work_item_history_item ON opencortex.work_item_history(work_item_id, created_at DESC);
```

##### T-002-02: Status transitions
```csharp
// src/OpenCortex.Domain/WorkItems/WorkItemStatusTransitions.cs
public static class WorkItemStatusTransitions
{
    private static readonly Dictionary<WorkItemStatus, WorkItemStatus[]> ValidTransitions = new()
    {
        [WorkItemStatus.Backlog] = [WorkItemStatus.Planned, WorkItemStatus.Cancelled, WorkItemStatus.WontDo],
        [WorkItemStatus.Planned] = [WorkItemStatus.InProgress, WorkItemStatus.Backlog, WorkItemStatus.Cancelled],
        [WorkItemStatus.InProgress] = [WorkItemStatus.InReview, WorkItemStatus.Blocked, WorkItemStatus.Done, WorkItemStatus.Cancelled],
        [WorkItemStatus.Blocked] = [WorkItemStatus.InProgress, WorkItemStatus.Cancelled],
        [WorkItemStatus.InReview] = [WorkItemStatus.Done, WorkItemStatus.InProgress],
        [WorkItemStatus.Done] = [WorkItemStatus.InProgress],  // Reopen
        [WorkItemStatus.Cancelled] = [WorkItemStatus.Backlog],  // Restore
        [WorkItemStatus.WontDo] = [WorkItemStatus.Backlog]
    };

    public static bool CanTransition(WorkItemStatus from, WorkItemStatus to)
        => ValidTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static void ValidateTransition(WorkItemStatus from, WorkItemStatus to)
    {
        if (!CanTransition(from, to))
            throw new InvalidOperationException(
                $"Cannot transition from {from} to {to}. " +
                $"Valid transitions: {string.Join(", ", ValidTransitions[from])}");
    }
}
```

---

#### US-003: Work Item Hierarchy Navigation
**As a** user
**I want** to navigate the work item hierarchy
**So that** I can see epics â†’ features â†’ stories â†’ tasks

**Acceptance Criteria**:
- Can load item with full hierarchy (up to N levels)
- Can load just children of an item
- Can traverse up to parent/ancestors
- Hierarchy depth enforced (max 4 levels)

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-003-01 | Implement `GetWithHierarchyAsync` recursive query | 3h | Persistence |
| T-003-02 | Add parent chain loading | 1h | Persistence |
| T-003-03 | Enforce hierarchy rules (epicâ†’featureâ†’storyâ†’task) | 1h | Domain |
| T-003-04 | Write hierarchy navigation tests | 2h | Testing |

**Slice Details**:

##### T-003-03: Hierarchy validation
```csharp
// src/OpenCortex.Domain/WorkItems/WorkItemHierarchy.cs
public static class WorkItemHierarchy
{
    private static readonly Dictionary<WorkItemType, WorkItemType[]> ValidChildren = new()
    {
        [WorkItemType.Epic] = [WorkItemType.Feature],
        [WorkItemType.Feature] = [WorkItemType.UserStory],
        [WorkItemType.UserStory] = [WorkItemType.Task],
        [WorkItemType.Task] = []  // Tasks have no children
    };

    public static bool CanBeChildOf(WorkItemType childType, WorkItemType parentType)
        => ValidChildren.TryGetValue(parentType, out var allowed) && allowed.Contains(childType);

    public static void ValidateParentChild(WorkItem parent, WorkItem child)
    {
        if (!CanBeChildOf(child.ItemType, parent.ItemType))
            throw new InvalidOperationException(
                $"A {child.ItemType} cannot be a child of a {parent.ItemType}. " +
                $"Valid children: {string.Join(", ", ValidChildren[parent.ItemType])}");
    }

    public static WorkItemType? GetParentType(WorkItemType type) => type switch
    {
        WorkItemType.Feature => WorkItemType.Epic,
        WorkItemType.UserStory => WorkItemType.Feature,
        WorkItemType.Task => WorkItemType.UserStory,
        _ => null
    };
}
```

---

## Feature 2: Work Item MCP/Agent Tools

**Feature ID**: `FEAT-002-02`

Tools for agents to create and manage work items during planning and execution.

### User Stories

#### US-004: Create Work Item Tool
**As an** agent
**I want** to create work items at any level of the hierarchy
**So that** I can help users plan and track work

**Acceptance Criteria**:
- Tool accepts type, title, description, parent_id
- Auto-assigns item number (EPIC-001, FEAT-001, etc.)
- Validates parent-child relationships
- Returns created item with ID

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-004-01 | Create `WorkItemToolDefinitions.cs` | 2h | Tools |
| T-004-02 | Implement `CreateWorkItemHandler` | 3h | Tools |
| T-004-03 | Add parent validation | 1h | Tools |
| T-004-04 | Write handler tests | 2h | Testing |

**Slice Details**:

##### T-004-01: Tool definitions and provider
```csharp
// src/OpenCortex.Tools.WorkItems/WorkItemToolDefinitionProvider.cs
using OpenCortex.Tools;

namespace OpenCortex.Tools.WorkItems;

public sealed class WorkItemToolDefinitionProvider : IToolDefinitionProvider
{
    public IEnumerable<ToolDefinition> GetToolDefinitions()
    {
        yield return WorkItemToolDefinitions.CreateWorkItem;
        yield return WorkItemToolDefinitions.ListWorkItems;
        yield return WorkItemToolDefinitions.UpdateWorkItem;
        yield return WorkItemToolDefinitions.GetWorkItem;
        yield return WorkItemToolDefinitions.PlanEpic;
    }
}

// src/OpenCortex.Tools.WorkItems/WorkItemToolDefinitions.cs
public static class WorkItemToolDefinitions
{
    public static ToolDefinition CreateWorkItem => new()
    {
        Type = "function",
        Function = new FunctionDefinition
        {
            Name = "create_work_item",
            Description = """
                Create a work item (epic, feature, user story, or task) for planning and tracking.

                Hierarchy:
                - Epic: Large initiative or project
                - Feature: Distinct capability within an epic
                - User Story: User-facing functionality within a feature
                - Task: Specific work to implement a story

                Use parent_id to place items in the hierarchy.
                """,
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    item_type = new
                    {
                        type = "string",
                        description = "Type of work item",
                        @enum = new[] { "epic", "feature", "user_story", "task" }
                    },
                    title = new
                    {
                        type = "string",
                        description = "Short title for the work item"
                    },
                    description = new
                    {
                        type = "string",
                        description = "Detailed description"
                    },
                    parent_id = new
                    {
                        type = "string",
                        description = "Parent work item ID (required for feature/story/task)"
                    },
                    // User story format
                    persona = new
                    {
                        type = "string",
                        description = "For user stories: 'As a {persona}'"
                    },
                    goal = new
                    {
                        type = "string",
                        description = "For user stories: 'I want {goal}'"
                    },
                    benefit = new
                    {
                        type = "string",
                        description = "For user stories: 'So that {benefit}'"
                    },
                    // Planning
                    acceptance_criteria = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "List of acceptance criteria"
                    },
                    priority = new
                    {
                        type = "string",
                        @enum = new[] { "critical", "high", "medium", "low" },
                        @default = "medium"
                    },
                    estimated_hours = new
                    {
                        type = "number",
                        description = "Estimated hours to complete"
                    },
                    tags = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Tags for categorization"
                    }
                },
                required = new[] { "item_type", "title" }
            }
        }
    };

    public static ToolDefinition ListWorkItems => new()
    {
        Type = "function",
        Function = new FunctionDefinition
        {
            Name = "list_work_items",
            Description = "List work items with optional filtering by type, status, or parent.",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    item_type = new
                    {
                        type = "string",
                        @enum = new[] { "epic", "feature", "user_story", "task" }
                    },
                    status = new
                    {
                        type = "string",
                        @enum = new[] { "backlog", "planned", "in_progress", "blocked", "in_review", "done" }
                    },
                    parent_id = new
                    {
                        type = "string",
                        description = "Filter to children of this item"
                    },
                    include_children = new
                    {
                        type = "boolean",
                        description = "Include child items in response",
                        @default = false
                    },
                    limit = new
                    {
                        type = "integer",
                        @default = 20
                    }
                }
            }
        }
    };

    public static ToolDefinition UpdateWorkItem => new()
    {
        Type = "function",
        Function = new FunctionDefinition
        {
            Name = "update_work_item",
            Description = "Update a work item's status, assignment, or other fields.",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    work_item_id = new
                    {
                        type = "string",
                        description = "Work item ID or formatted ID (e.g., 'EPIC-001', 'US-042')"
                    },
                    status = new
                    {
                        type = "string",
                        @enum = new[] { "backlog", "planned", "in_progress", "blocked", "in_review", "done", "cancelled" }
                    },
                    title = new { type = "string" },
                    description = new { type = "string" },
                    result = new
                    {
                        type = "string",
                        description = "Result or outcome (typically set when completing)"
                    },
                    actual_hours = new
                    {
                        type = "number",
                        description = "Actual hours spent"
                    },
                    add_acceptance_criteria = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Additional acceptance criteria to add"
                    }
                },
                required = new[] { "work_item_id" }
            }
        }
    };

    public static ToolDefinition GetWorkItem => new()
    {
        Type = "function",
        Function = new FunctionDefinition
        {
            Name = "get_work_item",
            Description = "Get details of a work item including its hierarchy.",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    work_item_id = new
                    {
                        type = "string",
                        description = "Work item ID or formatted ID (e.g., 'EPIC-001', 'US-042')"
                    },
                    include_children = new
                    {
                        type = "boolean",
                        description = "Include child items",
                        @default = true
                    },
                    children_depth = new
                    {
                        type = "integer",
                        description = "How many levels of children to include (1-3)",
                        @default = 2
                    }
                },
                required = new[] { "work_item_id" }
            }
        }
    };

    public static ToolDefinition PlanEpic => new()
    {
        Type = "function",
        Function = new FunctionDefinition
        {
            Name = "plan_epic",
            Description = """
                Break down an epic into features, user stories, and tasks.
                Provide the epic details and I'll create the full hierarchy.
                """,
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    title = new
                    {
                        type = "string",
                        description = "Epic title"
                    },
                    description = new
                    {
                        type = "string",
                        description = "Detailed description of the epic"
                    },
                    context = new
                    {
                        type = "string",
                        description = "Additional context, constraints, or requirements"
                    },
                    breakdown_depth = new
                    {
                        type = "string",
                        @enum = new[] { "features", "stories", "tasks" },
                        description = "How deep to break down (default: tasks)",
                        @default = "tasks"
                    }
                },
                required = new[] { "title", "description" }
            }
        }
    };
}
```

##### T-004-02: CreateWorkItemHandler
```csharp
// src/OpenCortex.Tools/WorkItems/CreateWorkItemHandler.cs
public sealed class CreateWorkItemHandler : IToolHandler
{
    private readonly IWorkItemRepository _workItemRepo;
    private readonly ILogger<CreateWorkItemHandler> _logger;

    public string ToolName => "create_work_item";
    public string Category => "planning";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var itemTypeStr = arguments.GetProperty("item_type").GetString()
            ?? throw new ArgumentException("item_type is required");

        var itemType = Enum.Parse<WorkItemType>(itemTypeStr, ignoreCase: true);

        var title = arguments.GetProperty("title").GetString()
            ?? throw new ArgumentException("title is required");

        var description = arguments.TryGetProperty("description", out var descEl)
            ? descEl.GetString()
            : null;

        var parentId = arguments.TryGetProperty("parent_id", out var parentEl)
            ? parentEl.GetString()
            : null;

        // Validate parent relationship
        if (itemType != WorkItemType.Epic && string.IsNullOrEmpty(parentId))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"A {itemType} requires a parent_id"
            });
        }

        WorkItem? parent = null;
        if (!string.IsNullOrEmpty(parentId))
        {
            parent = await _workItemRepo.GetByIdAsync(parentId, cancellationToken);
            if (parent is null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Parent work item not found: {parentId}"
                });
            }

            if (!WorkItemHierarchy.CanBeChildOf(itemType, parent.ItemType))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"A {itemType} cannot be a child of a {parent.ItemType}"
                });
            }
        }

        var workItem = new WorkItem
        {
            CustomerId = context.CustomerId,
            UserId = context.UserId,
            ParentId = parentId,
            ItemType = itemType,
            Title = title,
            Description = description,
            ConversationId = context.ConversationId
        };

        // User story format
        if (arguments.TryGetProperty("persona", out var personaEl))
            workItem.Persona = personaEl.GetString();
        if (arguments.TryGetProperty("goal", out var goalEl))
            workItem.Goal = goalEl.GetString();
        if (arguments.TryGetProperty("benefit", out var benefitEl))
            workItem.Benefit = benefitEl.GetString();

        // Acceptance criteria
        if (arguments.TryGetProperty("acceptance_criteria", out var acEl))
        {
            workItem.AcceptanceCriteria = acEl.EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();
        }

        // Priority
        if (arguments.TryGetProperty("priority", out var priEl))
        {
            workItem.Priority = Enum.Parse<WorkItemPriority>(
                priEl.GetString() ?? "medium", ignoreCase: true);
        }

        // Estimated hours
        if (arguments.TryGetProperty("estimated_hours", out var estEl))
        {
            workItem.EstimatedHours = estEl.GetDecimal();
        }

        // Tags
        if (arguments.TryGetProperty("tags", out var tagsEl))
        {
            workItem.Tags = tagsEl.EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();
        }

        await _workItemRepo.CreateAsync(workItem, cancellationToken);

        _logger.LogInformation(
            "Created {ItemType} {FormattedId}: {Title}",
            workItem.ItemType, workItem.FormattedId, workItem.Title);

        return JsonSerializer.Serialize(new
        {
            success = true,
            work_item_id = workItem.WorkItemId,
            formatted_id = workItem.FormattedId,
            item_type = workItem.ItemType.ToString().ToLowerInvariant(),
            title = workItem.Title,
            parent_id = workItem.ParentId,
            message = $"Created {workItem.FormattedId}: {workItem.Title}"
        });
    }
}
```

---

#### US-005: Plan Epic Tool (AI-Assisted Breakdown)
**As a** user
**I want** AI to help break down an epic into features/stories/tasks
**So that** I can quickly generate a detailed plan

**Acceptance Criteria**:
- Tool takes epic description and generates hierarchy
- Uses LLM to intelligently break down the work
- Creates all items in database
- Returns full plan structure

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-005-01 | Implement `PlanEpicHandler` | 4h | Tools |
| T-005-02 | Create planning prompt template | 2h | Prompts |
| T-005-03 | Parse LLM output into work items | 2h | Tools |
| T-005-04 | Batch create work items | 1h | Tools |
| T-005-05 | Write planning tests | 2h | Testing |

**Slice Details**:

##### T-005-02: Planning prompt template
```csharp
// src/OpenCortex.Tools/WorkItems/PlanningPrompts.cs
public static class PlanningPrompts
{
    public static string EpicBreakdown(string title, string description, string? context) => $"""
        You are a project planning expert. Break down the following epic into a structured hierarchy.

        # Epic
        **Title**: {title}
        **Description**: {description}
        {(context != null ? $"**Context**: {context}" : "")}

        # Instructions
        Create a breakdown following this structure:
        1. Features (2-5 distinct capabilities)
        2. User Stories per feature (2-4 per feature, in "As a X, I want Y, so that Z" format)
        3. Tasks per user story (2-6 specific implementation tasks)

        # Output Format
        Return JSON in this exact structure:
        ```json
        {{
          "features": [
            {{
              "title": "Feature title",
              "description": "Feature description",
              "user_stories": [
                {{
                  "title": "Story title",
                  "persona": "user type",
                  "goal": "what they want",
                  "benefit": "why they want it",
                  "acceptance_criteria": ["criterion 1", "criterion 2"],
                  "tasks": [
                    {{
                      "title": "Task title",
                      "description": "What needs to be done",
                      "estimated_hours": 2
                    }}
                  ]
                }}
              ]
            }}
          ]
        }}
        ```

        Be specific and actionable. Each task should be completable in 1-8 hours.
        """;
}
```

---

#### US-006: List & Query Work Items
**As an** agent or user
**I want** to query work items with filters
**So that** I can find relevant items to work on

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-006-01 | Implement `ListWorkItemsHandler` | 2h | Tools |
| T-006-02 | Implement `GetWorkItemHandler` | 2h | Tools |
| T-006-03 | Add hierarchy loading options | 1h | Tools |
| T-006-04 | Write query tests | 1h | Testing |

---

#### US-007: Update Work Item Status
**As an** agent
**I want** to update work item status and results
**So that** progress is tracked accurately

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-007-01 | Implement `UpdateWorkItemHandler` | 3h | Tools |
| T-007-02 | Add status transition validation | 1h | Tools |
| T-007-03 | Record history on update | 1h | Tools |
| T-007-04 | Write update tests | 2h | Testing |

---

## Feature 3: Work Item Context Integration

**Feature ID**: `FEAT-002-03`

Integrate work items into the agentic orchestration flow.

### User Stories

#### US-008: Work Item Context Injection
**As an** agent
**I want** relevant work items in my context
**So that** I know what I'm working on

**Acceptance Criteria**:
- Active work items appear in system prompt
- Current task/story context included
- Parent hierarchy visible
- Bounded context size

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-008-01 | Create `WorkItemContextBuilder` service | 3h | Orchestration |
| T-008-02 | Integrate into `AgenticOrchestrationEngine` | 2h | Orchestration |
| T-008-03 | Add configuration options | 1h | Configuration |
| T-008-04 | Write integration tests | 2h | Testing |

**Slice Details**:

##### T-008-01: WorkItemContextBuilder
```csharp
// src/OpenCortex.Orchestration/WorkItems/WorkItemContextBuilder.cs
public interface IWorkItemContextBuilder
{
    Task<string?> BuildContextAsync(
        Guid userId,
        string? activeWorkItemId = null,
        int maxItems = 5,
        CancellationToken ct = default);
}

public sealed class WorkItemContextBuilder : IWorkItemContextBuilder
{
    private readonly IWorkItemRepository _workItemRepo;

    public async Task<string?> BuildContextAsync(
        Guid userId,
        string? activeWorkItemId = null,
        int maxItems = 5,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Active Work Items");
        sb.AppendLine();

        // If specific item is active, show its full context
        if (!string.IsNullOrEmpty(activeWorkItemId))
        {
            var item = await _workItemRepo.GetWithHierarchyAsync(activeWorkItemId, depth: 2, ct);
            if (item != null)
            {
                sb.AppendLine($"### Currently Working On: {item.FormattedId}");
                sb.AppendLine($"**{item.ItemType}**: {item.Title}");
                if (!string.IsNullOrEmpty(item.Description))
                    sb.AppendLine($"**Description**: {item.Description}");
                if (item.AcceptanceCriteria.Any())
                {
                    sb.AppendLine("**Acceptance Criteria**:");
                    foreach (var ac in item.AcceptanceCriteria)
                        sb.AppendLine($"- [ ] {ac}");
                }
                sb.AppendLine();
            }
        }

        // Show in-progress items
        var inProgress = await _workItemRepo.GetByCustomerAsync(
            customerId: null!, // Will filter by user
            type: null,
            status: WorkItemStatus.InProgress,
            limit: maxItems,
            ct);

        var userItems = inProgress.Where(i => i.UserId == userId).ToList();

        if (userItems.Any())
        {
            sb.AppendLine("### In Progress");
            foreach (var item in userItems)
            {
                sb.AppendLine($"- **{item.FormattedId}** [{item.ItemType}]: {item.Title}");
            }
            sb.AppendLine();
        }

        return sb.Length > 30 ? sb.ToString() : null;
    }
}
```

---

#### US-009: Conversation-Work Item Linking
**As a** user
**I want** work items created in a conversation linked to it
**So that** I can see what items came from which chat

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-009-01 | Pass conversation_id to work item creation | 1h | Orchestration |
| T-009-02 | Add `GetByConversationAsync` repository method | 1h | Persistence |
| T-009-03 | Include work items in conversation API response | 2h | API |
| T-009-04 | Write integration tests | 1h | Testing |

---

## Feature 4: Work Item API Endpoints

**Feature ID**: `FEAT-002-04`

REST API for work item management.

### User Stories

#### US-010: Work Item CRUD API
**As a** frontend developer
**I want** REST endpoints for work items
**So that** the Portal UI can manage them

**Acceptance Criteria**:
- Full CRUD operations
- Filtering and pagination
- Hierarchy navigation
- Proper authorization

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-010-01 | Create `WorkItemEndpoints.cs` | 1h | API |
| T-010-02 | Implement list endpoint with filters | 2h | API |
| T-010-03 | Implement get with hierarchy | 2h | API |
| T-010-04 | Implement create endpoint | 2h | API |
| T-010-05 | Implement update endpoint | 2h | API |
| T-010-06 | Implement delete endpoint | 1h | API |
| T-010-07 | Add rate limiting | 30m | API |
| T-010-08 | Write API tests | 3h | Testing |

**Slice Details**:

##### T-010-01: WorkItemEndpoints
```csharp
// src/OpenCortex.Api/WorkItemEndpoints.cs
using OpenCortex.Domain.WorkItems;

namespace OpenCortex.Api;

public static class WorkItemEndpoints
{
    public static void MapWorkItemEndpoints(this WebApplication app)
    {
        var items = app.MapGroup("/api/work-items")
            .RequireAuthorization()
            .RequireRateLimiting("tenant-api");

        // List with filters
        items.MapGet("/", ListWorkItemsAsync);

        // Get single item with hierarchy
        items.MapGet("/{id}", GetWorkItemAsync);

        // Get children of an item
        items.MapGet("/{id}/children", GetChildrenAsync);

        // Get stats for an item (or all if no id)
        items.MapGet("/stats", GetStatsAsync);
        items.MapGet("/{id}/stats", GetItemStatsAsync);

        // Get history for an item
        items.MapGet("/{id}/history", GetHistoryAsync);

        // Create
        items.MapPost("/", CreateWorkItemAsync);

        // Update
        items.MapPatch("/{id}", UpdateWorkItemAsync);

        // Delete
        items.MapDelete("/{id}", DeleteWorkItemAsync);

        // Bulk operations
        items.MapPost("/bulk", BulkCreateAsync);
        items.MapPatch("/bulk", BulkUpdateAsync);

        // Move in hierarchy
        items.MapPost("/{id}/move", MoveWorkItemAsync);
    }

    // DTOs
    public sealed record ListWorkItemsRequest(
        string? ItemType = null,
        string? Status = null,
        string? ParentId = null,
        string? SprintId = null,
        string? AssignedTo = null,
        string? Tag = null,
        bool IncludeChildren = false,
        int Limit = 50,
        int Offset = 0
    );

    public sealed record CreateWorkItemRequest(
        string ItemType,
        string Title,
        string? Description = null,
        string? ParentId = null,
        string? Persona = null,
        string? Goal = null,
        string? Benefit = null,
        string? Priority = "medium",
        string[]? AcceptanceCriteria = null,
        decimal? EstimatedHours = null,
        string[]? Tags = null,
        string? SprintId = null
    );

    public sealed record UpdateWorkItemRequest(
        string? Title = null,
        string? Description = null,
        string? Status = null,
        string? Priority = null,
        string? AssignedTo = null,
        string? AssignedAgent = null,
        string[]? AcceptanceCriteria = null,
        decimal? EstimatedHours = null,
        decimal? ActualHours = null,
        string? Result = null,
        string? ResultSummary = null,
        string? SprintId = null,
        string[]? Tags = null
    );

    public sealed record WorkItemResponse(
        string WorkItemId,
        string FormattedId,
        string ItemType,
        string Title,
        string? Description,
        string? ParentId,
        string Status,
        string Priority,
        string? Persona,
        string? Goal,
        string? Benefit,
        string? UserStoryFormat,
        string[]? AcceptanceCriteria,
        decimal? EstimatedHours,
        decimal? ActualHours,
        string? AssignedTo,
        string? AssignedAgent,
        string? ConversationId,
        string? BrainId,
        string? SprintId,
        string[] Tags,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? DueDate,
        WorkItemResponse[]? Children = null
    );

    private static async Task<IResult> ListWorkItemsAsync(
        [AsParameters] ListWorkItemsRequest request,
        IWorkItemRepository repo,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var customerId = user.FindFirstValue("customer_id")!;
        var itemType = request.ItemType != null
            ? Enum.Parse<WorkItemType>(request.ItemType, ignoreCase: true)
            : (WorkItemType?)null;
        var status = request.Status != null
            ? Enum.Parse<WorkItemStatus>(request.Status, ignoreCase: true)
            : (WorkItemStatus?)null;

        var items = await repo.GetByCustomerAsync(
            customerId, itemType, status, request.Limit, ct);

        var response = items.Select(MapToResponse).ToArray();
        return Results.Ok(new { items = response, total = response.Length });
    }

    private static async Task<IResult> CreateWorkItemAsync(
        CreateWorkItemRequest request,
        IWorkItemRepository repo,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var customerId = user.FindFirstValue("customer_id")!;
        var userId = Guid.Parse(user.FindFirstValue("sub")!);

        var itemType = Enum.Parse<WorkItemType>(request.ItemType, ignoreCase: true);

        // Validate parent if provided
        if (!string.IsNullOrEmpty(request.ParentId))
        {
            var parent = await repo.GetByIdAsync(request.ParentId, ct);
            if (parent is null)
                return Results.BadRequest(new { error = $"Parent not found: {request.ParentId}" });

            if (!WorkItemHierarchy.CanBeChildOf(itemType, parent.ItemType))
                return Results.BadRequest(new { error = $"A {itemType} cannot be a child of a {parent.ItemType}" });
        }
        else if (itemType != WorkItemType.Epic)
        {
            return Results.BadRequest(new { error = $"A {itemType} requires a parent_id" });
        }

        var item = new WorkItem
        {
            CustomerId = customerId,
            UserId = userId,
            ParentId = request.ParentId,
            ItemType = itemType,
            Title = request.Title,
            Description = request.Description,
            Persona = request.Persona,
            Goal = request.Goal,
            Benefit = request.Benefit,
            Priority = Enum.Parse<WorkItemPriority>(request.Priority ?? "medium", ignoreCase: true),
            AcceptanceCriteria = request.AcceptanceCriteria?.ToList() ?? [],
            EstimatedHours = request.EstimatedHours,
            Tags = request.Tags?.ToList() ?? [],
            SprintId = request.SprintId
        };

        await repo.CreateAsync(item, ct);

        return Results.Created($"/api/work-items/{item.WorkItemId}", MapToResponse(item));
    }

    private static WorkItemResponse MapToResponse(WorkItem item) => new(
        item.WorkItemId,
        item.FormattedId,
        item.ItemType.ToString().ToLowerInvariant(),
        item.Title,
        item.Description,
        item.ParentId,
        item.Status.ToString().ToLowerInvariant(),
        item.Priority.ToString().ToLowerInvariant(),
        item.Persona,
        item.Goal,
        item.Benefit,
        item.UserStoryFormat,
        item.AcceptanceCriteria.ToArray(),
        item.EstimatedHours,
        item.ActualHours,
        item.AssignedTo?.ToString(),
        item.AssignedAgent,
        item.ConversationId,
        item.BrainId,
        item.SprintId,
        item.Tags.ToArray(),
        item.CreatedAt,
        item.UpdatedAt,
        item.StartedAt,
        item.CompletedAt,
        item.DueDate,
        item.Children?.Select(MapToResponse).ToArray()
    );
}
```

---

#### US-011: Work Item History API
**As a** user
**I want** to see the history of changes to a work item
**So that** I can track what happened

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-011-01 | Add history endpoint | 1h | API |
| T-011-02 | Implement history repository query | 1h | Persistence |
| T-011-03 | Write history tests | 1h | Testing |

---

## Feature 5: Portal UI Integration

**Feature ID**: `FEAT-002-05`

Comprehensive work item management UI in the Portal, inspired by Azure DevOps boards.

### UI Architecture

```
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚                        Portal Navigation                          â”‚
â”œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¤
â”‚  [Dashboard] [Chat] [Brains] [Work Items â–¼] [Agents] [Settings] â”‚
â”‚                              â”‚                                    â”‚
â”‚                              â”œâ”€â”€ Board View                      â”‚
â”‚                              â”œâ”€â”€ Backlog View                    â”‚
â”‚                              â”œâ”€â”€ Sprints                         â”‚
â”‚                              â””â”€â”€ Hierarchy View                  â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

### User Stories

#### US-012: Kanban Board View (ADO-Style)
**As a** user
**I want** a kanban board like Azure DevOps
**So that** I can visualize and manage work across status columns

**Acceptance Criteria**:
- Columns for each status (Backlog, Planned, In Progress, In Review, Done)
- Cards show: ID, title, type badge, assignee avatar, priority indicator
- Drag-and-drop between columns updates status
- Swimlanes by Epic or Feature (collapsible)
- WIP limits per column (configurable)
- Quick filters: assignee, priority, tags, type
- Column collapse/expand

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-012-01 | Create `WorkItemBoard` React component with column layout | 4h | Frontend |
| T-012-02 | Create `WorkItemCard` component with type badges, avatars | 2h | Frontend |
| T-012-03 | Implement drag-and-drop with `@dnd-kit` or `react-beautiful-dnd` | 4h | Frontend |
| T-012-04 | Add swimlane grouping by Epic/Feature | 3h | Frontend |
| T-012-05 | Implement WIP limits with visual warnings | 2h | Frontend |
| T-012-06 | Add filter bar (assignee, priority, tags, search) | 3h | Frontend |
| T-012-07 | Add column collapse/expand | 1h | Frontend |
| T-012-08 | Connect to work items API with optimistic updates | 3h | Frontend |
| T-012-09 | Add real-time updates via WebSocket/SSE | 2h | Frontend |

**Slice Details**:

##### T-012-01: WorkItemBoard component
```tsx
// src/OpenCortex.Portal/Frontend/src/components/WorkItems/WorkItemBoard.tsx
interface BoardColumn {
  status: WorkItemStatus;
  title: string;
  wipLimit?: number;
  collapsed: boolean;
}

interface BoardProps {
  columns: BoardColumn[];
  swimlaneGroupBy: 'none' | 'epic' | 'feature' | 'assignee';
  filters: WorkItemFilters;
  onStatusChange: (itemId: string, newStatus: WorkItemStatus) => void;
}

export function WorkItemBoard({ columns, swimlaneGroupBy, filters, onStatusChange }: BoardProps) {
  const { data: workItems, isLoading } = useWorkItems(filters);
  const swimlanes = useMemo(() => groupBySwimlane(workItems, swimlaneGroupBy), [workItems, swimlaneGroupBy]);

  return (
    <DndContext onDragEnd={handleDragEnd}>
      <div className="board-container">
        <BoardHeader columns={columns} />
        {swimlanes.map(swimlane => (
          <Swimlane key={swimlane.id} title={swimlane.title} collapsible>
            <div className="board-row">
              {columns.map(column => (
                <BoardColumn
                  key={column.status}
                  column={column}
                  items={swimlane.items.filter(i => i.status === column.status)}
                  wipLimit={column.wipLimit}
                />
              ))}
            </div>
          </Swimlane>
        ))}
      </div>
    </DndContext>
  );
}
```

##### T-012-02: WorkItemCard component
```tsx
// src/OpenCortex.Portal/Frontend/src/components/WorkItems/WorkItemCard.tsx
interface WorkItemCardProps {
  item: WorkItem;
  onClick: () => void;
  isDragging?: boolean;
}

export function WorkItemCard({ item, onClick, isDragging }: WorkItemCardProps) {
  return (
    <div
      className={cn("work-item-card", { dragging: isDragging })}
      onClick={onClick}
    >
      <div className="card-header">
        <TypeBadge type={item.itemType} />
        <span className="item-id">{item.formattedId}</span>
        <PriorityIndicator priority={item.priority} />
      </div>
      <div className="card-title">{item.title}</div>
      <div className="card-footer">
        {item.assignedTo && <UserAvatar userId={item.assignedTo} size="sm" />}
        {item.tags.slice(0, 2).map(tag => (
          <Tag key={tag} label={tag} size="xs" />
        ))}
        {item.tags.length > 2 && <span className="more-tags">+{item.tags.length - 2}</span>}
      </div>
    </div>
  );
}

function TypeBadge({ type }: { type: WorkItemType }) {
  const colors = {
    epic: 'purple',
    feature: 'blue',
    user_story: 'green',
    task: 'gray'
  };
  const icons = {
    epic: 'âš¡',
    feature: 'âœ¨',
    user_story: 'ðŸ“–',
    task: 'âœ“'
  };
  return (
    <span className={`type-badge type-${type}`} style={{ backgroundColor: colors[type] }}>
      {icons[type]}
    </span>
  );
}
```

---

#### US-013: Backlog View
**As a** user
**I want** a prioritized backlog list
**So that** I can manage and prioritize upcoming work

**Acceptance Criteria**:
- Flat list with drag-to-reorder (priority ordering)
- Bulk selection and actions
- Quick add item inline
- Expand to show children
- Sprint assignment (drag to sprint)

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-013-01 | Create `BacklogView` component with sortable list | 3h | Frontend |
| T-013-02 | Implement drag-to-reorder with priority updates | 2h | Frontend |
| T-013-03 | Add bulk selection and bulk actions menu | 2h | Frontend |
| T-013-04 | Add inline quick-add row | 2h | Frontend |
| T-013-05 | Add expandable children preview | 2h | Frontend |
| T-013-06 | Connect to API with optimistic reordering | 2h | Frontend |

**Slice Details**:

##### T-013-01: BacklogView component
```tsx
// src/OpenCortex.Portal/Frontend/src/components/WorkItems/BacklogView.tsx
export function BacklogView() {
  const [selectedItems, setSelectedItems] = useState<Set<string>>(new Set());
  const { data: backlog } = useWorkItems({ status: ['backlog', 'planned'] });

  return (
    <div className="backlog-view">
      <BacklogToolbar
        selectedCount={selectedItems.size}
        onBulkAction={handleBulkAction}
      />
      <SortableContext items={backlog.map(i => i.workItemId)}>
        {backlog.map((item, index) => (
          <BacklogRow
            key={item.workItemId}
            item={item}
            index={index}
            selected={selectedItems.has(item.workItemId)}
            onSelect={() => toggleSelection(item.workItemId)}
            onExpand={() => loadChildren(item.workItemId)}
          />
        ))}
      </SortableContext>
      <QuickAddRow onAdd={handleQuickAdd} />
    </div>
  );
}
```

---

#### US-014: Sprint Planning View
**As a** user
**I want** to organize work into sprints/iterations
**So that** I can plan time-boxed delivery

**Acceptance Criteria**:
- Create sprints with start/end dates
- Drag items from backlog to sprint
- Sprint capacity planning (hours)
- Sprint burndown visualization
- Current sprint highlighted

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-014-01 | Create `sprints` table migration | 2h | Database |
| T-014-02 | Create Sprint domain entity and repository | 2h | Domain |
| T-014-03 | Create Sprint API endpoints | 2h | API |
| T-014-04 | Create `SprintPlanningView` component | 4h | Frontend |
| T-014-05 | Implement drag from backlog to sprint | 2h | Frontend |
| T-014-06 | Add sprint capacity indicator | 2h | Frontend |
| T-014-07 | Add burndown chart component | 3h | Frontend |

**Slice Details**:

##### T-014-01: Sprint table migration
```sql
-- Migration: 0013_sprints.sql
CREATE TABLE IF NOT EXISTS opencortex.sprints (
    sprint_id text PRIMARY KEY,
    customer_id text NOT NULL REFERENCES opencortex.customers(customer_id),

    name text NOT NULL,
    goal text,

    start_date date NOT NULL,
    end_date date NOT NULL,

    capacity_hours numeric(8,2),

    status text NOT NULL DEFAULT 'planned',  -- planned, active, completed

    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT valid_sprint_status CHECK (status IN ('planned', 'active', 'completed')),
    CONSTRAINT valid_date_range CHECK (end_date > start_date)
);

CREATE INDEX ix_sprints_customer ON opencortex.sprints(customer_id, status);

-- Add sprint reference to work_items (already in 0012_work_items.sql, shown here for reference)
-- ALTER TABLE opencortex.work_items ADD COLUMN sprint_id text REFERENCES opencortex.sprints(sprint_id);
```

##### T-014-02: Sprint domain entity
```csharp
// src/OpenCortex.Domain/WorkItems/Sprint.cs
namespace OpenCortex.Domain.WorkItems;

public sealed class Sprint
{
    public string SprintId { get; init; } = Ulid.NewUlid().ToString();
    public string CustomerId { get; init; } = null!;

    public string Name { get; set; } = null!;
    public string? Goal { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public decimal? CapacityHours { get; set; }

    public SprintStatus Status { get; set; } = SprintStatus.Planned;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation (not persisted)
    public IReadOnlyList<WorkItem> Items { get; set; } = [];

    // Computed
    public int DurationDays => (EndDate.ToDateTime(TimeOnly.MinValue) - StartDate.ToDateTime(TimeOnly.MinValue)).Days;
    public bool IsActive => Status == SprintStatus.Active;
    public bool IsCurrent => IsActive && DateOnly.FromDateTime(DateTime.UtcNow) >= StartDate
                                      && DateOnly.FromDateTime(DateTime.UtcNow) <= EndDate;
}

public enum SprintStatus { Planned, Active, Completed }
```

```csharp
// src/OpenCortex.Domain/WorkItems/ISprintRepository.cs
namespace OpenCortex.Domain.WorkItems;

public interface ISprintRepository
{
    Task<Sprint?> GetByIdAsync(string sprintId, CancellationToken ct);
    Task<IReadOnlyList<Sprint>> GetByCustomerAsync(
        string customerId,
        SprintStatus? status = null,
        CancellationToken ct = default);
    Task<Sprint?> GetCurrentAsync(string customerId, CancellationToken ct);
    Task CreateAsync(Sprint sprint, CancellationToken ct);
    Task UpdateAsync(Sprint sprint, CancellationToken ct);
    Task DeleteAsync(string sprintId, CancellationToken ct);
}
```

##### T-014-03: Sprint API endpoints
```csharp
// src/OpenCortex.Api/SprintEndpoints.cs
using OpenCortex.Domain.WorkItems;

namespace OpenCortex.Api;

public static class SprintEndpoints
{
    public static void MapSprintEndpoints(this WebApplication app)
    {
        var sprints = app.MapGroup("/api/sprints")
            .RequireAuthorization()
            .RequireRateLimiting("tenant-api");

        sprints.MapGet("/", ListSprintsAsync);
        sprints.MapGet("/current", GetCurrentSprintAsync);
        sprints.MapGet("/{id}", GetSprintAsync);
        sprints.MapGet("/{id}/items", GetSprintItemsAsync);
        sprints.MapGet("/{id}/burndown", GetBurndownDataAsync);
        sprints.MapPost("/", CreateSprintAsync);
        sprints.MapPatch("/{id}", UpdateSprintAsync);
        sprints.MapPost("/{id}/start", StartSprintAsync);
        sprints.MapPost("/{id}/complete", CompleteSprintAsync);
        sprints.MapDelete("/{id}", DeleteSprintAsync);
    }

    public sealed record CreateSprintRequest(
        string Name,
        string? Goal,
        DateOnly StartDate,
        DateOnly EndDate,
        decimal? CapacityHours
    );

    public sealed record SprintResponse(
        string SprintId,
        string Name,
        string? Goal,
        DateOnly StartDate,
        DateOnly EndDate,
        decimal? CapacityHours,
        string Status,
        int DurationDays,
        bool IsCurrent,
        SprintStatsResponse? Stats
    );

    public sealed record SprintStatsResponse(
        int TotalItems,
        int Completed,
        int InProgress,
        int Remaining,
        decimal? PlannedHours,
        decimal? CompletedHours,
        decimal? RemainingHours
    );

    public sealed record BurndownDataPoint(
        DateOnly Date,
        decimal PlannedRemaining,
        decimal ActualRemaining
    );

    private static async Task<IResult> GetBurndownDataAsync(
        string id,
        ISprintRepository sprintRepo,
        IWorkItemRepository workItemRepo,
        CancellationToken ct)
    {
        var sprint = await sprintRepo.GetByIdAsync(id, ct);
        if (sprint is null)
            return Results.NotFound();

        var items = await workItemRepo.GetBySprintAsync(id, ct);

        // Calculate burndown: planned linear decrease vs actual remaining
        var dataPoints = new List<BurndownDataPoint>();
        var totalHours = items.Sum(i => i.EstimatedHours ?? 0);
        var hoursPerDay = totalHours / sprint.DurationDays;

        for (var date = sprint.StartDate; date <= sprint.EndDate; date = date.AddDays(1))
        {
            var dayIndex = (date.ToDateTime(TimeOnly.MinValue) - sprint.StartDate.ToDateTime(TimeOnly.MinValue)).Days;
            var plannedRemaining = totalHours - (hoursPerDay * dayIndex);

            // Actual: sum of remaining work as of that date
            var completedByDate = items
                .Where(i => i.CompletedAt.HasValue && DateOnly.FromDateTime(i.CompletedAt.Value.DateTime) <= date)
                .Sum(i => i.EstimatedHours ?? 0);
            var actualRemaining = totalHours - completedByDate;

            dataPoints.Add(new BurndownDataPoint(date, plannedRemaining, actualRemaining));
        }

        return Results.Ok(new { burndown = dataPoints });
    }
}
```

---

#### US-015: Work Item Hierarchy View
**As a** user
**I want** a tree view of my work items
**So that** I can see the full Epic â†’ Feature â†’ Story â†’ Task structure

**Acceptance Criteria**:
- Tree view with expand/collapse
- Shows: type icon, ID, title, status, assignee
- Inline editing of title
- Right-click context menu
- Create child action
- Drag to reparent

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-015-01 | Create `WorkItemTree` component | 3h | Frontend |
| T-015-02 | Implement recursive tree rendering with virtualization | 3h | Frontend |
| T-015-03 | Add expand/collapse with state persistence | 1h | Frontend |
| T-015-04 | Add inline title editing | 2h | Frontend |
| T-015-05 | Add context menu (edit, delete, add child, move) | 2h | Frontend |
| T-015-06 | Implement drag-to-reparent | 3h | Frontend |

**Slice Details**:

##### T-015-01: WorkItemTree component
```tsx
// src/OpenCortex.Portal/Frontend/src/components/WorkItems/WorkItemTree.tsx
interface TreeNodeProps {
  item: WorkItem;
  depth: number;
  expanded: boolean;
  onToggle: () => void;
  onSelect: () => void;
}

function TreeNode({ item, depth, expanded, onToggle, onSelect }: TreeNodeProps) {
  const { data: children } = useWorkItemChildren(item.workItemId, { enabled: expanded });

  return (
    <div className="tree-node" style={{ paddingLeft: depth * 24 }}>
      <div className="tree-row" onClick={onSelect} onContextMenu={handleContextMenu}>
        {item.children?.length > 0 && (
          <button className="expand-btn" onClick={onToggle}>
            {expanded ? 'â–¼' : 'â–¶'}
          </button>
        )}
        <TypeIcon type={item.itemType} />
        <span className="item-id">{item.formattedId}</span>
        <InlineEditableText value={item.title} onSave={updateTitle} />
        <StatusBadge status={item.status} />
        {item.assignedTo && <UserAvatar userId={item.assignedTo} size="xs" />}
      </div>
      {expanded && children?.map(child => (
        <TreeNode key={child.workItemId} item={child} depth={depth + 1} />
      ))}
    </div>
  );
}
```

---

#### US-016: Work Item Detail Panel
**As a** user
**I want** a detailed view of a work item
**So that** I can see all information and history

**Acceptance Criteria**:
- Slide-out panel or modal
- All fields editable
- User story format display (As a... I want... So that...)
- Acceptance criteria checklist
- History/activity timeline
- Comments (future)
- Linked items

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-016-01 | Create `WorkItemDetailPanel` component | 4h | Frontend |
| T-016-02 | Show hierarchy breadcrumb | 1h | Frontend |
| T-016-03 | Create editable form sections | 3h | Frontend |
| T-016-04 | Add acceptance criteria checklist | 2h | Frontend |
| T-016-05 | Add history timeline | 3h | Frontend |
| T-016-06 | Add linked items section | 2h | Frontend |

**Slice Details**:

##### T-016-01: WorkItemDetailPanel component
```tsx
// src/OpenCortex.Portal/Frontend/src/components/WorkItems/WorkItemDetailPanel.tsx
export function WorkItemDetailPanel({ itemId, onClose }: { itemId: string; onClose: () => void }) {
  const { data: item, isLoading } = useWorkItem(itemId, { includeHistory: true });
  const updateMutation = useUpdateWorkItem();

  if (isLoading) return <PanelSkeleton />;

  return (
    <SlideOutPanel title={item.formattedId} onClose={onClose}>
      <Breadcrumb items={item.ancestors} />

      <Section title="Details">
        <TypeSelector value={item.itemType} disabled />
        <StatusDropdown value={item.status} onChange={s => updateMutation.mutate({ status: s })} />
        <PriorityDropdown value={item.priority} onChange={p => updateMutation.mutate({ priority: p })} />
        <AssigneeDropdown value={item.assignedTo} onChange={a => updateMutation.mutate({ assignedTo: a })} />
      </Section>

      <Section title="Description">
        <RichTextEditor value={item.description} onSave={d => updateMutation.mutate({ description: d })} />
      </Section>

      {item.itemType === 'user_story' && (
        <Section title="User Story">
          <UserStoryFields
            persona={item.persona}
            goal={item.goal}
            benefit={item.benefit}
            onSave={fields => updateMutation.mutate(fields)}
          />
        </Section>
      )}

      <Section title="Acceptance Criteria">
        <AcceptanceCriteriaChecklist
          criteria={item.acceptanceCriteria}
          onUpdate={ac => updateMutation.mutate({ acceptanceCriteria: ac })}
        />
      </Section>

      <Section title="Children">
        <ChildrenList parentId={item.workItemId} />
        <AddChildButton parentId={item.workItemId} parentType={item.itemType} />
      </Section>

      <Section title="Activity">
        <HistoryTimeline history={item.history} />
      </Section>
    </SlideOutPanel>
  );
}
```

---

#### US-017: AI Planning Assistant
**As a** user
**I want** AI to help break down work items in the UI
**So that** I can plan without using chat

**Acceptance Criteria**:
- "Break Down with AI" button on Epics/Features
- Modal shows AI generating breakdown
- Preview generated items before creation
- Edit/remove items before confirming
- Streaming generation feedback

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-017-01 | Create `AIPlanningModal` component | 4h | Frontend |
| T-017-02 | Add "Break Down" action to Epic/Feature cards | 1h | Frontend |
| T-017-03 | Implement streaming breakdown display | 3h | Frontend |
| T-017-04 | Add preview editing (reorder, remove, edit) | 3h | Frontend |
| T-017-05 | Add confirm/create all action | 2h | Frontend |
| T-017-06 | Connect to `plan_epic` tool via API | 2h | Frontend |

**Slice Details**:

##### T-017-01: AIPlanningModal component
```tsx
// src/OpenCortex.Portal/Frontend/src/components/WorkItems/AIPlanningModal.tsx
export function AIPlanningModal({ item, onClose, onCreate }: Props) {
  const [generatedItems, setGeneratedItems] = useState<GeneratedItem[]>([]);
  const [isGenerating, setIsGenerating] = useState(false);
  const planMutation = usePlanEpic();

  const handleGenerate = async () => {
    setIsGenerating(true);

    // Stream the breakdown
    const stream = await planMutation.mutateAsync({
      title: item.title,
      description: item.description,
      context: item.acceptanceCriteria?.join('\n')
    });

    for await (const chunk of stream) {
      setGeneratedItems(prev => [...prev, ...chunk.items]);
    }

    setIsGenerating(false);
  };

  return (
    <Modal title="Break Down with AI" size="xl" onClose={onClose}>
      <div className="planning-modal">
        <div className="source-item">
          <h3>{item.formattedId}: {item.title}</h3>
          <p>{item.description}</p>
        </div>

        {!isGenerating && generatedItems.length === 0 && (
          <Button onClick={handleGenerate} variant="primary">
            Generate Breakdown
          </Button>
        )}

        {isGenerating && (
          <div className="generating">
            <Spinner /> Generating breakdown...
          </div>
        )}

        {generatedItems.length > 0 && (
          <>
            <GeneratedItemsPreview
              items={generatedItems}
              onEdit={handleEditItem}
              onRemove={handleRemoveItem}
              onReorder={handleReorder}
            />
            <div className="actions">
              <Button onClick={handleGenerate} variant="secondary">
                Regenerate
              </Button>
              <Button onClick={() => onCreate(generatedItems)} variant="primary">
                Create {generatedItems.length} Items
              </Button>
            </div>
          </>
        )}
      </div>
    </Modal>
  );
}
```

---

## Portal Integration

### Portal Routing

Add work items views to the Portal navigation and routing.

```tsx
// src/OpenCortex.Portal/Frontend/src/App.tsx
// ADD to viewDefinitions:

const viewDefinitions: Record<PortalView, ViewDefinition> = {
  // ... existing views ...

  "work-items": {
    component: () => <WorkItemsView />,
    label: "Work Items",
    icon: ClipboardListIcon,
  },
  "work-items-board": {
    component: () => <WorkItemBoard />,
    label: "Board",
    icon: ViewColumnsIcon,
    parent: "work-items",
  },
  "work-items-backlog": {
    component: () => <BacklogView />,
    label: "Backlog",
    icon: ListBulletIcon,
    parent: "work-items",
  },
  "work-items-sprints": {
    component: () => <SprintPlanningView />,
    label: "Sprints",
    icon: CalendarIcon,
    parent: "work-items",
  },
  "work-items-tree": {
    component: () => <WorkItemTree />,
    label: "Hierarchy",
    icon: FolderTreeIcon,
    parent: "work-items",
  },
};
```

### Frontend API Hooks

```tsx
// src/OpenCortex.Portal/Frontend/src/hooks/useWorkItems.ts
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';

export interface WorkItem {
  workItemId: string;
  formattedId: string;
  itemType: 'epic' | 'feature' | 'user_story' | 'task';
  title: string;
  description?: string;
  parentId?: string;
  status: WorkItemStatus;
  priority: WorkItemPriority;
  persona?: string;
  goal?: string;
  benefit?: string;
  userStoryFormat?: string;
  acceptanceCriteria?: string[];
  estimatedHours?: number;
  actualHours?: number;
  assignedTo?: string;
  assignedAgent?: string;
  sprintId?: string;
  tags: string[];
  createdAt: string;
  updatedAt: string;
  children?: WorkItem[];
}

export type WorkItemStatus = 'backlog' | 'planned' | 'in_progress' | 'blocked' | 'in_review' | 'done' | 'cancelled';
export type WorkItemPriority = 'critical' | 'high' | 'medium' | 'low';
export type WorkItemType = 'epic' | 'feature' | 'user_story' | 'task';

export interface WorkItemFilters {
  itemType?: WorkItemType;
  status?: WorkItemStatus | WorkItemStatus[];
  parentId?: string;
  sprintId?: string;
  assignedTo?: string;
  tag?: string;
  includeChildren?: boolean;
  limit?: number;
}

export function useWorkItems(filters: WorkItemFilters = {}) {
  return useQuery({
    queryKey: ['work-items', filters],
    queryFn: async () => {
      const params = new URLSearchParams();
      if (filters.itemType) params.set('itemType', filters.itemType);
      if (filters.status) {
        const statuses = Array.isArray(filters.status) ? filters.status : [filters.status];
        statuses.forEach(s => params.append('status', s));
      }
      if (filters.parentId) params.set('parentId', filters.parentId);
      if (filters.sprintId) params.set('sprintId', filters.sprintId);
      if (filters.includeChildren) params.set('includeChildren', 'true');
      if (filters.limit) params.set('limit', filters.limit.toString());

      const res = await api.get(`/api/work-items?${params}`);
      return res.data as { items: WorkItem[]; total: number };
    },
  });
}

export function useWorkItem(id: string, options?: { includeHistory?: boolean }) {
  return useQuery({
    queryKey: ['work-item', id, options],
    queryFn: async () => {
      const params = new URLSearchParams();
      if (options?.includeHistory) params.set('includeHistory', 'true');
      const res = await api.get(`/api/work-items/${id}?${params}`);
      return res.data as WorkItem;
    },
    enabled: !!id,
  });
}

export function useWorkItemChildren(parentId: string, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ['work-item-children', parentId],
    queryFn: async () => {
      const res = await api.get(`/api/work-items/${parentId}/children`);
      return res.data as { items: WorkItem[] };
    },
    enabled: options?.enabled ?? true,
  });
}
```

```tsx
// src/OpenCortex.Portal/Frontend/src/hooks/useCreateWorkItem.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import type { WorkItem, WorkItemType, WorkItemPriority } from './useWorkItems';

export interface CreateWorkItemInput {
  itemType: WorkItemType;
  title: string;
  description?: string;
  parentId?: string;
  persona?: string;
  goal?: string;
  benefit?: string;
  priority?: WorkItemPriority;
  acceptanceCriteria?: string[];
  estimatedHours?: number;
  tags?: string[];
  sprintId?: string;
}

export function useCreateWorkItem() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (input: CreateWorkItemInput) => {
      const res = await api.post('/api/work-items', input);
      return res.data as WorkItem;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['work-items'] });
    },
  });
}
```

```tsx
// src/OpenCortex.Portal/Frontend/src/hooks/useUpdateWorkItem.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import type { WorkItem, WorkItemStatus, WorkItemPriority } from './useWorkItems';

export interface UpdateWorkItemInput {
  title?: string;
  description?: string;
  status?: WorkItemStatus;
  priority?: WorkItemPriority;
  assignedTo?: string;
  acceptanceCriteria?: string[];
  estimatedHours?: number;
  actualHours?: number;
  result?: string;
  sprintId?: string;
  tags?: string[];
}

export function useUpdateWorkItem(id: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (input: UpdateWorkItemInput) => {
      const res = await api.patch(`/api/work-items/${id}`, input);
      return res.data as WorkItem;
    },
    // Optimistic update for drag-and-drop
    onMutate: async (newData) => {
      await queryClient.cancelQueries({ queryKey: ['work-items'] });
      const previous = queryClient.getQueryData(['work-item', id]);

      queryClient.setQueryData(['work-item', id], (old: WorkItem | undefined) =>
        old ? { ...old, ...newData } : old
      );

      return { previous };
    },
    onError: (_err, _newData, context) => {
      if (context?.previous) {
        queryClient.setQueryData(['work-item', id], context.previous);
      }
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ['work-items'] });
      queryClient.invalidateQueries({ queryKey: ['work-item', id] });
    },
  });
}
```

```tsx
// src/OpenCortex.Portal/Frontend/src/hooks/usePlanEpic.ts
import { useMutation } from '@tanstack/react-query';
import { api } from '../lib/api';

export interface PlanEpicInput {
  title: string;
  description: string;
  context?: string;
  breakdownDepth?: 'features' | 'stories' | 'tasks';
}

export interface GeneratedItem {
  itemType: 'feature' | 'user_story' | 'task';
  title: string;
  description?: string;
  persona?: string;
  goal?: string;
  benefit?: string;
  acceptanceCriteria?: string[];
  estimatedHours?: number;
  children?: GeneratedItem[];
}

export function usePlanEpic() {
  return useMutation({
    mutationFn: async (input: PlanEpicInput) => {
      // This calls a streaming endpoint for AI-generated breakdown
      const res = await api.post('/api/work-items/plan', input, {
        headers: { 'Accept': 'text/event-stream' },
        responseType: 'stream',
      });

      // Parse SSE stream into generated items
      const items: GeneratedItem[] = [];
      // ... parse SSE events
      return items;
    },
  });
}
```

```tsx
// src/OpenCortex.Portal/Frontend/src/hooks/useSprints.ts
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';

export interface Sprint {
  sprintId: string;
  name: string;
  goal?: string;
  startDate: string;
  endDate: string;
  capacityHours?: number;
  status: 'planned' | 'active' | 'completed';
  durationDays: number;
  isCurrent: boolean;
  stats?: SprintStats;
}

export interface SprintStats {
  totalItems: number;
  completed: number;
  inProgress: number;
  remaining: number;
  plannedHours?: number;
  completedHours?: number;
  remainingHours?: number;
}

export function useSprints(status?: 'planned' | 'active' | 'completed') {
  return useQuery({
    queryKey: ['sprints', status],
    queryFn: async () => {
      const params = status ? `?status=${status}` : '';
      const res = await api.get(`/api/sprints${params}`);
      return res.data as { sprints: Sprint[] };
    },
  });
}

export function useCurrentSprint() {
  return useQuery({
    queryKey: ['sprint', 'current'],
    queryFn: async () => {
      const res = await api.get('/api/sprints/current');
      return res.data as Sprint | null;
    },
  });
}

export function useSprintBurndown(sprintId: string) {
  return useQuery({
    queryKey: ['sprint-burndown', sprintId],
    queryFn: async () => {
      const res = await api.get(`/api/sprints/${sprintId}/burndown`);
      return res.data as {
        burndown: Array<{ date: string; plannedRemaining: number; actualRemaining: number }>;
      };
    },
    enabled: !!sprintId,
  });
}
```

---

# Implementation Timeline

## Week 1: Foundation
- [ ] US-001: Work Item Entity Schema
- [ ] US-002: Work Item Status & Transitions
- [ ] US-003: Work Item Hierarchy Navigation

## Week 2: Agent Tools
- [ ] US-004: Create Work Item Tool
- [ ] US-005: Plan Epic Tool
- [ ] US-006: List & Query Work Items
- [ ] US-007: Update Work Item Status

## Week 3: Integration & API
- [ ] US-008: Work Item Context Injection
- [ ] US-009: Conversation-Work Item Linking
- [ ] US-010: Work Item CRUD API
- [ ] US-011: Work Item History API

## Week 4-5: Portal UI - Board & Backlog
- [ ] US-012: Kanban Board View (ADO-style)
- [ ] US-013: Backlog View
- [ ] US-014: Sprint Planning View

## Week 6-7: Portal UI - Tree, Detail & AI
- [ ] US-015: Hierarchy Tree View
- [ ] US-016: Work Item Detail Panel
- [ ] US-017: AI Planning Assistant

---

# Total Effort Summary

| Category | Tasks | Estimated Hours |
|----------|-------|-----------------|
| Database | 4 | 7h |
| Domain | 7 | 12h |
| Persistence | 5 | 10h |
| Tools | 12 | 28h |
| Orchestration | 4 | 8h |
| API | 12 | 20h |
| **Frontend (Work Items UI)** | **42** | **72h** |
| Testing | 14 | 22h |
| **Total** | **100** | **~179h (~6-7 weeks)** |

## Portal UI Tasks Summary

| User Story | Tasks | Hours |
|------------|-------|-------|
| US-012: Kanban Board View | 9 | 24h |
| US-013: Backlog View | 6 | 13h |
| US-014: Sprint Planning View | 7 | 17h |
| US-015: Hierarchy View | 6 | 14h |
| US-016: Detail Panel | 6 | 15h |
| US-017: AI Planning Assistant | 6 | 15h |
| **Subtotal** | **40** | **~98h** |

---

# Work Item Hierarchy Rules

```
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚                   Work Item Hierarchy                    â”‚
â”œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¤
â”‚                                                          â”‚
â”‚  EPIC-001: Multi-Agent Orchestration                    â”‚
â”‚  â”œâ”€â”€ FEAT-001: Agent Memory Layer                       â”‚
â”‚  â”‚   â”œâ”€â”€ US-001: Memory Brain Provisioning              â”‚
â”‚  â”‚   â”‚   â”œâ”€â”€ T-001: Create brain mode enum              â”‚
â”‚  â”‚   â”‚   â”œâ”€â”€ T-002: Implement provider                  â”‚
â”‚  â”‚   â”‚   â””â”€â”€ T-003: Write tests                         â”‚
â”‚  â”‚   â””â”€â”€ US-002: Save Memory Tool                       â”‚
â”‚  â”‚       â”œâ”€â”€ T-004: Create tool definition              â”‚
â”‚  â”‚       â””â”€â”€ T-005: Implement handler                   â”‚
â”‚  â””â”€â”€ FEAT-002: Task Persistence                         â”‚
â”‚      â””â”€â”€ ...                                             â”‚
â”‚                                                          â”‚
â”‚  Allowed Relationships:                                  â”‚
â”‚  â€¢ Epic â†’ Feature                                        â”‚
â”‚  â€¢ Feature â†’ User Story                                  â”‚
â”‚  â€¢ User Story â†’ Task                                     â”‚
â”‚                                                          â”‚
â”‚  Not Allowed:                                            â”‚
â”‚  â€¢ Epic â†’ User Story (skip Feature)                     â”‚
â”‚  â€¢ Feature â†’ Task (skip User Story)                     â”‚
â”‚  â€¢ Task â†’ anything (leaf nodes)                         â”‚
â”‚                                                          â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

---

# Configuration

```json
{
  "OpenCortex": {
    "WorkItems": {
      "MaxHierarchyDepth": 4,
      "DefaultPageSize": 20,
      "MaxPageSize": 100,
      "EnableAIPlanning": true,
      "PlanningModel": "claude-sonnet"
    }
  }
}
```

---

# Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Deep hierarchies | Complexity | Enforce 4-level max |
| Orphaned items | Data integrity | Cascade deletes, validation |
| Large planning outputs | Token cost | Limit breakdown depth |
| UI complexity | UX | Progressive disclosure |
