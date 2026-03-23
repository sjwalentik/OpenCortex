# Priority 4: Shared Workspace Coordination

## Epic Overview

**Epic**: Multi-Agent Workspace Collaboration

Enable multiple agents to safely read and write shared documents and workspace files with proper coordination and conflict resolution.

---

## Implementation Overview

This priority adds document locking, change tracking, and workspace coordination for multi-agent collaboration.

Migration numbering note: `0010_tenant_scoped_user_provider_configs.sql` is now part of the real sequence. Planned shared-workspace migrations in this document should therefore use `0014_document_locks.sql` and `0015_document_changes.sql`.

### Files to Create

```
src/OpenCortex.Domain/Workspace/
â”œâ”€â”€ DocumentLock.cs                       # Lock entity
â”œâ”€â”€ LockHolderType.cs                     # Enum: Agent, User
â”œâ”€â”€ IDocumentLockService.cs               # Lock service interface
â”œâ”€â”€ DocumentChange.cs                     # Change tracking entity
â”œâ”€â”€ DocumentChangeType.cs                 # Enum: Created, Updated, Deleted
â”œâ”€â”€ IDocumentChangePublisher.cs           # Change event publisher interface
â”œâ”€â”€ IDocumentChangeRepository.cs          # Change repository interface
â”œâ”€â”€ ITaskWorkspaceService.cs              # Task workspace interface
â”œâ”€â”€ ConflictInfo.cs                       # Conflict details
â””â”€â”€ MergeResult.cs                        # Merge operation result

src/OpenCortex.Persistence.Postgres/
â”œâ”€â”€ Migrations/
â”‚   â”œâ”€â”€ 0014_document_locks.sql           # Document locks table
â”‚   â””â”€â”€ 0015_document_changes.sql         # Change tracking table
â”œâ”€â”€ Repositories/
â”‚   â”œâ”€â”€ PostgresDocumentLockRepository.cs
â”‚   â””â”€â”€ PostgresDocumentChangeRepository.cs
â””â”€â”€ ServiceCollectionExtensions.cs        # ADD: Repository registration

src/OpenCortex.Orchestration/Workspace/
â”œâ”€â”€ DocumentLockService.cs                # IDocumentLockService implementation
â”œâ”€â”€ DocumentChangePublisher.cs            # IDocumentChangePublisher implementation
â”œâ”€â”€ TaskWorkspaceService.cs               # ITaskWorkspaceService implementation
â”œâ”€â”€ TextMerger.cs                         # 3-way merge utility
â””â”€â”€ LockCleanupBackgroundService.cs       # Background lock expiry cleanup

src/OpenCortex.Tools.Workspace/
â”œâ”€â”€ OpenCortex.Tools.Workspace.csproj     # New project
â”œâ”€â”€ ServiceCollectionExtensions.cs        # DI registration
â”œâ”€â”€ WorkspaceToolDefinitions.cs           # Tool JSON schemas
â”œâ”€â”€ WorkspaceToolDefinitionProvider.cs    # IToolDefinitionProvider implementation
â””â”€â”€ Handlers/
    â”œâ”€â”€ LockDocumentHandler.cs
    â”œâ”€â”€ UnlockDocumentHandler.cs
    â”œâ”€â”€ CheckLockHandler.cs
    â”œâ”€â”€ ExtendLockHandler.cs
    â”œâ”€â”€ WorkspaceReadHandler.cs
    â”œâ”€â”€ WorkspaceWriteHandler.cs
    â”œâ”€â”€ ListRecentChangesHandler.cs
    â””â”€â”€ ResolveConflictHandler.cs

src/OpenCortex.Api/
â”œâ”€â”€ WorkspaceEndpoints.cs                 # NEW: REST API for workspace
â””â”€â”€ Program.cs                            # ADD: Background service, endpoints
```

### Existing Services to Use

| Service | Location | Usage |
|---------|----------|-------|
| `NpgsqlConnection` | DI from `AddPostgresStores()` | Database access |
| `IManagedDocumentStore` | `OpenCortex.Core.Persistence` | Document CRUD |
| `IBrainCatalogStore` | `OpenCortex.Retrieval.BrainCatalog` | Create workspace brains |
| `IWorkItemRepository` | From P2 | Link locks/changes to tasks |
| `IToolHandler` | `OpenCortex.Tools` | Tool handler interface |
| `IToolDefinitionProvider` | `OpenCortex.Tools` | Tool definition provider |

### DI Registration Pattern

