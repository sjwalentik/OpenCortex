# Priority 3: Agent Delegation

## Epic Overview

**Epic**: Multi-Agent Delegation System

Enable lead agents to spawn and coordinate with specialist sub-agents, creating a hierarchical multi-agent execution model.

---

## Implementation Overview

This priority adds agent profiles, delegation tools, and a sub-agent orchestration engine.

Migration numbering note: `0010_tenant_scoped_user_provider_configs.sql` now occupies the old placeholder slot. Planned delegation work in this document should therefore use `0013_agent_profiles.sql`.

### Files to Create

```
src/OpenCortex.Domain/Agents/
â”œâ”€â”€ AgentProfile.cs                       # Domain entity
â”œâ”€â”€ AgentSpecialization.cs                # Enum or string constants
â”œâ”€â”€ IAgentProfileRepository.cs            # Repository interface
â””â”€â”€ DefaultAgents.cs                      # System agent definitions

src/OpenCortex.Persistence.Postgres/
â”œâ”€â”€ Migrations/
â”‚   â””â”€â”€ 0013_agent_profiles.sql           # Agent profiles table
â”œâ”€â”€ Repositories/
â”‚   â””â”€â”€ PostgresAgentProfileRepository.cs # IAgentProfileRepository implementation
â””â”€â”€ ServiceCollectionExtensions.cs        # ADD: Repository registration

src/OpenCortex.Tools.Delegation/
â”œâ”€â”€ OpenCortex.Tools.Delegation.csproj    # New project
â”œâ”€â”€ ServiceCollectionExtensions.cs        # DI registration
â”œâ”€â”€ DelegationToolDefinitions.cs          # Tool JSON schemas
â”œâ”€â”€ DelegationToolDefinitionProvider.cs   # IToolDefinitionProvider implementation
â””â”€â”€ Handlers/
    â”œâ”€â”€ DelegateToAgentHandler.cs
    â””â”€â”€ ListAvailableAgentsHandler.cs

src/OpenCortex.Orchestration/Delegation/
â”œâ”€â”€ ISubAgentOrchestrator.cs              # Interface
â”œâ”€â”€ SubAgentOrchestrator.cs               # Implementation
â”œâ”€â”€ SubAgentRequest.cs                    # Request model
â”œâ”€â”€ SubAgentResult.cs                     # Result model
â””â”€â”€ IDelegationQuotaChecker.cs            # Quota checking interface

src/OpenCortex.Orchestration/Startup/
â””â”€â”€ DefaultAgentSeeder.cs                 # Seeds system agents on startup

src/OpenCortex.Api/
â”œâ”€â”€ AgentEndpoints.cs                     # NEW: REST API for agents
â””â”€â”€ Program.cs                            # ADD: .MapAgentEndpoints()

src/OpenCortex.Portal/Frontend/src/
â”œâ”€â”€ pages/Agents/
â”‚   â”œâ”€â”€ AgentListView.tsx                 # Agent list page
â”‚   â””â”€â”€ AgentEditorPage.tsx               # Create/edit agent page
â”œâ”€â”€ components/Agents/
â”‚   â”œâ”€â”€ AgentCard.tsx                     # Agent card for grid display
â”‚   â”œâ”€â”€ AgentIdentityTab.tsx              # Identity configuration
â”‚   â”œâ”€â”€ AgentSoulTab.tsx                  # System prompt editor
â”‚   â”œâ”€â”€ AgentToolsTab.tsx                 # Tool configuration
â”‚   â”œâ”€â”€ AgentProvidersTab.tsx             # Provider access config
â”‚   â””â”€â”€ AgentLimitsTab.tsx                # Limits configuration
â”œâ”€â”€ hooks/
â”‚   â”œâ”€â”€ useAgents.ts                      # Query hook for agents
â”‚   â”œâ”€â”€ useAgent.ts                       # Single agent query
â”‚   â”œâ”€â”€ useCreateAgent.ts                 # Create mutation
â”‚   â”œâ”€â”€ useUpdateAgent.ts                 # Update mutation
â”‚   â””â”€â”€ useToolCatalog.ts                 # Available tools list
â””â”€â”€ App.tsx                               # ADD: Agents to viewDefinitions
```

### Existing Services to Use

| Service | Location | Usage |
|---------|----------|-------|
| `NpgsqlConnection` | DI from `AddPostgresStores()` | Database access |
| `IAgenticOrchestrationEngine` | `OpenCortex.Orchestration` | Execute sub-agents |
| `IToolExecutor` | `OpenCortex.Tools` | Get available tools |
| `IToolHandler` | `OpenCortex.Tools` | Tool handler interface |
| `IToolDefinitionProvider` | `OpenCortex.Tools` | Tool definition provider |
| `IWorkItemRepository` | From P2 | Create tasks for delegations |

### DI Registration Pattern

```csharp
// src/OpenCortex.Persistence.Postgres/ServiceCollectionExtensions.cs
// ADD to existing AddPostgresStores() method:
services.AddScoped<IAgentProfileRepository, PostgresAgentProfileRepository>();
```

```csharp
// src/OpenCortex.Tools.Delegation/ServiceCollectionExtensions.cs (NEW FILE)
namespace OpenCortex.Tools.Delegation;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDelegationTools(this IServiceCollection services)
    {
        // Tool handlers
        services.AddScoped<IToolHandler, DelegateToAgentHandler>();
        services.AddScoped<IToolHandler, ListAvailableAgentsHandler>();

        // Tool definitions provider
        services.AddSingleton<IToolDefinitionProvider, DelegationToolDefinitionProvider>();

        // Sub-agent orchestration
        services.AddScoped<ISubAgentOrchestrator, SubAgentOrchestrator>();
        services.AddScoped<IDelegationQuotaChecker, DelegationQuotaChecker>();

        return services;
    }
}
```

```csharp
// src/OpenCortex.Api/Program.cs
// ADD after AddWorkItemTools():
builder.Services.AddDelegationTools();

// In startup, seed default agents:
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DefaultAgentSeeder>();
    await seeder.SeedAsync();
}

// ADD after MapWorkItemEndpoints():
app.MapAgentEndpoints();
```

### Portal Routing

```tsx
// src/OpenCortex.Portal/Frontend/src/App.tsx
// ADD to viewDefinitions:

const viewDefinitions: Record<PortalView, ViewDefinition> = {
  // ... existing views ...

  "agents": {
    component: () => <AgentListView />,
    label: "Agents",
    icon: UsersIcon,
  },
  "agents-new": {
    component: () => <AgentEditorPage />,
    label: "New Agent",
    parent: "agents",
  },
  "agents-edit": {
    component: () => <AgentEditorPage />,
    label: "Edit Agent",
    parent: "agents",
  },
};
```

---

## Epic Details

| Field | Value |
|-------|-------|
| Epic ID | `EPIC-003` |
| Priority | P3 |
| Dependencies | P1 (Memory), P2 (Tasks) |
| Estimated Effort | 8-9 weeks |
| Business Value | Very High - Core multi-agent capability |

### Problem Statement

Single-agent execution limits capability:
- One agent cannot be expert in everything
- Complex tasks benefit from specialization
- No parallelization of independent subtasks
- No separation of concerns in problem-solving

### Success Criteria

- Lead agent can delegate tasks to specialist agents
- Sub-agents execute autonomously and return results
- Agent profiles define capabilities and prompts
- Delegation appears in conversation and task history
- Resource limits prevent runaway delegation

---

# Features

## Feature 1: Agent Registry & Profiles

**Feature ID**: `FEAT-003-01`

Define and manage agent profiles with specializations.

### User Stories

#### US-016: Agent Profile Storage
**As a** system administrator
**I want to** store agent profile definitions
**So that** agents can be discovered and invoked

