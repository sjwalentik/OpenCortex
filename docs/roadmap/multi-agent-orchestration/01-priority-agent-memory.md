# Priority 1: Agent Memory Layer

## Epic Overview

**Epic**: Agent Memory System

Enable agents to persist and recall knowledge across sessions. Memories are documents stored in the user's brain under the `memories/` path prefix.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         MEMORY ARCHITECTURE                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  User's Brain (managed-content)                                 │
│  ├── documents/               ← Shown in Documents page         │
│  │   └── *.md                                                   │
│  └── memories/                ← Hidden from Documents,          │
│      ├── facts/*.md              shown in Memories page         │
│      ├── decisions/*.md                                         │
│      ├── preferences/*.md                                       │
│      └── learnings/*.md                                         │
│                                                                  │
│  Brain Selection:                                                │
│  • 1 brain  → Use automatically                                 │
│  • N brains → User configures "memory brain" in Account         │
│                                                                  │
│  Document quota applies to ALL documents (including memories)   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Epic Details

| Field | Value |
|-------|-------|
| Epic ID | `EPIC-001` |
| Priority | P1 |
| Dependencies | None (uses existing infrastructure) |
| Estimated Effort | 1-2 weeks |
| Business Value | High - Enables session continuity |

### Key Insight

**No new infrastructure needed.** Memory tools:
- Inject existing `IManagedDocumentStore` to save/delete
- Inject existing `OqlQueryExecutor` to search
- Save to `memories/{category}/{id}.md` canonical path
- UI filters: Documents page excludes `memories/*`, Memories page shows only `memories/*`

---

# Features

## Feature 1: Memory Brain Resolution

**Feature ID**: `FEAT-001-01`

Determine which brain to use for memories.

### User Stories

#### US-001: Resolve Memory Brain
**As an** agent system
**I want to** determine which brain stores memories
**So that** memory tools know where to save/query

**Acceptance Criteria**:
- If user has 1 brain → use it
- If user has multiple brains → use configured memory brain from user settings
- If not configured and multiple brains → prompt user to configure

**Tasks**:

| Task ID | Description | Effort |
|---------|-------------|--------|
| T-001-01 | Create IMemoryBrainResolver interface | 30m |
| T-001-02 | Implement MemoryBrainResolver | 1h |
| T-001-03 | Add memory_brain_id to user settings | 30m |
| T-001-04 | Write resolver tests | 1h |

---

##### T-001-01: Create IMemoryBrainResolver interface

**File**: `src/OpenCortex.Orchestration/Memory/IMemoryBrainResolver.cs`

```csharp
namespace OpenCortex.Orchestration.Memory;

public interface IMemoryBrainResolver
{
    /// <summary>
    /// Gets the brain ID to use for memory storage.
    /// Returns the only brain if user has one, or the configured memory brain if multiple.
    /// </summary>
    Task<MemoryBrainResult> ResolveAsync(
        string customerId,
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed record MemoryBrainResult(
    bool Success,
    string? BrainId,
    string? Error,
    bool NeedsConfiguration // True if multiple brains and none configured
);
```

---

##### T-001-02: Implement MemoryBrainResolver

**File**: `src/OpenCortex.Orchestration/Memory/MemoryBrainResolver.cs`

**Inject**:
- `IBrainCatalogStore` (from `OpenCortex.Core.Persistence`)
- `IUserSettingsStore` (from `OpenCortex.Core.Persistence`) - may need to create

```csharp
namespace OpenCortex.Orchestration.Memory;

public sealed class MemoryBrainResolver : IMemoryBrainResolver
{
    private readonly IBrainCatalogStore _brainStore;
    private readonly IUserSettingsStore _userSettings;

    public MemoryBrainResolver(
        IBrainCatalogStore brainStore,
        IUserSettingsStore userSettings)
    {
        _brainStore = brainStore;
        _userSettings = userSettings;
    }

    public async Task<MemoryBrainResult> ResolveAsync(
        string customerId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Get all brains for customer
        var brains = await _brainStore.ListBrainsByCustomerAsync(customerId, cancellationToken);
        var activeBrains = brains.Where(b => b.Status == BrainStatus.Active).ToList();

        if (activeBrains.Count == 0)
        {
            return new MemoryBrainResult(false, null, "No active brains found", false);
        }

        if (activeBrains.Count == 1)
        {
            // Single brain - use it automatically
            return new MemoryBrainResult(true, activeBrains[0].BrainId, null, false);
        }

        // Multiple brains - check user's configured memory brain
        var settings = await _userSettings.GetAsync(userId, cancellationToken);
        var memoryBrainId = settings?.MemoryBrainId;

        if (string.IsNullOrEmpty(memoryBrainId))
        {
            return new MemoryBrainResult(false, null,
                "Multiple brains found. Please configure your memory brain in Account settings.",
                NeedsConfiguration: true);
        }

        // Verify the configured brain still exists and is active
        var configuredBrain = activeBrains.FirstOrDefault(b => b.BrainId == memoryBrainId);
        if (configuredBrain is null)
        {
            return new MemoryBrainResult(false, null,
                "Configured memory brain not found. Please update in Account settings.",
                NeedsConfiguration: true);
        }

        return new MemoryBrainResult(true, memoryBrainId, null, false);
    }
}
```

**Register in**: `src/OpenCortex.Orchestration/ServiceCollectionExtensions.cs`
```csharp
services.AddSingleton<IMemoryBrainResolver, MemoryBrainResolver>();
```

---

##### T-001-03: Add memory_brain_id to user settings

**Option A**: If `IUserSettingsStore` exists, add `MemoryBrainId` field

**Option B**: If not, add column to `users` table:

**File**: `src/OpenCortex.Persistence.Postgres/Migrations/0009_user_memory_brain.sql`

```sql
-- Add memory brain preference to users
ALTER TABLE opencortex.users
ADD COLUMN memory_brain_id text REFERENCES opencortex.brains(brain_id);

COMMENT ON COLUMN opencortex.users.memory_brain_id IS
'User''s preferred brain for storing agent memories. NULL = auto (use only brain or prompt to configure)';
```

---

## Feature 2: Memory Tools

**Feature ID**: `FEAT-001-02`

Thin wrapper tools that save/query/delete documents in the `memories/` path.

### Project Setup

**Create new project**: `src/OpenCortex.Tools.Memory/`

```
OpenCortex.Tools.Memory/
├── OpenCortex.Tools.Memory.csproj
├── ServiceCollectionExtensions.cs
├── MemoryToolDefinitions.cs
└── Handlers/
    ├── SaveMemoryHandler.cs
    ├── RecallMemoriesHandler.cs
    └── ForgetMemoryHandler.cs
```

**Project file**: `src/OpenCortex.Tools.Memory/OpenCortex.Tools.Memory.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpenCortex.Tools\OpenCortex.Tools.csproj" />
    <ProjectReference Include="..\OpenCortex.Core\OpenCortex.Core.csproj" />
    <ProjectReference Include="..\OpenCortex.Orchestration\OpenCortex.Orchestration.csproj" />
  </ItemGroup>
</Project>
```

---

### User Stories

#### US-002: Save Memory Tool
**As an** agent
**I want to** save a memory
**So that** I can recall it in future sessions

**Tasks**:

| Task ID | Description | Effort |
|---------|-------------|--------|
| T-002-01 | Create MemoryToolDefinitions with save_memory | 30m |
| T-002-02 | Implement SaveMemoryHandler | 1.5h |
| T-002-03 | Write handler tests | 1h |

---

##### T-002-01: Create MemoryToolDefinitions

**File**: `src/OpenCortex.Tools.Memory/MemoryToolDefinitions.cs`

```csharp
using System.Text.Json;
using OpenCortex.Tools;

namespace OpenCortex.Tools.Memory;

public sealed class MemoryToolDefinitions : IToolDefinitionProvider
{
    public IReadOnlyList<ToolDefinition> GetToolDefinitions() =>
    [
        SaveMemory,
        RecallMemories,
        ForgetMemory
    ];

    public static ToolDefinition SaveMemory => ToolDefinition.FromFunction(
        name: "save_memory",
        description: """
            Save an important fact, decision, preference, or learning for future recall.
            Use this to remember things across conversations.
            Examples: user preferences, project decisions, learned facts about the codebase.
            """,
        parameters: JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "content": {
                        "type": "string",
                        "description": "The memory content. Be specific and include context."
                    },
                    "category": {
                        "type": "string",
                        "enum": ["fact", "decision", "preference", "learning"],
                        "description": "Category of memory"
                    },
                    "confidence": {
                        "type": "string",
                        "enum": ["high", "medium", "low"],
                        "default": "medium",
                        "description": "How confident you are in this memory"
                    },
                    "tags": {
                        "type": "array",
                        "items": { "type": "string" },
                        "description": "Optional tags for categorization"
                    }
                },
                "required": ["content", "category"]
            }
            """)
    );

    public static ToolDefinition RecallMemories => ToolDefinition.FromFunction(
        name: "recall_memories",
        description: "Search your memories for relevant information from past conversations.",
        parameters: JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "query": {
                        "type": "string",
                        "description": "What you're trying to remember"
                    },
                    "category": {
                        "type": "string",
                        "enum": ["fact", "decision", "preference", "learning"],
                        "description": "Optional: filter by category"
                    },
                    "limit": {
                        "type": "integer",
                        "default": 5,
                        "description": "Maximum memories to return"
                    }
                },
                "required": ["query"]
            }
            """)
    );

    public static ToolDefinition ForgetMemory => ToolDefinition.FromFunction(
        name: "forget_memory",
        description: "Remove a memory that is no longer accurate or relevant.",
        parameters: JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "memory_path": {
                        "type": "string",
                        "description": "The canonical path of the memory to forget (e.g., memories/facts/abc123.md)"
                    },
                    "reason": {
                        "type": "string",
                        "description": "Why this memory should be forgotten"
                    }
                },
                "required": ["memory_path"]
            }
            """)
    );
}
```

---

##### T-002-02: Implement SaveMemoryHandler

**File**: `src/OpenCortex.Tools.Memory/Handlers/SaveMemoryHandler.cs`

**Inject**:
- `IManagedDocumentStore` (from `OpenCortex.Core.Persistence`)
- `IMemoryBrainResolver` (from `OpenCortex.Orchestration.Memory`)

```csharp
using System.Text.Json;
using OpenCortex.Core.Persistence;
using OpenCortex.Orchestration.Memory;
using OpenCortex.Tools;

namespace OpenCortex.Tools.Memory.Handlers;

public sealed class SaveMemoryHandler : IToolHandler
{
    private readonly IManagedDocumentStore _documentStore;
    private readonly IMemoryBrainResolver _brainResolver;

    public SaveMemoryHandler(
        IManagedDocumentStore documentStore,
        IMemoryBrainResolver brainResolver)
    {
        _documentStore = documentStore;
        _brainResolver = brainResolver;
    }

    public string ToolName => "save_memory";
    public string Category => "memory";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // Extract arguments
        var content = arguments.GetProperty("content").GetString()
            ?? throw new ArgumentException("content is required");

        var category = arguments.GetProperty("category").GetString()
            ?? throw new ArgumentException("category is required");

        var confidence = arguments.TryGetProperty("confidence", out var confEl)
            ? confEl.GetString() ?? "medium"
            : "medium";

        var tags = arguments.TryGetProperty("tags", out var tagsEl)
            ? tagsEl.EnumerateArray().Select(t => t.GetString()!).ToList()
            : new List<string>();

        // Resolve memory brain
        var brainResult = await _brainResolver.ResolveAsync(
            context.CustomerId, context.UserId, cancellationToken);

        if (!brainResult.Success)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = brainResult.Error,
                needs_configuration = brainResult.NeedsConfiguration
            });
        }

        // Generate memory path
        var memoryId = Ulid.NewUlid().ToString();
        var slug = $"{memoryId[..8]}";
        var canonicalPath = $"memories/{category}/{slug}.md";
        var title = content.Length > 60 ? content[..60] + "..." : content;

        // Build frontmatter
        var frontmatter = new Dictionary<string, string?>
        {
            ["category"] = category,
            ["confidence"] = confidence,
            ["tags"] = string.Join(",", tags),
            ["source_conversation"] = context.ConversationId
        };

        // Save via existing document store
        await _documentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest
            {
                CustomerId = context.CustomerId,
                BrainId = brainResult.BrainId!,
                Title = $"[{category}] {title}",
                Slug = slug,
                CanonicalPath = canonicalPath,
                Content = content,
                Frontmatter = frontmatter,
                Status = "published",
                CreatedBy = context.UserId
            },
            cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            memory_path = canonicalPath,
            category,
            message = "Memory saved"
        });
    }
}
```

---

#### US-003: Recall Memories Tool

**Tasks**:

| Task ID | Description | Effort |
|---------|-------------|--------|
| T-003-01 | Implement RecallMemoriesHandler | 1h |
| T-003-02 | Write handler tests | 1h |

---

##### T-003-01: Implement RecallMemoriesHandler

**File**: `src/OpenCortex.Tools.Memory/Handlers/RecallMemoriesHandler.cs`

**Inject**:
- `OqlQueryExecutor` (from `OpenCortex.Retrieval.Execution`)
- `IMemoryBrainResolver` (from `OpenCortex.Orchestration.Memory`)

```csharp
using System.Text.Json;
using OpenCortex.Orchestration.Memory;
using OpenCortex.Retrieval.Execution;
using OpenCortex.Tools;

namespace OpenCortex.Tools.Memory.Handlers;

public sealed class RecallMemoriesHandler : IToolHandler
{
    private readonly OqlQueryExecutor _queryExecutor;
    private readonly IMemoryBrainResolver _brainResolver;

    public RecallMemoriesHandler(
        OqlQueryExecutor queryExecutor,
        IMemoryBrainResolver brainResolver)
    {
        _queryExecutor = queryExecutor;
        _brainResolver = brainResolver;
    }

    public string ToolName => "recall_memories";
    public string Category => "memory";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var query = arguments.GetProperty("query").GetString()
            ?? throw new ArgumentException("query is required");

        var limit = arguments.TryGetProperty("limit", out var limitEl)
            ? limitEl.GetInt32()
            : 5;

        var categoryFilter = arguments.TryGetProperty("category", out var catEl)
            ? catEl.GetString()
            : null;

        // Resolve memory brain
        var brainResult = await _brainResolver.ResolveAsync(
            context.CustomerId, context.UserId, cancellationToken);

        if (!brainResult.Success)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = brainResult.Error,
                memories = Array.Empty<object>()
            });
        }

        // Build OQL query - search only in memories/ path
        var escapedQuery = query.Replace("\"", "\\\"");
        var oql = $"""
            FROM brain("{brainResult.BrainId}")
            WHERE path LIKE "memories/%"
            SEARCH "{escapedQuery}"
            RANK semantic
            LIMIT {limit}
            """;

        var results = await _queryExecutor.ExecuteAsync(oql, cancellationToken);

        // Filter by category if specified
        var memories = results.Documents
            .Where(d => categoryFilter == null ||
                        d.Frontmatter?.GetValueOrDefault("category") == categoryFilter)
            .Select(d => new
            {
                path = d.CanonicalPath,
                content = d.Snippet ?? d.Content?[..Math.Min(200, d.Content.Length)],
                category = d.Frontmatter?.GetValueOrDefault("category"),
                confidence = d.Frontmatter?.GetValueOrDefault("confidence"),
                score = d.Score
            })
            .ToList();

        return JsonSerializer.Serialize(new
        {
            success = true,
            query,
            count = memories.Count,
            memories
        });
    }
}
```

---

#### US-004: Forget Memory Tool

**Tasks**:

| Task ID | Description | Effort |
|---------|-------------|--------|
| T-004-01 | Implement ForgetMemoryHandler | 45m |
| T-004-02 | Write handler tests | 30m |

---

##### T-004-01: Implement ForgetMemoryHandler

**File**: `src/OpenCortex.Tools.Memory/Handlers/ForgetMemoryHandler.cs`

**Inject**:
- `IManagedDocumentStore` (from `OpenCortex.Core.Persistence`)
- `IMemoryBrainResolver` (from `OpenCortex.Orchestration.Memory`)

```csharp
using System.Text.Json;
using OpenCortex.Core.Persistence;
using OpenCortex.Orchestration.Memory;
using OpenCortex.Tools;

namespace OpenCortex.Tools.Memory.Handlers;

public sealed class ForgetMemoryHandler : IToolHandler
{
    private readonly IManagedDocumentStore _documentStore;
    private readonly IMemoryBrainResolver _brainResolver;

    public ForgetMemoryHandler(
        IManagedDocumentStore documentStore,
        IMemoryBrainResolver brainResolver)
    {
        _documentStore = documentStore;
        _brainResolver = brainResolver;
    }

    public string ToolName => "forget_memory";
    public string Category => "memory";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var memoryPath = arguments.GetProperty("memory_path").GetString()
            ?? throw new ArgumentException("memory_path is required");

        // Validate it's a memory path
        if (!memoryPath.StartsWith("memories/"))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Can only forget memories (path must start with 'memories/')"
            });
        }

        // Resolve memory brain
        var brainResult = await _brainResolver.ResolveAsync(
            context.CustomerId, context.UserId, cancellationToken);

        if (!brainResult.Success)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = brainResult.Error
            });
        }

        // Get document by path to get the ID
        var doc = await _documentStore.GetManagedDocumentByCanonicalPathAsync(
            context.CustomerId, brainResult.BrainId!, memoryPath, cancellationToken);

        if (doc is null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Memory not found: {memoryPath}"
            });
        }

        // Soft delete
        await _documentStore.SoftDeleteManagedDocumentAsync(
            context.CustomerId,
            brainResult.BrainId!,
            doc.ManagedDocumentId,
            context.UserId,
            cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            forgotten = memoryPath,
            message = "Memory forgotten"
        });
    }
}
```

---

### Tool Registration

**File**: `src/OpenCortex.Tools.Memory/ServiceCollectionExtensions.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using OpenCortex.Tools;
using OpenCortex.Tools.Memory.Handlers;

namespace OpenCortex.Tools.Memory;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMemoryTools(this IServiceCollection services)
    {
        // Register tool definitions provider
        services.AddSingleton<IToolDefinitionProvider, MemoryToolDefinitions>();

        // Register tool handlers
        services.AddSingleton<IToolHandler, SaveMemoryHandler>();
        services.AddSingleton<IToolHandler, RecallMemoriesHandler>();
        services.AddSingleton<IToolHandler, ForgetMemoryHandler>();

        return services;
    }
}
```

**Register in API**: `src/OpenCortex.Api/Program.cs`

Add after other tool registrations (~line 788):
```csharp
using OpenCortex.Tools.Memory;
// ...
builder.Services.AddMemoryTools();
```

---

## Feature 3: Portal Memories Page

**Feature ID**: `FEAT-001-03`

UI for viewing and deleting memories.

### User Stories

#### US-005: Memories Page

**Tasks**:

| Task ID | Description | Effort |
|---------|-------------|--------|
| T-005-01 | Add 'memories' to PortalView type | 15m |
| T-005-02 | Add viewDefinition for memories | 15m |
| T-005-03 | Create MemoriesView component | 2h |
| T-005-04 | Add delete functionality | 1h |

---

##### T-005-01 & T-005-02: Update App.tsx

**File**: `src/OpenCortex.Portal/Frontend/src/App.tsx`

**1. Update PortalView type** (around line 7):
```typescript
type PortalView = 'signin' | 'documents' | 'chat' | 'account' | 'usage' | 'tools' | 'memories';
```

**2. Add viewDefinition** (in viewDefinitions object):
```typescript
memories: {
  id: 'memories',
  title: 'Memories',
  lead: 'View and manage what your agent remembers across sessions.',
  bullets: [
    'Memories persist across conversations.',
    'Search and filter by category.',
    'Delete memories you no longer need.'
  ]
},
```

**3. Update orderedViews array**:
```typescript
const orderedViews: PortalView[] = ['signin', 'documents', 'chat', 'account', 'usage', 'tools', 'memories'];
```

---

##### T-005-03: Create MemoriesView component

**File**: `src/OpenCortex.Portal/Frontend/src/components/MemoriesView.tsx`

```typescript
import { useState, useEffect } from 'react';

type Memory = {
  path: string;
  title: string;
  content: string;
  category: string;
  confidence: string;
  createdAt: string;
};

type MemoriesViewProps = {
  activeBrainId: string;
  authSession: StoredAuthSession;
  onRefreshSession: () => Promise<string | null>;
};

export function MemoriesView({ activeBrainId, authSession, onRefreshSession }: MemoriesViewProps) {
  const [memories, setMemories] = useState<Memory[]>([]);
  const [loading, setLoading] = useState(true);
  const [categoryFilter, setCategoryFilter] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');

  useEffect(() => {
    loadMemories();
  }, [activeBrainId, categoryFilter]);

  const loadMemories = async () => {
    setLoading(true);
    try {
      // Query documents with path starting with "memories/"
      const response = await fetch(`/api/brains/${activeBrainId}/documents?pathPrefix=memories/`, {
        headers: { Authorization: `Bearer ${authSession.accessToken}` }
      });
      const data = await response.json();
      setMemories(data.documents ?? []);
    } catch (error) {
      console.error('Failed to load memories:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleDelete = async (memoryPath: string) => {
    if (!confirm('Forget this memory?')) return;

    try {
      await fetch(`/api/brains/${activeBrainId}/documents/${encodeURIComponent(memoryPath)}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${authSession.accessToken}` }
      });
      setMemories(memories.filter(m => m.path !== memoryPath));
    } catch (error) {
      console.error('Failed to delete memory:', error);
    }
  };

  const filteredMemories = memories.filter(m => {
    if (categoryFilter && m.category !== categoryFilter) return false;
    if (searchQuery && !m.content.toLowerCase().includes(searchQuery.toLowerCase())) return false;
    return true;
  });

  const categories = ['fact', 'decision', 'preference', 'learning'];

  return (
    <section className="portal-layout">
      <article className="panel portal-hero">
        <p className="eyebrow">Memories</p>
        <h2>What your agent remembers</h2>
        <p className="summary-detail">
          {memories.length} memories stored
        </p>
      </article>

      <div className="memories-controls">
        <input
          type="text"
          placeholder="Search memories..."
          value={searchQuery}
          onChange={e => setSearchQuery(e.target.value)}
          className="search-input"
        />
        <select
          value={categoryFilter ?? ''}
          onChange={e => setCategoryFilter(e.target.value || null)}
          className="category-filter"
        >
          <option value="">All categories</option>
          {categories.map(cat => (
            <option key={cat} value={cat}>{cat}</option>
          ))}
        </select>
      </div>

      {loading ? (
        <div className="loading">Loading memories...</div>
      ) : filteredMemories.length === 0 ? (
        <div className="empty-state">
          <span className="icon">🧠</span>
          <h3>No memories yet</h3>
          <p>Your agent will save memories as it learns about you and your work.</p>
        </div>
      ) : (
        <div className="memories-list">
          {filteredMemories.map(memory => (
            <div key={memory.path} className="memory-card">
              <div className="memory-header">
                <span className={`category-badge ${memory.category}`}>
                  {memory.category}
                </span>
                <span className="confidence">{memory.confidence}</span>
              </div>
              <div className="memory-content">{memory.content}</div>
              <div className="memory-footer">
                <span className="date">{new Date(memory.createdAt).toLocaleDateString()}</span>
                <button
                  onClick={() => handleDelete(memory.path)}
                  className="forget-button"
                >
                  Forget
                </button>
              </div>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}
```

**4. Add to main render in App.tsx** (in the view switching section):
```typescript
) : activeView === 'memories' ? (
  <MemoriesView
    activeBrainId={activeBrainId}
    authSession={authSession}
    onRefreshSession={handleRefreshSession}
  />
```

---

## Feature 4: Filter Memories from Documents Page

**Feature ID**: `FEAT-001-04`

Hide memories from the Documents page.

### User Stories

#### US-006: Hide Memories from Documents

**Tasks**:

| Task ID | Description | Effort |
|---------|-------------|--------|
| T-006-01 | Update documents query to exclude memories/* | 30m |

---

##### T-006-01: Update Documents query

**File**: `src/OpenCortex.Portal/Frontend/src/components/DocumentsView.tsx`

Find the documents fetch and add exclusion:

```typescript
// Before
const response = await fetch(`/api/brains/${activeBrainId}/documents`);

// After - exclude memories
const response = await fetch(`/api/brains/${activeBrainId}/documents?excludePathPrefix=memories/`);
```

**Backend**: If the endpoint doesn't support `excludePathPrefix`, add it to the managed documents endpoint.

---

## Feature 5: Account Memory Brain Selector

**Feature ID**: `FEAT-001-05`

Let users with multiple brains choose their memory brain.

### User Stories

#### US-007: Memory Brain Configuration

**Tasks**:

| Task ID | Description | Effort |
|---------|-------------|--------|
| T-007-01 | Add memory brain selector to Account view | 1h |
| T-007-02 | Create API endpoint to save preference | 30m |

---

##### T-007-01: Add to Account view

**File**: `src/OpenCortex.Portal/Frontend/src/App.tsx` (in Account section)

```typescript
// In account view, add:
{brains.length > 1 && (
  <div className="setting-row">
    <label>Memory Brain</label>
    <p className="setting-description">
      Choose which brain stores your agent's memories.
    </p>
    <select
      value={memoryBrainId ?? ''}
      onChange={e => updateMemoryBrain(e.target.value)}
    >
      <option value="">Select a brain...</option>
      {brains.map(brain => (
        <option key={brain.brainId} value={brain.brainId}>
          {brain.name}
        </option>
      ))}
    </select>
  </div>
)}
```

---

# Implementation Timeline

## Week 1
- [ ] T-001-01 to T-001-04: Memory brain resolution
- [ ] T-002-01 to T-002-03: Save memory tool
- [ ] T-003-01 to T-003-02: Recall memories tool
- [ ] T-004-01 to T-004-02: Forget memory tool

## Week 2
- [ ] T-005-01 to T-005-04: Memories Portal page
- [ ] T-006-01: Filter memories from documents
- [ ] T-007-01 to T-007-02: Account memory brain selector

---

# Total Effort Summary

| Category | Tasks | Hours |
|----------|-------|-------|
| Interfaces | 1 | 0.5h |
| Services | 2 | 1.5h |
| Tool Handlers | 3 | 3h |
| Tool Registration | 1 | 0.5h |
| Frontend | 4 | 4h |
| Database | 1 | 0.5h |
| Testing | 5 | 4.5h |
| **Total** | **17** | **~15h (1-2 weeks)** |

---

# Files to Create/Modify Summary

## New Files
```
src/OpenCortex.Orchestration/Memory/
├── IMemoryBrainResolver.cs
└── MemoryBrainResolver.cs

src/OpenCortex.Tools.Memory/
├── OpenCortex.Tools.Memory.csproj
├── ServiceCollectionExtensions.cs
├── MemoryToolDefinitions.cs
└── Handlers/
    ├── SaveMemoryHandler.cs
    ├── RecallMemoriesHandler.cs
    └── ForgetMemoryHandler.cs

src/OpenCortex.Portal/Frontend/src/components/
└── MemoriesView.tsx

src/OpenCortex.Persistence.Postgres/Migrations/
└── 0009_user_memory_brain.sql
```

## Files to Modify
```
src/OpenCortex.Api/Program.cs                    → Add builder.Services.AddMemoryTools()
src/OpenCortex.Portal/Frontend/src/App.tsx       → Add memories view
src/OpenCortex.Portal/Frontend/src/components/DocumentsView.tsx → Filter out memories/*
```