```csharp
// src/OpenCortex.Persistence.Postgres/ServiceCollectionExtensions.cs
// ADD to existing AddPostgresStores() method:
services.AddScoped<IDocumentLockRepository, PostgresDocumentLockRepository>();
services.AddScoped<IDocumentChangeRepository, PostgresDocumentChangeRepository>();
```

```csharp
// src/OpenCortex.Tools.Workspace/ServiceCollectionExtensions.cs (NEW FILE)
namespace OpenCortex.Tools.Workspace;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkspaceTools(this IServiceCollection services)
    {
        // Core services
        services.AddScoped<IDocumentLockService, DocumentLockService>();
        services.AddScoped<IDocumentChangePublisher, DocumentChangePublisher>();
        services.AddScoped<ITaskWorkspaceService, TaskWorkspaceService>();

        // Tool handlers
        services.AddScoped<IToolHandler, LockDocumentHandler>();
        services.AddScoped<IToolHandler, UnlockDocumentHandler>();
        services.AddScoped<IToolHandler, CheckLockHandler>();
        services.AddScoped<IToolHandler, ExtendLockHandler>();
        services.AddScoped<IToolHandler, WorkspaceReadHandler>();
        services.AddScoped<IToolHandler, WorkspaceWriteHandler>();
        services.AddScoped<IToolHandler, ListRecentChangesHandler>();
        services.AddScoped<IToolHandler, ResolveConflictHandler>();

        // Tool definitions provider
        services.AddSingleton<IToolDefinitionProvider, WorkspaceToolDefinitionProvider>();

        // Background service for lock cleanup
        services.AddHostedService<LockCleanupBackgroundService>();

        return services;
    }
}
```

```csharp
// src/OpenCortex.Api/Program.cs
// ADD after AddDelegationTools():
builder.Services.AddWorkspaceTools();

// ADD after MapAgentEndpoints():
app.MapWorkspaceEndpoints();
```

### Background Service for Lock Cleanup

```csharp
// src/OpenCortex.Orchestration/Workspace/LockCleanupBackgroundService.cs
namespace OpenCortex.Orchestration.Workspace;

public sealed class LockCleanupBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LockCleanupBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    public LockCleanupBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<LockCleanupBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var lockService = scope.ServiceProvider.GetRequiredService<IDocumentLockService>();

                var cleaned = await lockService.CleanupExpiredAsync(stoppingToken);
                if (cleaned > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} expired document locks", cleaned);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up expired locks");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
```

### Tool Definition Provider

```csharp
// src/OpenCortex.Tools.Workspace/WorkspaceToolDefinitionProvider.cs
namespace OpenCortex.Tools.Workspace;

public sealed class WorkspaceToolDefinitionProvider : IToolDefinitionProvider
{
    public IEnumerable<ToolDefinition> GetToolDefinitions()
    {
        yield return WorkspaceToolDefinitions.LockDocument;
        yield return WorkspaceToolDefinitions.UnlockDocument;
        yield return WorkspaceToolDefinitions.CheckLock;
        yield return WorkspaceToolDefinitions.ExtendLock;
        yield return WorkspaceToolDefinitions.WorkspaceRead;
        yield return WorkspaceToolDefinitions.WorkspaceWrite;
        yield return WorkspaceToolDefinitions.ListRecentChanges;
        yield return WorkspaceToolDefinitions.ResolveConflict;
    }
}
```

---

## Epic Details

| Field | Value |
|-------|-------|
| Epic ID | `EPIC-004` |
| Priority | P4 |
| Dependencies | P2 (Tasks), P3 (Delegation) |
| Estimated Effort | 4-5 weeks |
| Business Value | High - Enables collaborative agent work |

### Problem Statement

When multiple agents work on the same task:
- No coordination for concurrent file edits
- No awareness of other agents' changes
- No conflict resolution mechanism
- Changes may overwrite each other
- No shared context accumulation

### Success Criteria

- Agents can acquire advisory locks on documents
- Changes are visible to other agents
- Conflicts are detected and surfaced
- Workspace state is consistent
- Task-scoped shared knowledge accumulates

---

# Features

## Feature 1: Document Locking

**Feature ID**: `FEAT-004-01`

Advisory locking system for managed documents to prevent concurrent edit conflicts.

### User Stories

#### US-033: Acquire Document Lock
**As an** agent
**I want to** lock a document before editing
**So that** other agents know I'm working on it