**Acceptance Criteria**:
- Agent profiles persisted in database
- Profiles include: id, name, description, system prompt, tools, model
- Support for customer-specific custom agents
- Seed default agents on first run

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-016-01 | Create `agent_profiles` table migration | 2h | Database |
| T-016-02 | Create `AgentProfile` domain entity | 1h | Domain |
| T-016-03 | Create `IAgentProfileRepository` interface | 1h | Abstractions |
| T-016-04 | Implement `PostgresAgentProfileRepository` | 3h | Persistence |
| T-016-05 | Add repository to DI registration | 30m | Infrastructure |
| T-016-06 | Write repository tests | 2h | Testing |

**Slice Details**:

##### T-016-01: Database migration
```sql
-- Migration: 0013_agent_profiles.sql
CREATE TABLE IF NOT EXISTS opencortex.agent_profiles (
    agent_profile_id text PRIMARY KEY,
    customer_id text REFERENCES opencortex.customers(customer_id),  -- NULL = system agent

    name text NOT NULL,
    description text NOT NULL,
    specialization text NOT NULL,

    system_prompt text NOT NULL,
    enabled_tools text[] NOT NULL DEFAULT '{}',
    disabled_tools text[] NOT NULL DEFAULT '{}',

    model_preference text,  -- NULL = use default
    max_iterations integer NOT NULL DEFAULT 15,

    is_active boolean NOT NULL DEFAULT true,
    is_system boolean NOT NULL DEFAULT false,  -- System agents can't be deleted

    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,

    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT unique_agent_name_per_customer UNIQUE (customer_id, name)
);

CREATE INDEX ix_agent_profiles_customer ON opencortex.agent_profiles(customer_id) WHERE customer_id IS NOT NULL;
CREATE INDEX ix_agent_profiles_specialization ON opencortex.agent_profiles(specialization);
CREATE INDEX ix_agent_profiles_active ON opencortex.agent_profiles(is_active) WHERE is_active = true;
```

##### T-016-02: Domain entity
```csharp
// src/OpenCortex.Domain/Agents/AgentProfile.cs
public sealed class AgentProfile
{
    public string AgentProfileId { get; init; } = Ulid.NewUlid().ToString();
    public string? CustomerId { get; init; }  // Null = system agent

    public string Name { get; set; }
    public string Description { get; set; }
    public string Specialization { get; set; }

    public string SystemPrompt { get; set; }
    public IReadOnlyList<string> EnabledTools { get; set; } = [];
    public IReadOnlyList<string> DisabledTools { get; set; } = [];

    public string? ModelPreference { get; set; }
    public int MaxIterations { get; set; } = 15;

    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }

    public Dictionary<string, object> Metadata { get; set; } = new();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

##### T-016-03: Repository interface
```csharp
// src/OpenCortex.Domain/Agents/IAgentProfileRepository.cs
public interface IAgentProfileRepository
{
    Task<AgentProfile?> GetByIdAsync(string agentProfileId, CancellationToken ct);
    Task<AgentProfile?> GetByNameAsync(string name, string? customerId, CancellationToken ct);
    Task<IReadOnlyList<AgentProfile>> GetAvailableAgentsAsync(string? customerId, CancellationToken ct);
    Task<IReadOnlyList<AgentProfile>> GetBySpecializationAsync(string specialization, string? customerId, CancellationToken ct);
    Task CreateAsync(AgentProfile profile, CancellationToken ct);
    Task UpdateAsync(AgentProfile profile, CancellationToken ct);
    Task DeleteAsync(string agentProfileId, CancellationToken ct);
}
```

---

#### US-017: Seed Default Agents
**As a** system
**I want to** have pre-configured specialist agents available
**So that** users can delegate tasks immediately

**Acceptance Criteria**:
- Default agents created on first startup
- Agents cover common specializations
- Agents have optimized system prompts
- Tool sets appropriate for specialization

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-017-01 | Create `DefaultAgentSeeder` service | 2h | Services |
| T-017-02 | Define code-reviewer agent | 1h | Configuration |
| T-017-03 | Define researcher agent | 1h | Configuration |
| T-017-04 | Define planner agent | 1h | Configuration |
| T-017-05 | Define writer agent | 1h | Configuration |
| T-017-06 | Define debugger agent | 1h | Configuration |
| T-017-07 | Integrate seeder into startup | 30m | Infrastructure |
| T-017-08 | Write seeder tests | 1h | Testing |

**Slice Details**:

##### T-017-02: Code reviewer agent definition
```csharp
new AgentProfile
{
    AgentProfileId = "code-reviewer",
    Name = "Code Reviewer",
    Description = "Expert at reviewing code for quality, security, and best practices",
    Specialization = "code-review",
    SystemPrompt = """
        You are a senior code reviewer with expertise in multiple programming languages.

        Your responsibilities:
        - Identify bugs, security vulnerabilities, and anti-patterns
        - Evaluate code quality, readability, and maintainability
        - Check for proper error handling and edge cases
        - Suggest specific improvements with code examples
        - Be constructive and educational in your feedback

        When reviewing:
        1. First read the code to understand its purpose
        2. Check for critical issues (security, correctness)
        3. Review code structure and patterns
        4. Suggest improvements with priorities

        Always provide actionable feedback with specific line references.
        """,
    EnabledTools = ["read_file", "list_directory", "grep_search", "recall_memories", "save_memory"],
    MaxIterations = 15,
    IsSystem = true,
    IsActive = true
}
```

##### T-017-03: Researcher agent definition
```csharp
new AgentProfile
{
    AgentProfileId = "researcher",
    Name = "Researcher",
    Description = "Gathers information from documentation, web, and codebase",
    Specialization = "research",
    SystemPrompt = """
        You are a research specialist who excels at finding and synthesizing information.

        Your responsibilities:
        - Search documentation and codebases for relevant information
        - Find best practices and examples from trusted sources
        - Synthesize findings into clear, actionable summaries
        - Cite sources and provide links when available

        Research methodology:
        1. Understand the research question clearly
        2. Search multiple sources (docs, code, web)
        3. Cross-reference and verify information
        4. Synthesize into a clear summary with citations

        Always distinguish between facts and opinions/recommendations.
        """,
    EnabledTools = ["read_file", "list_directory", "grep_search", "web_search", "web_fetch", "recall_memories", "save_memory"],
    MaxIterations = 20,
    IsSystem = true,
    IsActive = true
}
```

##### T-017-04: Planner agent definition
```csharp
new AgentProfile
{
    AgentProfileId = "planner",
    Name = "Planner",
    Description = "Decomposes complex goals into structured task plans",
    Specialization = "planning",
    SystemPrompt = """
        You are a project planning specialist who excels at breaking down complex goals.

        Your responsibilities:
        - Analyze complex goals and identify component tasks
        - Create structured, prioritized task breakdowns
        - Identify dependencies between tasks
        - Estimate effort and flag risks

        Planning methodology:
        1. Understand the end goal and constraints
        2. Identify major phases or milestones
        3. Break each phase into specific tasks
        4. Order by dependency and priority
        5. Create tasks using the task management tools

        Plans should be actionable and specific, not vague.
        """,
    EnabledTools = ["read_file", "list_directory", "create_task", "update_task", "list_tasks", "recall_memories", "save_memory"],
    MaxIterations = 10,
    IsSystem = true,
    IsActive = true
}
```

---

#### US-018: Agent Profile Management API
**As a** user
**I want to** list and view available agents
**So that** I know what specialists I can delegate to

**Acceptance Criteria**:
- GET /api/agents returns available agents
- Includes system agents and customer custom agents
- Response includes capabilities summary
- Supports filtering by specialization

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-018-01 | Create `AgentEndpoints.cs` | 1h | API |
| T-018-02 | Implement list agents endpoint | 2h | API |
| T-018-03 | Implement get agent detail endpoint | 1h | API |
| T-018-04 | Add rate limiting | 30m | API |
| T-018-05 | Write API tests | 2h | Testing |

**Slice Details**:

##### T-018-01: Agent endpoints
```csharp
// src/OpenCortex.Api/AgentEndpoints.cs
public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this WebApplication app)
    {
        var agents = app.MapGroup("/api/agents")
            .RequireAuthorization()
            .RequireRateLimiting("tenant-api");

        // List available agents (system + customer custom)
        agents.MapGet("/", async (
            HttpContext context,
            IAgentProfileRepository agentRepo,
            string? specialization,
            CancellationToken ct) =>
        {
            var customerId = context.GetCustomerId();

            var profiles = specialization is not null
                ? await agentRepo.GetBySpecializationAsync(specialization, customerId, ct)
                : await agentRepo.GetAvailableAgentsAsync(customerId, ct);

            return Results.Ok(profiles.Select(p => new
            {
                agentId = p.AgentProfileId,
                name = p.Name,
                description = p.Description,
                specialization = p.Specialization,
                toolCount = p.EnabledTools.Count,
                isSystem = p.IsSystem
            }));
        });

        // Get agent detail
        agents.MapGet("/{agentId}", async (
            string agentId,
            IAgentProfileRepository agentRepo,
            CancellationToken ct) =>
        {
            var profile = await agentRepo.GetByIdAsync(agentId, ct);
            if (profile is null)
                return Results.NotFound();

            return Results.Ok(new
            {
                agentId = profile.AgentProfileId,
                name = profile.Name,
                description = profile.Description,
                specialization = profile.Specialization,
                enabledTools = profile.EnabledTools,
                maxIterations = profile.MaxIterations,
                isSystem = profile.IsSystem
            });
        });
    }
}
```

---

#### US-019: Custom Agent Creation
**As a** user
**I want to** create my own specialist agents
**So that** I can customize agent behavior for my needs

**Acceptance Criteria**:
- POST /api/agents creates custom agent
- Validates system prompt and tools
- Respects per-customer agent limits
- Custom agents scoped to customer

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-019-01 | Implement create agent endpoint | 3h | API |
| T-019-02 | Add agent quota check | 1h | Billing |
| T-019-03 | Implement update agent endpoint | 2h | API |
| T-019-04 | Implement delete agent endpoint | 1h | API |
| T-019-05 | Write API tests | 2h | Testing |

---

## Feature 5: Agent Configuration Portal UI

**Feature ID**: `FEAT-003-05`

Comprehensive UI for creating, configuring, and managing custom agents.

### UI Architecture

```
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚                     Portal â†’ Agents Section                       â”‚
â”œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¤
â”‚  â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”  â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”   â”‚
â”‚  â”‚   Agent List     â”‚  â”‚      Agent Configuration Panel      â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚                                      â”‚   â”‚
â”‚  â”‚  [+ New Agent]   â”‚  â”‚  Identity Tab                       â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚  â”œâ”€â”€ Name, Avatar                   â”‚   â”‚
â”‚  â”‚  System Agents   â”‚  â”‚  â”œâ”€â”€ Specialization                 â”‚   â”‚
â”‚  â”‚  â”œâ”€â”€ Researcher  â”‚  â”‚  â””â”€â”€ Description                    â”‚   â”‚
â”‚  â”‚  â”œâ”€â”€ Code Review â”‚  â”‚                                      â”‚   â”‚
â”‚  â”‚  â”œâ”€â”€ Planner     â”‚  â”‚  Soul Tab (System Prompt)           â”‚   â”‚
â”‚  â”‚  â”œâ”€â”€ Writer      â”‚  â”‚  â”œâ”€â”€ Rich text editor               â”‚   â”‚
â”‚  â”‚  â””â”€â”€ Debugger    â”‚  â”‚  â”œâ”€â”€ Template variables             â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚  â””â”€â”€ Personality guidelines         â”‚   â”‚
â”‚  â”‚  My Agents       â”‚  â”‚                                      â”‚   â”‚
â”‚  â”‚  â”œâ”€â”€ PR Wizard   â”‚  â”‚  Tools Tab                          â”‚   â”‚
â”‚  â”‚  â””â”€â”€ Doc Writer  â”‚  â”‚  â”œâ”€â”€ Available tools list           â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚  â”œâ”€â”€ Enabled/disabled toggles       â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚  â””â”€â”€ Tool configuration             â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚                                      â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚  Providers Tab                       â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚  â”œâ”€â”€ GitHub access                  â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚  â”œâ”€â”€ Other integrations             â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚  â””â”€â”€ Credentials scope              â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚                                      â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚  Limits Tab                          â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚  â”œâ”€â”€ Max iterations                 â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚  â”œâ”€â”€ Timeout                        â”‚   â”‚
â”‚  â”‚                  â”‚  â”‚  â””â”€â”€ Model preference               â”‚   â”‚
â”‚  â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜  â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜   â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