**Acceptance Criteria**:
- Lock acquired via tool call
- Lock has configurable timeout (default: 60s)
- Lock shows holder identity
- Cannot lock already-locked documents
- Lock persisted in database

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-033-01 | Create `document_locks` table migration | 2h | Database |
| T-033-02 | Create `DocumentLock` domain entity | 1h | Domain |
| T-033-03 | Create `IDocumentLockService` interface | 1h | Abstractions |
| T-033-04 | Implement `DocumentLockService` | 3h | Services |
| T-033-05 | Create `lock_document` tool definition | 1h | Tools |
| T-033-06 | Implement `LockDocumentHandler` | 2h | Tools |
| T-033-07 | Write lock service tests | 2h | Testing |

**Slice Details**:

##### T-033-01: Database migration
```sql
-- Migration: 0014_document_locks.sql
CREATE TABLE IF NOT EXISTS opencortex.document_locks (
    lock_id text PRIMARY KEY,
    document_id text NOT NULL,
    brain_id text NOT NULL,

    holder_type text NOT NULL,  -- 'agent' or 'user'
    holder_id text NOT NULL,    -- agent_profile_id or user_id
    holder_name text NOT NULL,  -- Display name

    task_id text REFERENCES opencortex.tasks(task_id),
    conversation_id text,

    acquired_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,
    timeout_seconds integer NOT NULL DEFAULT 60,

    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,

    CONSTRAINT unique_document_lock UNIQUE (document_id, brain_id)
);

CREATE INDEX ix_document_locks_expiry ON opencortex.document_locks(expires_at);
CREATE INDEX ix_document_locks_holder ON opencortex.document_locks(holder_type, holder_id);
CREATE INDEX ix_document_locks_task ON opencortex.document_locks(task_id) WHERE task_id IS NOT NULL;

-- Function to clean expired locks
CREATE OR REPLACE FUNCTION opencortex.cleanup_expired_locks()
RETURNS integer AS $$
DECLARE
    deleted_count integer;
BEGIN
    DELETE FROM opencortex.document_locks WHERE expires_at < now();
    GET DIAGNOSTICS deleted_count = ROW_COUNT;
    RETURN deleted_count;
END;
$$ LANGUAGE plpgsql;
```

##### T-040-02: Domain entity
```csharp
// src/OpenCortex.Domain/Workspace/DocumentLock.cs
public sealed class DocumentLock
{
    public string LockId { get; init; } = Ulid.NewUlid().ToString();
    public string DocumentId { get; init; }
    public string BrainId { get; init; }

    public LockHolderType HolderType { get; init; }
    public string HolderId { get; init; }
    public string HolderName { get; init; }

    public string? TaskId { get; init; }
    public string? ConversationId { get; init; }

    public DateTimeOffset AcquiredAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; }
    public int TimeoutSeconds { get; init; } = 60;

    public Dictionary<string, object> Metadata { get; init; } = new();

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
}

public enum LockHolderType { Agent, User }
```

##### T-040-03: Service interface
```csharp
// src/OpenCortex.Domain/Workspace/IDocumentLockService.cs
public interface IDocumentLockService
{
    Task<LockResult> TryAcquireAsync(
        string documentId,
        string brainId,
        LockHolderType holderType,
        string holderId,
        string holderName,
        int timeoutSeconds = 60,
        string? taskId = null,
        CancellationToken ct = default);

    Task<bool> ReleaseAsync(
        string documentId,
        string brainId,
        string holderId,
        CancellationToken ct = default);

    Task<bool> ExtendAsync(
        string documentId,
        string brainId,
        string holderId,
        int additionalSeconds = 60,
        CancellationToken ct = default);

    Task<DocumentLock?> GetLockAsync(
        string documentId,
        string brainId,
        CancellationToken ct = default);

    Task<int> CleanupExpiredAsync(CancellationToken ct = default);
}

public sealed class LockResult
{
    public bool Acquired { get; init; }
    public DocumentLock? Lock { get; init; }
    public DocumentLock? ExistingLock { get; init; }  // If blocked
    public string? Error { get; init; }
}
```

##### T-040-05: Tool definition
```csharp
// src/OpenCortex.Tools/Workspace/WorkspaceToolDefinitions.cs
public static ToolDefinition LockDocument => new()
{
    Type = "function",
    Function = new FunctionDefinition
    {
        Name = "lock_document",
        Description = """
            Acquire an advisory lock on a document before editing.
            Other agents will see the lock and can wait or proceed with read-only access.
            Locks auto-expire after the timeout to prevent deadlocks.
            """,
        Parameters = new
        {
            type = "object",
            properties = new
            {
                document_path = new
                {
                    type = "string",
                    description = "Canonical path of the document to lock"
                },
                brain_id = new
                {
                    type = "string",
                    description = "Brain ID (optional if only one task workspace)"
                },
                timeout_seconds = new
                {
                    type = "integer",
                    description = "Lock timeout in seconds (default: 60, max: 300)",
                    @default = 60
                },
                reason = new
                {
                    type = "string",
                    description = "Why you need to lock this document"
                }
            },
            required = new[] { "document_path" }
        }
    }
};
```

##### T-040-06: LockDocumentHandler
```csharp
// src/OpenCortex.Tools/Workspace/LockDocumentHandler.cs
public sealed class LockDocumentHandler : IToolHandler
{
    private readonly IDocumentLockService _lockService;
    private readonly IManagedDocumentStore _documentStore;

    public string ToolName => "lock_document";
    public string Category => "workspace";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var documentPath = arguments.GetProperty("document_path").GetString()
            ?? throw new ArgumentException("document_path is required");

        var brainId = arguments.TryGetProperty("brain_id", out var brainEl)
            ? brainEl.GetString()
            : context.TaskWorkspaceBrainId;

        if (string.IsNullOrEmpty(brainId))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "brain_id required when no task workspace is active"
            });
        }

        var timeoutSeconds = arguments.TryGetProperty("timeout_seconds", out var toEl)
            ? Math.Min(toEl.GetInt32(), 300)  // Max 5 minutes
            : 60;

        var reason = arguments.TryGetProperty("reason", out var reasonEl)
            ? reasonEl.GetString()
            : null;

        // Resolve document ID from path
        var document = await _documentStore.GetByPathAsync(brainId, documentPath, cancellationToken);
        if (document is null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Document not found: {documentPath}"
            });
        }

        // Try to acquire lock
        var result = await _lockService.TryAcquireAsync(
            document.ManagedDocumentId,
            brainId,
            LockHolderType.Agent,
            context.CurrentAgentId ?? "lead-agent",
            context.CurrentAgentName ?? "Lead Agent",
            timeoutSeconds,
            context.CurrentTaskId,
            cancellationToken);

        if (!result.Acquired)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                locked_by = result.ExistingLock?.HolderName,
                expires_at = result.ExistingLock?.ExpiresAt,
                error = $"Document locked by {result.ExistingLock?.HolderName}. " +
                        $"Expires at {result.ExistingLock?.ExpiresAt:HH:mm:ss}"
            });
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            lock_id = result.Lock!.LockId,
            document_path = documentPath,
            expires_at = result.Lock.ExpiresAt,
            timeout_seconds = timeoutSeconds,
            message = $"Lock acquired. Expires in {timeoutSeconds}s."
        });
    }
}
```

---

#### US-034: Release Document Lock
**As an** agent
**I want to** release a lock when I'm done editing
**So that** other agents can access the document

**Acceptance Criteria**:
- Lock released via tool call
- Only holder can release their lock
- Release is idempotent (no error if already released)
- Auto-release on session end

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-034-01 | Create `unlock_document` tool definition | 30m | Tools |
| T-034-02 | Implement `UnlockDocumentHandler` | 1h | Tools |
| T-034-03 | Add session-end lock cleanup | 2h | Orchestration |
| T-034-04 | Write unlock tests | 1h | Testing |

**Slice Details**:

##### T-034-01: Tool definition
```csharp
public static ToolDefinition UnlockDocument => new()
{
    Type = "function",
    Function = new FunctionDefinition
    {
        Name = "unlock_document",
        Description = "Release a lock you hold on a document.",
        Parameters = new
        {
            type = "object",
            properties = new
            {
                document_path = new
                {
                    type = "string",
                    description = "Canonical path of the document to unlock"
                },
                brain_id = new
                {
                    type = "string",
                    description = "Brain ID (optional if only one task workspace)"
                }
            },
            required = new[] { "document_path" }
        }
    }
};
```

---

#### US-035: Check Document Lock Status
**As an** agent
**I want to** check if a document is locked
**So that** I know if I should wait or proceed

**Acceptance Criteria**:
- Query lock status without acquiring
- Returns holder info if locked
- Returns time until expiry
- Includes wait recommendation

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-035-01 | Create `check_lock` tool definition | 30m | Tools |
| T-035-02 | Implement `CheckLockHandler` | 1h | Tools |
| T-035-03 | Write check lock tests | 30m | Testing |

---

#### US-036: Extend Lock Duration
**As an** agent
**I want to** extend my lock if I need more time
**So that** my work isn't interrupted