### User Stories

#### US-026: Agent List View
**As a** user
**I want** to see all available agents (system + my custom ones)
**So that** I can manage and choose agents to use

**Acceptance Criteria**:
- List shows system agents and user's custom agents
- Cards show: name, avatar, specialization, tool count
- Filter by specialization
- Search by name
- System agents are read-only (view details only)

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-026-01 | Create `AgentListView` page component | 3h | Frontend |
| T-026-02 | Create `AgentCard` component with avatar and badges | 2h | Frontend |
| T-026-03 | Add search and filter controls | 1h | Frontend |
| T-026-04 | Differentiate system vs custom agents visually | 1h | Frontend |
| T-026-05 | Connect to agents API | 1h | Frontend |

**Slice Details**:

##### T-026-01: AgentListView component
```tsx
// src/OpenCortex.Portal/Frontend/src/pages/Agents/AgentListView.tsx
export function AgentListView() {
  const { data: agents } = useAgents();
  const [filter, setFilter] = useState<string>('');
  const [specialization, setSpecialization] = useState<string | null>(null);

  const systemAgents = agents?.filter(a => a.isSystem) ?? [];
  const customAgents = agents?.filter(a => !a.isSystem) ?? [];

  return (
    <div className="agent-list-view">
      <PageHeader title="Agents">
        <Button onClick={() => navigate('/agents/new')} variant="primary">
          + New Agent
        </Button>
      </PageHeader>

      <FilterBar>
        <SearchInput value={filter} onChange={setFilter} placeholder="Search agents..." />
        <SpecializationDropdown value={specialization} onChange={setSpecialization} />
      </FilterBar>

      <Section title="System Agents" description="Pre-configured specialist agents">
        <AgentGrid>
          {systemAgents.map(agent => (
            <AgentCard key={agent.agentProfileId} agent={agent} readOnly />
          ))}
        </AgentGrid>
      </Section>

      <Section title="My Agents" description="Custom agents you've created">
        {customAgents.length === 0 ? (
          <EmptyState
            icon="ðŸ¤–"
            title="No custom agents yet"
            description="Create your first agent to automate specialized tasks"
            action={<Button onClick={() => navigate('/agents/new')}>Create Agent</Button>}
          />
        ) : (
          <AgentGrid>
            {customAgents.map(agent => (
              <AgentCard key={agent.agentProfileId} agent={agent} />
            ))}
          </AgentGrid>
        )}
      </Section>
    </div>
  );
}
```

---

#### US-027: Agent Identity Configuration
**As a** user
**I want** to define my agent's identity (name, avatar, specialization)
**So that** I can give it a clear purpose and personality

**Acceptance Criteria**:
- Name: unique per customer
- Avatar: upload or choose from presets
- Specialization: select from list or custom
- Description: what this agent is for

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-027-01 | Create `AgentIdentityTab` component | 2h | Frontend |
| T-027-02 | Add avatar upload/selection | 2h | Frontend |
| T-027-03 | Create specialization picker with custom option | 1h | Frontend |
| T-027-04 | Add name uniqueness validation | 1h | Frontend |

**Slice Details**:

##### T-027-01: AgentIdentityTab component
```tsx
// src/OpenCortex.Portal/Frontend/src/components/Agents/AgentIdentityTab.tsx
interface AgentIdentityTabProps {
  agent: AgentProfile;
  onChange: (updates: Partial<AgentProfile>) => void;
  errors?: Record<string, string>;
}

export function AgentIdentityTab({ agent, onChange, errors }: AgentIdentityTabProps) {
  return (
    <div className="agent-identity-tab">
      <FormField label="Agent Name" error={errors?.name} required>
        <Input
          value={agent.name}
          onChange={e => onChange({ name: e.target.value })}
          placeholder="e.g., PR Wizard, Doc Generator"
        />
      </FormField>

      <FormField label="Avatar">
        <AvatarPicker
          value={agent.avatar}
          onChange={avatar => onChange({ avatar })}
          presets={AGENT_AVATAR_PRESETS}
          allowUpload
        />
      </FormField>

      <FormField label="Specialization" required>
        <SpecializationPicker
          value={agent.specialization}
          onChange={specialization => onChange({ specialization })}
          options={SPECIALIZATION_OPTIONS}
          allowCustom
        />
      </FormField>

      <FormField label="Description" required>
        <Textarea
          value={agent.description}
          onChange={e => onChange({ description: e.target.value })}
          placeholder="What does this agent do? When should it be used?"
          rows={3}
        />
      </FormField>
    </div>
  );
}

const SPECIALIZATION_OPTIONS = [
  { value: 'code-review', label: 'Code Review', icon: 'ðŸ”' },
  { value: 'research', label: 'Research', icon: 'ðŸ“š' },
  { value: 'planning', label: 'Planning', icon: 'ðŸ“‹' },
  { value: 'writing', label: 'Writing', icon: 'âœï¸' },
  { value: 'debugging', label: 'Debugging', icon: 'ðŸ›' },
  { value: 'testing', label: 'Testing', icon: 'ðŸ§ª' },
  { value: 'devops', label: 'DevOps', icon: 'ðŸš€' },
  { value: 'security', label: 'Security', icon: 'ðŸ”’' },
  { value: 'custom', label: 'Custom...', icon: 'âš™ï¸' }
];
```

---

#### US-028: Agent Soul (System Prompt) Configuration
**As a** user
**I want** to craft my agent's "soul" - its personality and instructions
**So that** it behaves exactly how I need

**Acceptance Criteria**:
- Rich text editor for system prompt
- Template variables: `{{user_name}}`, `{{workspace_path}}`, etc.
- Personality presets (professional, friendly, concise)
- Character count/token estimate
- Preview rendered prompt

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-028-01 | Create `AgentSoulTab` component | 3h | Frontend |
| T-028-02 | Add rich text/markdown editor | 2h | Frontend |
| T-028-03 | Implement template variable insertion | 2h | Frontend |
| T-028-04 | Add personality preset selector | 1h | Frontend |
| T-028-05 | Add token count estimator | 1h | Frontend |
| T-028-06 | Add prompt preview modal | 1h | Frontend |

**Slice Details**:

##### T-028-01: AgentSoulTab component
```tsx
// src/OpenCortex.Portal/Frontend/src/components/Agents/AgentSoulTab.tsx
export function AgentSoulTab({ agent, onChange }: Props) {
  const [showPreview, setShowPreview] = useState(false);
  const tokenCount = useTokenEstimate(agent.systemPrompt);

  return (
    <div className="agent-soul-tab">
      <div className="soul-header">
        <h3>Agent Soul</h3>
        <p className="subtitle">
          Define your agent's personality, expertise, and instructions.
          This is the system prompt that shapes how your agent thinks and responds.
        </p>
      </div>

      <FormField label="Personality Preset">
        <PersonalityPresetPicker
          value={agent.personalityPreset}
          onChange={preset => applyPreset(preset)}
          options={[
            { value: 'professional', label: 'Professional', description: 'Formal, precise, business-focused' },
            { value: 'friendly', label: 'Friendly', description: 'Warm, helpful, conversational' },
            { value: 'concise', label: 'Concise', description: 'Brief, to-the-point, minimal' },
            { value: 'educational', label: 'Educational', description: 'Explains reasoning, teaches' },
            { value: 'custom', label: 'Custom', description: 'Write your own' }
          ]}
        />
      </FormField>

      <FormField label="System Prompt" required>
        <div className="prompt-editor-container">
          <MarkdownEditor
            value={agent.systemPrompt}
            onChange={prompt => onChange({ systemPrompt: prompt })}
            placeholder="You are a specialist in..."
            minHeight={300}
          />
          <div className="prompt-toolbar">
            <TemplateVariableDropdown onInsert={insertVariable} />
            <Button size="sm" variant="ghost" onClick={() => setShowPreview(true)}>
              Preview
            </Button>
            <span className="token-count">
              ~{tokenCount} tokens
            </span>
          </div>
        </div>
      </FormField>

      <Collapsible title="Template Variables Reference">
        <TemplateVariablesTable variables={AVAILABLE_VARIABLES} />
      </Collapsible>

      {showPreview && (
        <PromptPreviewModal
          template={agent.systemPrompt}
          variables={SAMPLE_VARIABLES}
          onClose={() => setShowPreview(false)}
        />
      )}
    </div>
  );
}

const AVAILABLE_VARIABLES = [
  { name: '{{user_name}}', description: 'Current user\'s display name' },
  { name: '{{workspace_path}}', description: 'Path to the agent\'s workspace' },
  { name: '{{current_date}}', description: 'Today\'s date' },
  { name: '{{task_context}}', description: 'Description of the current task' },
  { name: '{{memory_context}}', description: 'Relevant memories from agent memory' }
];
```

---

#### US-029: Agent Tool Configuration
**As a** user
**I want** to control which tools my agent can use
**So that** it has the right capabilities for its job

**Acceptance Criteria**:
- List all available tools with descriptions
- Toggle enabled/disabled per tool
- Tool categories (files, web, memory, workspace, etc.)
- Some tools require provider access (shown)
- Tool-specific configuration where applicable

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-029-01 | Create `AgentToolsTab` component | 3h | Frontend |
| T-029-02 | Create tool catalog with categories | 2h | Frontend |
| T-029-03 | Add enable/disable toggles with provider warnings | 2h | Frontend |
| T-029-04 | Add tool configuration modals | 2h | Frontend |
| T-029-05 | Fetch available tools from API | 1h | Frontend |

**Slice Details**:

##### T-029-01: AgentToolsTab component
```tsx
// src/OpenCortex.Portal/Frontend/src/components/Agents/AgentToolsTab.tsx
export function AgentToolsTab({ agent, onChange, availableProviders }: Props) {
  const { data: toolCatalog } = useToolCatalog();

  const toolsByCategory = useMemo(() =>
    groupBy(toolCatalog, 'category'),
    [toolCatalog]
  );

  const isToolEnabled = (toolName: string) =>
    agent.enabledTools.includes(toolName) && !agent.disabledTools.includes(toolName);

  const toggleTool = (toolName: string, enabled: boolean) => {
    if (enabled) {
      onChange({
        enabledTools: [...agent.enabledTools, toolName],
        disabledTools: agent.disabledTools.filter(t => t !== toolName)
      });
    } else {
      onChange({
        enabledTools: agent.enabledTools.filter(t => t !== toolName),
        disabledTools: [...agent.disabledTools, toolName]
      });
    }
  };

  return (
    <div className="agent-tools-tab">
      <div className="tools-header">
        <h3>Tool Access</h3>
        <p>Select which tools this agent can use. Some tools require provider access.</p>
      </div>

      {Object.entries(toolsByCategory).map(([category, tools]) => (
        <ToolCategory key={category} title={CATEGORY_LABELS[category]}>
          {tools.map(tool => (
            <ToolRow
              key={tool.name}
              tool={tool}
              enabled={isToolEnabled(tool.name)}
              onToggle={enabled => toggleTool(tool.name, enabled)}
              providerRequired={tool.requiresProvider}
              providerConfigured={tool.requiresProvider
                ? availableProviders.includes(tool.requiresProvider)
                : true}
            />
          ))}
        </ToolCategory>
      ))}

      <QuickPresets
        onApply={preset => onChange({ enabledTools: preset.tools, disabledTools: [] })}
        presets={[
          { name: 'Read-Only', tools: ['read_file', 'list_directory', 'grep_search', 'recall_memories'] },
          { name: 'Full Access', tools: toolCatalog.map(t => t.name) },
          { name: 'Code Review', tools: ['read_file', 'list_directory', 'grep_search', 'save_memory'] },
          { name: 'Research', tools: ['read_file', 'web_search', 'web_fetch', 'save_memory', 'recall_memories'] }
        ]}
      />
    </div>
  );
}

function ToolRow({ tool, enabled, onToggle, providerRequired, providerConfigured }: ToolRowProps) {
  return (
    <div className={cn('tool-row', { disabled: providerRequired && !providerConfigured })}>
      <Toggle checked={enabled} onChange={onToggle} disabled={providerRequired && !providerConfigured} />
      <div className="tool-info">
        <span className="tool-name">{tool.displayName}</span>
        <span className="tool-description">{tool.description}</span>
      </div>
      {providerRequired && (
        <ProviderBadge
          provider={providerRequired}
          configured={providerConfigured}
        />
      )}
      {tool.hasConfig && enabled && (
        <Button size="xs" variant="ghost" onClick={() => openToolConfig(tool)}>
          Configure
        </Button>
      )}
    </div>
  );
}
```

---

#### US-030: Agent Provider Access Configuration
**As a** user
**I want** to grant my agent access to external providers (GitHub, etc.)
**So that** it can use tools that require authentication

**Acceptance Criteria**:
- Show configured providers for the user
- Toggle which providers the agent can access
- Show tools that require each provider
- Link to provider configuration if not set up
- Scope selection (read/write) where applicable

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-030-01 | Create `AgentProvidersTab` component | 3h | Frontend |
| T-030-02 | Show user's configured providers | 1h | Frontend |
| T-030-03 | Add provider access toggles | 1h | Frontend |
| T-030-04 | Add scope/permission selector | 2h | Frontend |
| T-030-05 | Link to provider setup when not configured | 1h | Frontend |

**Slice Details**:

##### T-030-01: AgentProvidersTab component
```tsx
// src/OpenCortex.Portal/Frontend/src/components/Agents/AgentProvidersTab.tsx
export function AgentProvidersTab({ agent, onChange }: Props) {
  const { data: userProviders } = useUserProviders();

  return (
    <div className="agent-providers-tab">
      <div className="providers-header">
        <h3>Provider Access</h3>
        <p>
          Grant this agent access to your configured providers.
          This allows the agent to use provider-specific tools on your behalf.
        </p>
      </div>

      <ProviderList>
        {SUPPORTED_PROVIDERS.map(provider => {
          const userConfig = userProviders?.find(p => p.providerId === provider.id);
          const isGranted = agent.grantedProviders?.includes(provider.id);

          return (
            <ProviderAccessCard
              key={provider.id}
              provider={provider}
              configured={!!userConfig}
              granted={isGranted}
              onToggle={granted => {
                const newGranted = granted
                  ? [...(agent.grantedProviders ?? []), provider.id]
                  : (agent.grantedProviders ?? []).filter(p => p !== provider.id);
                onChange({ grantedProviders: newGranted });
              }}
            />
          );
        })}
      </ProviderList>
    </div>
  );
}

function ProviderAccessCard({ provider, configured, granted, onToggle }: CardProps) {
  return (
    <div className={cn('provider-card', { 'not-configured': !configured })}>
      <div className="provider-header">
        <ProviderLogo provider={provider.id} size={32} />
        <div className="provider-info">
          <span className="provider-name">{provider.name}</span>
          <span className="provider-description">{provider.description}</span>
        </div>
        {configured ? (
          <Toggle checked={granted} onChange={onToggle} />
        ) : (
          <Button size="sm" variant="secondary" onClick={() => navigate('/settings/providers')}>
            Configure
          </Button>
        )}
      </div>

      {configured && granted && (
        <div className="provider-scope">
          <FormField label="Access Scope">
            <ScopeSelector
              provider={provider.id}
              value={agent.providerScopes?.[provider.id] ?? 'full'}
              onChange={scope => updateScope(provider.id, scope)}
              options={provider.scopes}
            />
          </FormField>
        </div>
      )}

      <div className="provider-tools">
        <span className="label">Enables tools:</span>
        {provider.tools.map(tool => (
          <ToolBadge key={tool} tool={tool} />
        ))}
      </div>
    </div>
  );
}

const SUPPORTED_PROVIDERS = [
  {
    id: 'github',
    name: 'GitHub',
    description: 'Access repositories, create PRs, manage issues',
    tools: ['clone_repository', 'create_branch', 'create_pull_request', 'list_repository_files'],
    scopes: [
      { value: 'read', label: 'Read Only', description: 'Clone and read repositories' },
      { value: 'write', label: 'Read & Write', description: 'Create branches, commits, and PRs' },
      { value: 'full', label: 'Full Access', description: 'All GitHub operations' }
    ]
  },
  {
    id: 'jira',
    name: 'Jira',
    description: 'Create and manage Jira issues',
    tools: ['create_jira_issue', 'update_jira_issue', 'search_jira'],
    scopes: [
      { value: 'read', label: 'Read Only' },
      { value: 'write', label: 'Read & Write' }
    ]
  },
  {
    id: 'slack',
    name: 'Slack',
    description: 'Send messages and notifications',
    tools: ['send_slack_message', 'search_slack'],
    scopes: [
      { value: 'post', label: 'Post Messages' },
      { value: 'read', label: 'Read & Post' }
    ]
  }
];
```

---

#### US-031: Agent Limits Configuration
**As a** user
**I want** to set resource limits for my agent
**So that** I can control costs and prevent runaway execution

**Acceptance Criteria**:
- Max iterations slider
- Timeout duration
- Model preference (optional override)
- Token budget (future)

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-031-01 | Create `AgentLimitsTab` component | 2h | Frontend |
| T-031-02 | Add max iterations slider | 30m | Frontend |
| T-031-03 | Add timeout selector | 30m | Frontend |
| T-031-04 | Add model preference dropdown | 1h | Frontend |

**Slice Details**:

##### T-031-01: AgentLimitsTab component
```tsx
// src/OpenCortex.Portal/Frontend/src/components/Agents/AgentLimitsTab.tsx
export function AgentLimitsTab({ agent, onChange }: Props) {
  return (
    <div className="agent-limits-tab">
      <div className="limits-header">
        <h3>Execution Limits</h3>
        <p>Control how much this agent can do in a single delegation.</p>
      </div>

      <FormField label="Max Iterations" hint="Maximum tool-call loops before stopping">
        <Slider
          value={agent.maxIterations}
          onChange={v => onChange({ maxIterations: v })}
          min={1}
          max={50}
          step={1}
          marks={[
            { value: 5, label: 'Quick (5)' },
            { value: 15, label: 'Standard (15)' },
            { value: 30, label: 'Thorough (30)' },
            { value: 50, label: 'Max (50)' }
          ]}
        />
        <span className="current-value">{agent.maxIterations} iterations</span>
      </FormField>

      <FormField label="Timeout" hint="Max execution time before cancellation">
        <Select
          value={agent.timeoutMinutes ?? 5}
          onChange={v => onChange({ timeoutMinutes: v })}
          options={[
            { value: 1, label: '1 minute' },
            { value: 2, label: '2 minutes' },
            { value: 5, label: '5 minutes (default)' },
            { value: 10, label: '10 minutes' },
            { value: 15, label: '15 minutes' }
          ]}
        />
      </FormField>

      <FormField label="Model Preference" hint="Override the default model for this agent">
        <Select
          value={agent.modelPreference ?? ''}
          onChange={v => onChange({ modelPreference: v || null })}
          options={[
            { value: '', label: 'Use default' },
            { value: 'claude-haiku', label: 'Claude Haiku (fast, cheap)' },
            { value: 'claude-sonnet', label: 'Claude Sonnet (balanced)' },
            { value: 'claude-opus', label: 'Claude Opus (powerful)' }
          ]}
        />
      </FormField>

      <InfoBox variant="tip">
        Shorter limits = faster responses and lower costs.
        Longer limits = more thorough work on complex tasks.
      </InfoBox>
    </div>
  );
}
```