**Acceptance Criteria**:
- Extend via tool call
- Only holder can extend
- Maximum total duration enforced
- Returns new expiry time

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-036-01 | Create `extend_lock` tool definition | 30m | Tools |
| T-036-02 | Implement `ExtendLockHandler` | 1h | Tools |
| T-036-03 | Add max duration check (10 min total) | 30m | Tools |
| T-036-04 | Write extend lock tests | 30m | Testing |

---

## Feature 2: Change Notifications

**Feature ID**: `FEAT-004-02`

Real-time awareness of workspace changes for coordinated multi-agent work.

### User Stories

#### US-037: Document Change Events
**As an** agent
**I want to** know when documents I care about change
**So that** I can react to updates from other agents

**Acceptance Criteria**:
- Events emitted on document create/update/delete
- Events include: document_id, change_type, changed_by
- Events published to task participants
- Queryable recent changes

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-037-01 | Create `document_changes` table migration | 1h | Database |
| T-037-02 | Create `DocumentChange` domain entity | 1h | Domain |
| T-037-03 | Create `IDocumentChangePublisher` interface | 1h | Abstractions |
| T-037-04 | Implement change publisher | 2h | Services |
| T-037-05 | Integrate into ManagedDocumentStore | 2h | Persistence |
| T-037-06 | Write change tracking tests | 2h | Testing |

**Slice Details**:

##### T-037-01: Database migration
```sql
-- Migration: 0015_document_changes.sql
CREATE TABLE IF NOT EXISTS opencortex.document_changes (
    change_id text PRIMARY KEY,
    document_id text NOT NULL,
    brain_id text NOT NULL,
    document_path text NOT NULL,

    change_type text NOT NULL,  -- 'created', 'updated', 'deleted'
    changed_by_type text NOT NULL,  -- 'agent' or 'user'
    changed_by_id text NOT NULL,
    changed_by_name text NOT NULL,

    task_id text REFERENCES opencortex.tasks(task_id),

    previous_content_hash text,
    new_content_hash text,
    change_summary text,

    created_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT valid_change_type CHECK (change_type IN ('created', 'updated', 'deleted'))
);

CREATE INDEX ix_document_changes_document ON opencortex.document_changes(document_id, created_at DESC);
CREATE INDEX ix_document_changes_brain ON opencortex.document_changes(brain_id, created_at DESC);
CREATE INDEX ix_document_changes_task ON opencortex.document_changes(task_id, created_at DESC) WHERE task_id IS NOT NULL;

-- Keep last 7 days of changes
CREATE OR REPLACE FUNCTION opencortex.cleanup_old_changes()
RETURNS integer AS $$
DECLARE
    deleted_count integer;
BEGIN
    DELETE FROM opencortex.document_changes WHERE created_at < now() - interval '7 days';
    GET DIAGNOSTICS deleted_count = ROW_COUNT;
    RETURN deleted_count;
END;
$$ LANGUAGE plpgsql;
```

##### T-037-02: Domain entity
```csharp
// src/OpenCortex.Domain/Workspace/DocumentChange.cs
public sealed class DocumentChange
{
    public string ChangeId { get; init; } = Ulid.NewUlid().ToString();
    public string DocumentId { get; init; }
    public string BrainId { get; init; }
    public string DocumentPath { get; init; }

    public DocumentChangeType ChangeType { get; init; }
    public string ChangedByType { get; init; }
    public string ChangedById { get; init; }
    public string ChangedByName { get; init; }

    public string? TaskId { get; init; }

    public string? PreviousContentHash { get; init; }
    public string? NewContentHash { get; init; }
    public string? ChangeSummary { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum DocumentChangeType { Created, Updated, Deleted }
```

##### T-037-04: Change publisher
```csharp
// src/OpenCortex.Orchestration/Workspace/DocumentChangePublisher.cs
public sealed class DocumentChangePublisher : IDocumentChangePublisher
{
    private readonly IDocumentChangeRepository _changeRepo;
    private readonly ILogger<DocumentChangePublisher> _logger;

    public async Task PublishAsync(
        DocumentChange change,
        CancellationToken cancellationToken)
    {
        await _changeRepo.CreateAsync(change, cancellationToken);

        _logger.LogInformation(
            "Document {ChangeType}: {DocumentPath} by {ChangedBy}",
            change.ChangeType,
            change.DocumentPath,
            change.ChangedByName);

        // Future: Real-time push via SignalR/WebSocket
    }
}
```

---

#### US-038: Query Recent Changes
**As an** agent
**I want to** query recent changes in my workspace
**So that** I can sync my understanding of the current state

**Acceptance Criteria**:
- Tool to query changes since timestamp
- Filter by task, brain, or document
- Returns change summaries
- Paginated results

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-038-01 | Create `list_recent_changes` tool definition | 1h | Tools |
| T-038-02 | Implement `ListRecentChangesHandler` | 2h | Tools |
| T-038-03 | Add change repository query methods | 1h | Persistence |
| T-038-04 | Write query tests | 1h | Testing |

**Slice Details**:

##### T-038-01: Tool definition
```csharp
public static ToolDefinition ListRecentChanges => new()
{
    Type = "function",
    Function = new FunctionDefinition
    {
        Name = "list_recent_changes",
        Description = "List recent document changes in the workspace. Useful for syncing with other agents' work.",
        Parameters = new
        {
            type = "object",
            properties = new
            {
                since_minutes = new
                {
                    type = "integer",
                    description = "Get changes from the last N minutes (default: 5)",
                    @default = 5
                },
                task_id = new
                {
                    type = "string",
                    description = "Filter to changes within a specific task"
                },
                limit = new
                {
                    type = "integer",
                    description = "Maximum changes to return (default: 20)",
                    @default = 20
                }
            }
        }
    }
};
```

---

## Feature 3: Task Workspace

**Feature ID**: `FEAT-004-03`

Shared workspace brain scoped to a task, accessible by all participating agents.

### User Stories

#### US-039: Task Workspace Brain
**As a** task owner
**I want** a shared brain for the task
**So that** all agents working on it share knowledge

**Acceptance Criteria**:
- Brain auto-created when task created
- Named: `task-workspace-{taskId}`
- All delegated agents have access
- Documents persist for task duration

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-039-01 | Create `ITaskWorkspaceService` interface | 1h | Abstractions |
| T-039-02 | Implement `TaskWorkspaceService` | 3h | Services |
| T-039-03 | Create workspace brain on task creation | 2h | Services |
| T-039-04 | Pass workspace brain ID to delegated agents | 1h | Orchestration |
| T-039-05 | Write workspace service tests | 2h | Testing |

**Slice Details**:

##### T-039-01: Interface
```csharp
// src/OpenCortex.Domain/Workspace/ITaskWorkspaceService.cs
public interface ITaskWorkspaceService
{
    Task<string> EnsureWorkspaceAsync(
        string taskId,
        string customerId,
        CancellationToken ct);

    Task<string?> GetWorkspaceBrainIdAsync(
        string taskId,
        CancellationToken ct);

    Task GrantAccessAsync(
        string taskId,
        string agentProfileId,
        CancellationToken ct);

    Task CleanupWorkspaceAsync(
        string taskId,
        CancellationToken ct);
}
```

##### T-039-02: Implementation
```csharp
// src/OpenCortex.Orchestration/Workspace/TaskWorkspaceService.cs
public sealed class TaskWorkspaceService : ITaskWorkspaceService
{
    private readonly IBrainCatalogStore _brainStore;
    private readonly ITaskRepository _taskRepo;
    private readonly ILogger<TaskWorkspaceService> _logger;

    public async Task<string> EnsureWorkspaceAsync(
        string taskId,
        string customerId,
        CancellationToken ct)
    {
        var brainId = $"task-workspace-{taskId}";

        var existing = await _brainStore.GetBrainAsync(brainId, ct);
        if (existing is not null)
            return brainId;

        var task = await _taskRepo.GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"Task {taskId} not found");

        await _brainStore.CreateBrainAsync(new Brain
        {
            BrainId = brainId,
            CustomerId = customerId,
            Name = $"Workspace: {task.Title[..Math.Min(30, task.Title.Length)]}",
            Description = $"Shared workspace for task: {task.Goal}",
            Mode = BrainMode.ManagedContent,
            Status = BrainStatus.Active,
            Metadata = new Dictionary<string, object>
            {
                ["task_id"] = taskId,
                ["created_for"] = "task-workspace"
            }
        }, ct);

        _logger.LogInformation(
            "Created task workspace brain {BrainId} for task {TaskId}",
            brainId, taskId);

        return brainId;
    }

    public async Task CleanupWorkspaceAsync(
        string taskId,
        CancellationToken ct)
    {
        var brainId = $"task-workspace-{taskId}";
        var brain = await _brainStore.GetBrainAsync(brainId, ct);

        if (brain is not null)
        {
            brain.Status = BrainStatus.Archived;
            await _brainStore.UpdateBrainAsync(brain, ct);

            _logger.LogInformation(
                "Archived task workspace brain {BrainId}",
                brainId);
        }
    }
}
```