---

#### US-032: Agent Editor Page
**As a** user
**I want** a unified editor to create/edit agents
**So that** I can configure all aspects in one place

**Acceptance Criteria**:
- Tab-based layout (Identity, Soul, Tools, Providers, Limits)
- Live validation
- Save/discard changes
- Duplicate agent action
- Delete with confirmation

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-032-01 | Create `AgentEditorPage` component | 4h | Frontend |
| T-032-02 | Implement tab navigation | 1h | Frontend |
| T-032-03 | Add form validation | 2h | Frontend |
| T-032-04 | Implement save/create API calls | 2h | Frontend |
| T-032-05 | Add duplicate and delete actions | 1h | Frontend |
| T-032-06 | Add unsaved changes warning | 1h | Frontend |

**Slice Details**:

##### T-032-01: AgentEditorPage component
```tsx
// src/OpenCortex.Portal/Frontend/src/pages/Agents/AgentEditorPage.tsx
export function AgentEditorPage() {
  const { agentId } = useParams();
  const isNew = agentId === 'new';

  const { data: existingAgent } = useAgent(agentId, { enabled: !isNew });
  const [agent, setAgent] = useState<AgentProfile>(isNew ? DEFAULT_AGENT : existingAgent);
  const [activeTab, setActiveTab] = useState('identity');
  const [errors, setErrors] = useState<Record<string, string>>({});

  const createMutation = useCreateAgent();
  const updateMutation = useUpdateAgent();

  const handleSave = async () => {
    const validationErrors = validateAgent(agent);
    if (Object.keys(validationErrors).length > 0) {
      setErrors(validationErrors);
      return;
    }

    if (isNew) {
      await createMutation.mutateAsync(agent);
    } else {
      await updateMutation.mutateAsync({ agentId, ...agent });
    }
    navigate('/agents');
  };

  return (
    <div className="agent-editor-page">
      <PageHeader
        title={isNew ? 'Create Agent' : `Edit ${agent.name}`}
        backLink="/agents"
      >
        <Button variant="secondary" onClick={() => navigate('/agents')}>
          Cancel
        </Button>
        <Button
          variant="primary"
          onClick={handleSave}
          loading={createMutation.isLoading || updateMutation.isLoading}
        >
          {isNew ? 'Create Agent' : 'Save Changes'}
        </Button>
        {!isNew && (
          <DropdownMenu>
            <DropdownItem onClick={handleDuplicate}>Duplicate</DropdownItem>
            <DropdownItem onClick={handleDelete} variant="danger">Delete</DropdownItem>
          </DropdownMenu>
        )}
      </PageHeader>

      <Tabs value={activeTab} onChange={setActiveTab}>
        <Tab value="identity" label="Identity" icon="ðŸ‘¤" />
        <Tab value="soul" label="Soul" icon="âœ¨" />
        <Tab value="tools" label="Tools" icon="ðŸ”§" badge={agent.enabledTools.length} />
        <Tab value="providers" label="Providers" icon="ðŸ”—" />
        <Tab value="limits" label="Limits" icon="âš™ï¸" />
      </Tabs>

      <div className="tab-content">
        {activeTab === 'identity' && (
          <AgentIdentityTab agent={agent} onChange={update} errors={errors} />
        )}
        {activeTab === 'soul' && (
          <AgentSoulTab agent={agent} onChange={update} />
        )}
        {activeTab === 'tools' && (
          <AgentToolsTab agent={agent} onChange={update} />
        )}
        {activeTab === 'providers' && (
          <AgentProvidersTab agent={agent} onChange={update} />
        )}
        {activeTab === 'limits' && (
          <AgentLimitsTab agent={agent} onChange={update} />
        )}
      </div>

      <UnsavedChangesWarning when={hasChanges} />
    </div>
  );
}

const DEFAULT_AGENT: AgentProfile = {
  name: '',
  description: '',
  specialization: 'custom',
  systemPrompt: `You are a helpful assistant specialized in...

Your responsibilities:
-

When working:
1.
2.
3.

Be thorough but concise.`,
  enabledTools: ['read_file', 'list_directory', 'save_memory', 'recall_memories'],
  disabledTools: [],
  maxIterations: 15,
  timeoutMinutes: 5,
  grantedProviders: []
};
```

---

## Feature 2: Delegation Tool

**Feature ID**: `FEAT-003-02`

Tool for agents to delegate work to specialist agents.

### User Stories

#### US-020: Delegate To Agent Tool
**As a** lead agent
**I want to** delegate a task to a specialist agent
**So that** I can leverage specialized capabilities

**Acceptance Criteria**:
- Tool accepts: agent_id, task_description, context
- Creates task linked to delegation
- Spawns sub-agent execution
- Returns results when complete
- Respects delegation limits

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-020-01 | Create `DelegationToolDefinitions.cs` | 1h | Tools |
| T-020-02 | Implement `DelegateToAgentHandler` | 6h | Tools |
| T-020-03 | Add delegation depth tracking | 2h | Tools |
| T-020-04 | Add delegation quota check | 1h | Billing |
| T-020-05 | Write handler tests | 3h | Testing |

**Slice Details**:

##### T-020-01: Tool definition
```csharp
// src/OpenCortex.Tools/Delegation/DelegationToolDefinitions.cs
public static class DelegationToolDefinitions
{
    public static ToolDefinition DelegateToAgent => new()
    {
        Type = "function",
        Function = new FunctionDefinition
        {
            Name = "delegate_to_agent",
            Description = """
                Delegate a specific task to a specialist agent. The agent will execute autonomously and return results.
                Use this when a task requires specialized expertise that another agent has.

                Available specialists:
                - code-reviewer: Review code for quality and security
                - researcher: Gather information from docs, web, code
                - planner: Break down complex goals into tasks
                - writer: Create documentation and content
                - debugger: Diagnose and fix issues
                """,
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    agent_id = new
                    {
                        type = "string",
                        description = "The specialist agent ID to delegate to"
                    },
                    task_description = new
                    {
                        type = "string",
                        description = "Clear description of what you want the agent to accomplish"
                    },
                    context = new
                    {
                        type = "string",
                        description = "Relevant context: file paths, background info, constraints"
                    },
                    priority = new
                    {
                        type = "string",
                        @enum = new[] { "low", "medium", "high" },
                        @default = "medium"
                    }
                },
                required = new[] { "agent_id", "task_description" }
            }
        }
    };

    public static ToolDefinition ListAvailableAgents => new()
    {
        Type = "function",
        Function = new FunctionDefinition
        {
            Name = "list_available_agents",
            Description = "List all available specialist agents you can delegate tasks to.",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    specialization = new
                    {
                        type = "string",
                        description = "Optional: filter by specialization"
                    }
                }
            }
        }
    };
}
```