---

#### US-040: Workspace Document Tools
**As an** agent
**I want to** read and write documents in the task workspace
**So that** I can share findings with other agents

**Acceptance Criteria**:
- `workspace_read` tool for reading
- `workspace_write` tool for writing
- Auto-locks on write
- Records changes for other agents

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-040-01 | Create `workspace_read` tool definition | 1h | Tools |
| T-040-02 | Implement `WorkspaceReadHandler` | 2h | Tools |
| T-040-03 | Create `workspace_write` tool definition | 1h | Tools |
| T-040-04 | Implement `WorkspaceWriteHandler` | 3h | Tools |
| T-040-05 | Auto-lock on write | 1h | Tools |
| T-040-06 | Write workspace tool tests | 2h | Testing |

---

## Feature 4: Conflict Detection & Resolution

**Feature ID**: `FEAT-004-04`

Handle concurrent edit conflicts gracefully.

### User Stories

#### US-041: Conflict Detection
**As a** workspace service
**I want to** detect when edits conflict
**So that** agents can resolve issues

**Acceptance Criteria**:
- Detect stale writes (content changed since read)
- Detect concurrent edits (multiple writers)
- Surface conflict to writing agent
- Include both versions in conflict info

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-041-01 | Add `expected_hash` param to write operations | 1h | Domain |
| T-041-02 | Implement hash comparison check | 2h | Services |
| T-041-03 | Create `ConflictResult` response type | 1h | Domain |
| T-041-04 | Return conflict info in write response | 1h | Tools |
| T-041-05 | Write conflict detection tests | 2h | Testing |

**Slice Details**:

##### T-041-01: Write with expected hash
```csharp
// Add to workspace write tool parameters
{
    expected_hash = new
    {
        type = "string",
        description = "Content hash from when you last read the document. Used for conflict detection."
    }
}

// In handler:
if (!string.IsNullOrEmpty(expectedHash))
{
    var current = await _documentStore.GetAsync(documentId, ct);
    if (current?.ContentHash != expectedHash)
    {
        return new WriteResult
        {
            Success = false,
            Conflict = new ConflictInfo
            {
                ExpectedHash = expectedHash,
                ActualHash = current?.ContentHash,
                LastModifiedBy = current?.LastModifiedBy,
                LastModifiedAt = current?.UpdatedAt
            }
        };
    }
}
```

---

#### US-042: Conflict Resolution Strategies
**As an** agent
**I want** options for resolving conflicts
**So that** work isn't lost

**Acceptance Criteria**:
- Force write (overwrite)
- Merge (for compatible changes)
- Abort and refresh
- Escalate to user

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-042-01 | Implement force write option | 1h | Tools |
| T-042-02 | Implement basic text merge | 3h | Services |
| T-042-03 | Add merge conflict markers | 1h | Services |
| T-042-04 | Create conflict resolution tool | 2h | Tools |
| T-042-05 | Write resolution tests | 2h | Testing |

**Slice Details**:

##### T-042-02: Basic text merge
```csharp
// src/OpenCortex.Orchestration/Workspace/TextMerger.cs
public static class TextMerger
{
    public static MergeResult TryMerge(
        string baseContent,
        string currentContent,
        string incomingContent)
    {
        // If changes are in different sections, merge is possible
        var baseLines = baseContent.Split('\n');
        var currentLines = currentContent.Split('\n');
        var incomingLines = incomingContent.Split('\n');

        // Simple line-based 3-way merge
        var result = new List<string>();
        var conflicts = new List<MergeConflict>();

        // ... merge algorithm ...

        if (conflicts.Count == 0)
        {
            return MergeResult.Success(string.Join('\n', result));
        }

        // Include conflict markers
        return MergeResult.WithConflicts(
            string.Join('\n', result),
            conflicts);
    }
}

public sealed class MergeResult
{
    public bool Success { get; init; }
    public string MergedContent { get; init; }
    public IReadOnlyList<MergeConflict> Conflicts { get; init; } = [];
    public bool HasConflictMarkers { get; init; }
}
```

---

#### US-043: Conflict Notifications
**As an** agent
**I want to** be notified of conflicts affecting my work
**So that** I can respond appropriately

**Acceptance Criteria**:
- Conflicts logged as change events
- Conflict status in document metadata
- Queryable conflict list
- Auto-retry suggestion

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-043-01 | Add conflict tracking to document changes | 1h | Domain |
| T-043-02 | Create `list_conflicts` tool | 1h | Tools |
| T-043-03 | Add conflict resolution guidance to response | 1h | Tools |
| T-043-04 | Write conflict notification tests | 1h | Testing |

---

# Implementation Timeline

## Week 1-2: Document Locking
- [ ] US-033: Acquire Document Lock
- [ ] US-034: Release Document Lock
- [ ] US-035: Check Document Lock Status
- [ ] US-036: Extend Lock Duration

## Week 3-4: Change Notifications
- [ ] US-037: Document Change Events
- [ ] US-038: Query Recent Changes

## Week 5-6: Task Workspace
- [ ] US-039: Task Workspace Brain
- [ ] US-040: Workspace Document Tools

## Week 7: Conflict Resolution
- [ ] US-041: Conflict Detection
- [ ] US-042: Conflict Resolution Strategies
- [ ] US-043: Conflict Notifications

---

# Total Effort Summary

| Category | Tasks | Estimated Hours |
|----------|-------|-----------------|
| Database | 2 | 3h |
| Domain | 5 | 5h |
| Abstractions | 3 | 3h |
| Services | 5 | 13h |
| Tools | 14 | 22h |
| Orchestration | 3 | 5h |
| Persistence | 2 | 3h |
| Testing | 11 | 16h |
| **Total** | **45** | **~70h (~4-5 weeks)** |

---

# Locking Protocol

```
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚                    Locking Flow                          â”‚
â”œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¤
â”‚                                                          â”‚
â”‚  1. Agent wants to edit document                         â”‚
â”‚         â”‚                                                â”‚
â”‚         â–¼                                                â”‚
â”‚  2. Call lock_document(path, timeout=60s)               â”‚
â”‚         â”‚                                                â”‚
â”‚         â”œâ”€â”€ Lock acquired â†’ Continue to edit            â”‚
â”‚         â”‚                                                â”‚
â”‚         â””â”€â”€ Lock held by other â†’                        â”‚
â”‚                 â”‚                                        â”‚
â”‚                 â”œâ”€â”€ Wait and retry (with backoff)       â”‚
â”‚                 â”‚                                        â”‚
â”‚                 â””â”€â”€ Or proceed read-only                â”‚
â”‚                                                          â”‚
â”‚  3. Make edits with workspace_write()                   â”‚
â”‚         â”‚                                                â”‚
â”‚  4. Call unlock_document(path)                          â”‚
â”‚         â”‚                                                â”‚
â”‚  5. On timeout: auto-release lock                       â”‚
â”‚                                                          â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

---

# Conflict Resolution Flow

```
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚              Conflict Resolution Flow                    â”‚
â”œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¤
â”‚                                                          â”‚
â”‚  1. Agent A reads document (hash: abc123)               â”‚
â”‚  2. Agent B reads document (hash: abc123)               â”‚
â”‚  3. Agent A writes changes (expected: abc123) â†’ Success â”‚
â”‚     Document now has hash: def456                        â”‚
â”‚  4. Agent B writes changes (expected: abc123) â†’ CONFLICTâ”‚
â”‚         â”‚                                                â”‚
â”‚         â–¼                                                â”‚
â”‚  5. Conflict detected:                                   â”‚
â”‚     - Expected: abc123                                   â”‚
â”‚     - Actual: def456                                     â”‚
â”‚     - Changed by: Agent A                                â”‚
â”‚         â”‚                                                â”‚
â”‚         â–¼                                                â”‚
â”‚  6. Resolution options:                                  â”‚
â”‚     a) Force write (overwrite Agent A's changes)        â”‚
â”‚     b) Refresh and re-apply changes                     â”‚
â”‚     c) Merge (if changes are compatible)                â”‚
â”‚     d) Abort and notify user                            â”‚
â”‚                                                          â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

---

# Configuration

```json
{
  "OpenCortex": {
    "Workspace": {
      "LockTimeoutSeconds": 60,
      "MaxLockDurationSeconds": 600,
      "EnableChangeTracking": true,
      "ChangeRetentionDays": 7,
      "AutoCleanupExpiredLocks": true,
      "ConflictResolutionMode": "prompt"
    }
  }
}
```

---

# Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Deadlocks from forgotten locks | Blocked agents | Auto-expiry, cleanup job |
| Lock contention | Slowdowns | Short timeouts, retry backoff |
| Lost updates from conflicts | Data loss | Conflict detection, merge support |
| Change tracking overhead | Performance | Async publishing, retention limits |
| Workspace brain bloat | Storage costs | Archive on task completion |