##### T-020-02: DelegateToAgentHandler
```csharp
// src/OpenCortex.Tools/Delegation/DelegateToAgentHandler.cs
public sealed class DelegateToAgentHandler : IToolHandler
{
    private readonly IAgentProfileRepository _agentRepo;
    private readonly ISubAgentOrchestrator _subAgentOrchestrator;
    private readonly ITaskRepository _taskRepo;
    private readonly IDelegationQuotaChecker _quotaChecker;
    private readonly ILogger<DelegateToAgentHandler> _logger;

    public string ToolName => "delegate_to_agent";
    public string Category => "delegation";

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var agentId = arguments.GetProperty("agent_id").GetString()
            ?? throw new ArgumentException("agent_id is required");

        var taskDescription = arguments.GetProperty("task_description").GetString()
            ?? throw new ArgumentException("task_description is required");

        var taskContext = arguments.TryGetProperty("context", out var ctxEl)
            ? ctxEl.GetString()
            : null;

        var priority = arguments.TryGetProperty("priority", out var priEl)
            ? priEl.GetString() ?? "medium"
            : "medium";

        // Check delegation depth
        if (context.DelegationDepth >= context.MaxDelegationDepth)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Maximum delegation depth ({context.MaxDelegationDepth}) reached. Cannot delegate further."
            });
        }

        // Check quota
        var quotaResult = await _quotaChecker.CheckAsync(
            context.UserId, context.CustomerId, cancellationToken);

        if (!quotaResult.Allowed)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Daily delegation quota exceeded ({quotaResult.Current}/{quotaResult.Max})"
            });
        }

        // Get agent profile
        var agent = await _agentRepo.GetByIdAsync(agentId, cancellationToken)
                 ?? await _agentRepo.GetByNameAsync(agentId, context.CustomerId, cancellationToken);

        if (agent is null || !agent.IsActive)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Agent '{agentId}' not found or inactive"
            });
        }

        // Create task for tracking
        var task = new Domain.Tasks.Task
        {
            UserId = context.UserId,
            CustomerId = context.CustomerId,
            ConversationId = context.ConversationId,
            ParentTaskId = context.CurrentTaskId,
            Title = $"[{agent.Name}] {taskDescription[..Math.Min(50, taskDescription.Length)]}",
            Goal = taskDescription,
            Description = taskContext,
            Priority = Enum.Parse<TaskPriority>(priority, ignoreCase: true),
            Status = TaskStatus.InProgress,
            Metadata = new Dictionary<string, object>
            {
                ["delegated_to"] = agent.AgentProfileId,
                ["delegation_depth"] = context.DelegationDepth + 1
            }
        };

        await _taskRepo.CreateAsync(task, cancellationToken);

        _logger.LogInformation(
            "Delegating to agent {AgentId} for task {TaskId}. Depth={Depth}",
            agent.AgentProfileId, task.TaskId, context.DelegationDepth + 1);

        try
        {
            // Execute sub-agent
            var result = await _subAgentOrchestrator.ExecuteAsync(
                new SubAgentRequest
                {
                    AgentProfile = agent,
                    TaskDescription = taskDescription,
                    Context = taskContext,
                    ParentContext = context,
                    TaskId = task.TaskId
                },
                cancellationToken);

            // Update task with result
            task.Status = result.Success ? TaskStatus.Completed : TaskStatus.Failed;
            task.Result = result.Output;
            task.ResultSummary = result.Summary;
            task.CompletedAt = DateTimeOffset.UtcNow;
            await _taskRepo.UpdateAsync(task, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                success = result.Success,
                agent = agent.Name,
                task_id = task.TaskId,
                result = result.Output,
                summary = result.Summary,
                iterations = result.Iterations,
                tool_calls = result.ToolCallCount
            });
        }
        catch (Exception ex)
        {
            task.Status = TaskStatus.Failed;
            task.Result = ex.Message;
            await _taskRepo.UpdateAsync(task, cancellationToken);

            _logger.LogError(ex, "Delegation to {AgentId} failed", agent.AgentProfileId);

            return JsonSerializer.Serialize(new
            {
                success = false,
                agent = agent.Name,
                task_id = task.TaskId,
                error = $"Delegation failed: {ex.Message}"
            });
        }
    }
}
```

---

## Feature 3: Sub-Agent Orchestration

**Feature ID**: `FEAT-003-03`

Execute delegated tasks as autonomous sub-agents.

### User Stories

#### US-021: Sub-Agent Execution Engine
**As a** delegation handler
**I want to** execute sub-agents with proper context
**So that** delegated tasks complete successfully

**Acceptance Criteria**:
- Sub-agent uses parent's workspace
- Sub-agent has access to memory brain
- Context from parent is injected
- Results are captured and returned
- Telemetry tracks sub-agent execution

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-021-01 | Create `ISubAgentOrchestrator` interface | 1h | Abstractions |
| T-021-02 | Implement `SubAgentOrchestrator` | 6h | Orchestration |
| T-021-03 | Build sub-agent context from parent | 2h | Orchestration |
| T-021-04 | Implement result extraction | 2h | Orchestration |
| T-021-05 | Add sub-agent telemetry | 2h | Telemetry |
| T-021-06 | Write orchestrator tests | 3h | Testing |

**Slice Details**:

##### T-021-01: Interface
```csharp
// src/OpenCortex.Orchestration/Delegation/ISubAgentOrchestrator.cs
public interface ISubAgentOrchestrator
{
    Task<SubAgentResult> ExecuteAsync(
        SubAgentRequest request,
        CancellationToken cancellationToken);
}

public sealed class SubAgentRequest
{
    public AgentProfile AgentProfile { get; init; }
    public string TaskDescription { get; init; }
    public string? Context { get; init; }
    public ToolExecutionContext ParentContext { get; init; }
    public string TaskId { get; init; }
}

public sealed class SubAgentResult
{
    public bool Success { get; init; }
    public string? Output { get; init; }
    public string? Summary { get; init; }
    public string? Error { get; init; }
    public int Iterations { get; init; }
    public int ToolCallCount { get; init; }
    public TimeSpan Duration { get; init; }
}
```

##### T-021-02: SubAgentOrchestrator implementation
```csharp
// src/OpenCortex.Orchestration/Delegation/SubAgentOrchestrator.cs
public sealed class SubAgentOrchestrator : ISubAgentOrchestrator
{
    private readonly IAgenticOrchestrationEngine _engine;
    private readonly IToolExecutor _toolExecutor;
    private readonly ILogger<SubAgentOrchestrator> _logger;

    public async Task<SubAgentResult> ExecuteAsync(
        SubAgentRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // Build messages for sub-agent
        var messages = new List<ChatMessage>
        {
            new()
            {
                Role = ChatRole.System,
                Content = request.AgentProfile.SystemPrompt
            },
            new()
            {
                Role = ChatRole.User,
                Content = BuildTaskPrompt(request)
            }
        };

        // Build sub-agent context (inherits workspace, memory, etc.)
        var subContext = new ToolExecutionContext
        {
            UserId = request.ParentContext.UserId,
            CustomerId = request.ParentContext.CustomerId,
            ConversationId = request.ParentContext.ConversationId,
            WorkspacePath = request.ParentContext.WorkspacePath,
            MemoryBrainId = request.ParentContext.MemoryBrainId,
            Credentials = request.ParentContext.Credentials,
            DelegationDepth = request.ParentContext.DelegationDepth + 1,
            MaxDelegationDepth = request.ParentContext.MaxDelegationDepth,
            CurrentTaskId = request.TaskId
        };

        // Determine available tools
        var tools = _toolExecutor.GetToolsByName(request.AgentProfile.EnabledTools)
            .Where(t => !request.AgentProfile.DisabledTools.Contains(t.Function.Name))
            .ToList();

        // Execute the sub-agent
        var result = await _engine.ExecuteAgenticAsync(
            new AgenticOrchestrationRequest
            {
                UserId = subContext.UserId,
                CustomerId = subContext.CustomerId,
                ConversationId = subContext.ConversationId,
                Messages = messages,
                EnabledTools = tools.Select(t => t.Function.Name).ToList(),
                MaxIterations = request.AgentProfile.MaxIterations,
                ModelId = request.AgentProfile.ModelPreference,
                Credentials = request.ParentContext.Credentials
            },
            cancellationToken);

        stopwatch.Stop();

        // Extract result summary
        var lastAssistantMessage = result.Conversation
            .LastOrDefault(m => m.Role == ChatRole.Assistant);

        return new SubAgentResult
        {
            Success = !result.ReachedMaxIterations && string.IsNullOrEmpty(result.Error),
            Output = lastAssistantMessage?.Content,
            Summary = ExtractSummary(lastAssistantMessage?.Content),
            Error = result.Error,
            Iterations = result.Iterations,
            ToolCallCount = result.ToolExecutions.Count,
            Duration = stopwatch.Elapsed
        };
    }

    private static string BuildTaskPrompt(SubAgentRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Task");
        sb.AppendLine(request.TaskDescription);
        sb.AppendLine();

        if (!string.IsNullOrEmpty(request.Context))
        {
            sb.AppendLine("# Context");
            sb.AppendLine(request.Context);
            sb.AppendLine();
        }

        sb.AppendLine("# Instructions");
        sb.AppendLine("Complete this task and provide a clear summary of your findings/results.");
        sb.AppendLine("Be thorough but concise.");

        return sb.ToString();
    }

    private static string? ExtractSummary(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return null;

        // Take first 500 chars as summary, or up to first double newline
        var firstParagraph = content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return firstParagraph?[..Math.Min(500, firstParagraph.Length)];
    }
}
```

---

#### US-022: Delegation Telemetry
**As an** operator
**I want to** track delegation patterns and performance
**So that** I can optimize agent configurations

**Acceptance Criteria**:
- Track: delegations per session, agent usage, success rates
- Aggregate sub-agent metrics to parent
- Include delegation tree in response
- Log delegation events

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-022-01 | Add delegation metrics to AgenticTelemetry | 1h | Telemetry |
| T-022-02 | Track per-agent delegation stats | 2h | Telemetry |
| T-022-03 | Build delegation tree for response | 2h | API |
| T-022-04 | Write telemetry tests | 1h | Testing |

**Slice Details**:

##### T-022-01: Telemetry extension
```csharp
// Add to AgenticTelemetry
public int DelegationsInitiated { get; set; }
public int DelegationsCompleted { get; set; }
public int DelegationsFailed { get; set; }
public IReadOnlyList<DelegationMetric> DelegationMetrics { get; set; } = [];

public sealed class DelegationMetric
{
    public string AgentId { get; init; }
    public string TaskId { get; init; }
    public bool Success { get; init; }
    public int Iterations { get; init; }
    public int ToolCalls { get; init; }
    public TimeSpan Duration { get; init; }
}
```

---

## Feature 4: Delegation Safety & Limits

**Feature ID**: `FEAT-003-04`

Prevent runaway delegation and resource abuse.

### User Stories

#### US-023: Delegation Depth Limiting
**As a** system
**I want to** limit how deep delegation chains can go
**So that** resources aren't exhausted

**Acceptance Criteria**:
- Maximum delegation depth configurable (default: 2)
- Clear error when depth exceeded
- Depth tracked in context
- Cannot delegate to self

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-023-01 | Add depth tracking to ToolExecutionContext | 1h | Domain |
| T-023-02 | Implement depth check in handler | 1h | Tools |
| T-023-03 | Add self-delegation prevention | 30m | Tools |
| T-023-04 | Make max depth configurable | 30m | Configuration |
| T-023-05 | Write depth limit tests | 1h | Testing |

---

#### US-024: Delegation Quotas
**As a** billing system
**I want to** limit delegations per day
**So that** costs are controlled

**Acceptance Criteria**:
- Free: 10 delegations/day
- Pro: 100 delegations/day
- Enterprise: 500 delegations/day
- Quota resets daily
- Clear error when exceeded

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-024-01 | Add DelegationQuota to PlanEntitlements | 1h | Billing |
| T-024-02 | Create `IDelegationQuotaChecker` | 1h | Abstractions |
| T-024-03 | Implement daily quota tracking | 2h | Services |
| T-024-04 | Integrate into delegation handler | 1h | Tools |
| T-024-05 | Write quota tests | 1h | Testing |

---

#### US-025: Delegation Timeout
**As a** system
**I want to** timeout long-running delegations
**So that** users aren't stuck waiting

**Acceptance Criteria**:
- Default timeout: 5 minutes per delegation
- Timeout configurable per agent profile
- Graceful cancellation
- Partial results returned if available

**Tasks**:

| Task ID | Description | Effort | Slice |
|---------|-------------|--------|-------|
| T-025-01 | Add timeout to SubAgentOrchestrator | 2h | Orchestration |
| T-025-02 | Add timeout config to AgentProfile | 30m | Domain |
| T-025-03 | Implement graceful cancellation | 2h | Orchestration |
| T-025-04 | Return partial results on timeout | 1h | Orchestration |
| T-025-05 | Write timeout tests | 1h | Testing |

---

# Implementation Timeline

## Week 1-2: Agent Registry
- [ ] US-016: Agent Profile Storage
- [ ] US-017: Seed Default Agents
- [ ] US-018: Agent Profile Management API

## Week 3-4: Delegation Tool
- [ ] US-019: Custom Agent Creation
- [ ] US-020: Delegate To Agent Tool

## Week 5-6: Sub-Agent Execution
- [ ] US-021: Sub-Agent Execution Engine
- [ ] US-022: Delegation Telemetry

## Week 7-8: Safety & Polish
- [ ] US-023: Delegation Depth Limiting
- [ ] US-024: Delegation Quotas
- [ ] US-025: Delegation Timeout

---

# Total Effort Summary

| Category | Tasks | Estimated Hours |
|----------|-------|-----------------|
| Database | 1 | 2h |
| Domain | 4 | 5h |
| Abstractions | 4 | 4h |
| Persistence | 1 | 3h |
| Services | 3 | 5h |
| Tools | 6 | 16h |
| Orchestration | 6 | 17h |
| Configuration | 3 | 2h |
| Billing | 3 | 4h |
| API | 5 | 9h |
| Telemetry | 4 | 6h |
| **Frontend (Agent UI)** | **28** | **45h** |
| Testing | 12 | 18h |
| **Total** | **80** | **~136h (~8-9 weeks)** |

## Portal UI Tasks Summary

| User Story | Tasks | Hours |
|------------|-------|-------|
| US-026: Agent List View | 5 | 8h |
| US-027: Agent Identity Config | 4 | 6h |
| US-028: Agent Soul Config | 6 | 10h |
| US-029: Agent Tools Config | 5 | 10h |
| US-030: Agent Providers Config | 5 | 8h |
| US-031: Agent Limits Config | 4 | 4h |
| US-032: Agent Editor Page | 6 | 11h |
| **Subtotal** | **35** | **~57h** |

---

# Default Agent Configurations

| Agent ID | Specialization | Tools | Max Iterations |
|----------|---------------|-------|----------------|
| `code-reviewer` | code-review | read_file, list_directory, grep_search | 15 |
| `researcher` | research | read_file, web_search, web_fetch | 20 |
| `planner` | planning | create_task, update_task, list_tasks | 10 |
| `writer` | writing | read_file, write_file, save_memory | 15 |
| `debugger` | debugging | read_file, execute_command, grep_search | 20 |

---

# Safety Limits

| Limit | Default | Configurable |
|-------|---------|--------------|
| Max delegation depth | 2 | Yes (per-customer) |
| Delegation timeout | 5 min | Yes (per-agent) |
| Daily delegation quota | By plan | No |
| Concurrent delegations | 3 | Yes |

---

# Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Infinite delegation loops | Resource exhaustion | Depth limiting, self-delegation prevention |
| Expensive sub-agents | Cost overrun | Quotas, model preferences, iteration limits |
| Slow delegations | Poor UX | Timeouts, streaming progress |
| Context bloat | Token limits | Summarized context passing |
