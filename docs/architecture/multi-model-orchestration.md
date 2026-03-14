# Multi-Model Orchestration

## Overview

OpenCortex extends beyond memory and retrieval to become a multi-model agent orchestration platform. The core principle is that **the app is the agent**—not any single model. Models are specialists; OpenCortex decides who handles each task, what tools are available, what memory is injected, and how answers are merged.

## Design Principles

- the orchestration layer owns routing decisions, not the models
- models are interchangeable workers behind a unified interface
- memory and tools flow through MCP, not model-specific integrations
- conversations are first-class persistent entities
- streaming is the default interaction mode
- cost, latency, and capability drive routing decisions

## Core Entities

### Model Provider

A configured connection to an LLM service.

Required fields:

- `providerId`
- `name`
- `type` (`openai`, `anthropic`, `ollama`)
- `endpoint` (URL or `local` for Ollama)
- `apiKeyRef` (secret reference, not plaintext)
- `defaultModel` (e.g., `gpt-4o`, `claude-sonnet-4-20250514`, `llama3.2`)
- `capabilities` (flags: `chat`, `code`, `vision`, `tools`, `streaming`)
- `costProfile` (`free`, `low`, `medium`, `high`)
- `isEnabled`

### Conversation

A persistent chat session with history and state.

Required fields:

- `conversationId`
- `brainId` (memory scope)
- `customerId`
- `title`
- `createdAt`
- `lastMessageAt`
- `status` (`active`, `archived`)

### Message

A single turn in a conversation.

Required fields:

- `messageId`
- `conversationId`
- `role` (`user`, `assistant`, `system`, `tool`)
- `content`
- `providerId` (which model generated this, if assistant)
- `modelId` (specific model used)
- `toolCalls` (array of tool invocations)
- `tokenUsage` (`promptTokens`, `completionTokens`)
- `latencyMs`
- `createdAt`

### Routing Rule

A declarative rule for task-to-model assignment.

Fields:

- `ruleId`
- `name`
- `priority` (lower = higher priority)
- `condition` (task classifier output or keyword match)
- `targetProviderId`
- `targetModelId` (optional override)
- `fallbackProviderId` (if primary fails)
- `isEnabled`

## Provider Interface

All model providers implement a common abstraction:

```csharp
public interface IModelProvider
{
    string ProviderId { get; }
    ProviderCapabilities Capabilities { get; }

    Task<ChatCompletion> CompleteAsync(
        ChatRequest request,
        CancellationToken ct = default);

    IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        CancellationToken ct = default);

    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}

public record ChatRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ToolDefinition>? Tools = null,
    ChatRequestOptions? Options = null);

public record ChatCompletion(
    string Content,
    IReadOnlyList<ToolCall>? ToolCalls,
    TokenUsage Usage,
    string FinishReason);

public record StreamChunk(
    string? ContentDelta,
    ToolCall? ToolCallDelta,
    bool IsComplete,
    TokenUsage? FinalUsage = null);

public record ProviderCapabilities(
    bool SupportsChat,
    bool SupportsCode,
    bool SupportsVision,
    bool SupportsTools,
    bool SupportsStreaming,
    int MaxContextTokens);
```

## Provider Implementations

### OpenAI Provider

Targets:

- OpenAI API (`api.openai.com`)
- Azure OpenAI
- OpenAI-compatible endpoints (vLLM, LocalAI, etc.)

Models: `gpt-4o`, `gpt-4o-mini`, `o1`, `o3-mini`, `codex`

Use cases:

- code generation and editing
- structured tool calling
- fast iteration tasks

### Anthropic Provider

Targets:

- Anthropic API (`api.anthropic.com`)
- Amazon Bedrock (Claude models)

Models: `claude-sonnet-4-20250514`, `claude-opus-4-20250514`, `claude-3-5-haiku`

Use cases:

- planning and system design
- deep reasoning and analysis
- long-form writing
- complex multi-step tasks

### Ollama Provider

Targets:

- Local Ollama instance (`localhost:11434`)
- Remote Ollama over HTTPS

Models: `llama3.2`, `mistral`, `codellama`, `phi3`, `qwen2.5`

Use cases:

- private/sensitive content
- low-cost helper tasks
- local document classification
- memory lookup and summarization
- offline operation

## Routing Engine

### Task Classification

Before routing, classify the incoming message:

| Category | Signals | Default Provider |
|----------|---------|------------------|
| `code` | code blocks, file paths, programming keywords | OpenAI |
| `planning` | design, architecture, roadmap keywords | Anthropic |
| `writing` | document, article, long-form indicators | Anthropic |
| `analysis` | analyze, compare, evaluate keywords | Anthropic |
| `quick` | simple questions, lookups | Ollama |
| `private` | user-flagged sensitive content | Ollama |
| `general` | default fallback | configurable |

### Routing Flow

```
User Message
    │
    ▼
┌─────────────────┐
│ Task Classifier │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Routing Rules   │──► Match by priority
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Provider Select │──► Check health, fallback if needed
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Memory Inject   │──► Pull relevant context from MCP
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Execute Request │──► Stream response
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Persist Message │──► Store in conversation
└─────────────────┘
```

### Multi-Model Execution

For high-stakes tasks, route to multiple models and merge:

- **Parallel mode**: send to 2-3 models simultaneously, present all responses
- **Consensus mode**: compare responses, surface agreement/disagreement
- **Merge mode**: use a coordinator model to synthesize a final answer

Merge coordinator can be local (Ollama) for cost efficiency.

## Memory Integration

The orchestration layer injects memory context before model execution:

1. Query the conversation's brain via MCP/OQL
2. Build a context pack from relevant documents
3. Prepend context to the system message or inject as tool results
4. Track which memory was used for explainability

### Memory Actions

Models can request memory operations via tool calls:

- `memory_search` - query the brain for relevant context
- `memory_save` - persist a fact, decision, or summary
- `memory_link` - associate current conversation with a document

## Conversation Persistence

### Schema

```sql
CREATE TABLE conversations (
    conversation_id UUID PRIMARY KEY,
    brain_id UUID NOT NULL REFERENCES brains(brain_id),
    customer_id UUID NOT NULL REFERENCES customers(customer_id),
    title TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_message_at TIMESTAMPTZ,
    status TEXT NOT NULL DEFAULT 'active',
    metadata JSONB
);

CREATE TABLE messages (
    message_id UUID PRIMARY KEY,
    conversation_id UUID NOT NULL REFERENCES conversations(conversation_id),
    role TEXT NOT NULL,
    content TEXT,
    provider_id TEXT,
    model_id TEXT,
    tool_calls JSONB,
    token_usage JSONB,
    latency_ms INT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_messages_conversation ON messages(conversation_id, created_at);
```

### Rolling Summaries

For long conversations:

- summarize older messages periodically
- keep full detail for recent N messages
- store summaries as special `system` role messages
- use local model (Ollama) for summarization to control costs

## Project Structure

```
src/
  OpenCortex.Providers.Abstractions/
    IModelProvider.cs
    ChatRequest.cs
    ChatCompletion.cs
    StreamChunk.cs
    ProviderCapabilities.cs
    ToolDefinition.cs
    ToolCall.cs

  OpenCortex.Providers.OpenAI/
    OpenAIProvider.cs
    OpenAIOptions.cs

  OpenCortex.Providers.Anthropic/
    AnthropicProvider.cs
    AnthropicOptions.cs

  OpenCortex.Providers.Ollama/
    OllamaProvider.cs
    OllamaOptions.cs

  OpenCortex.Orchestration/
    Routing/
      IRouter.cs
      TaskClassifier.cs
      RoutingRule.cs
      DefaultRouter.cs
    Execution/
      OrchestrationEngine.cs
      MultiModelExecutor.cs
      MergeCoordinator.cs
    Memory/
      MemoryInjector.cs
      MemoryToolHandler.cs

  OpenCortex.Conversations/
    Conversation.cs
    Message.cs
    ConversationRepository.cs
    ConversationService.cs
```

## Configuration

```json
{
  "OpenCortex": {
    "Orchestration": {
      "DefaultProvider": "anthropic",
      "EnableMultiModel": false,
      "TaskClassifierModel": "ollama:llama3.2"
    },
    "Providers": {
      "OpenAI": {
        "Endpoint": "https://api.openai.com/v1",
        "ApiKeyRef": "secrets:openai-api-key",
        "DefaultModel": "gpt-4o",
        "IsEnabled": true
      },
      "Anthropic": {
        "Endpoint": "https://api.anthropic.com",
        "ApiKeyRef": "secrets:anthropic-api-key",
        "DefaultModel": "claude-sonnet-4-20250514",
        "IsEnabled": true
      },
      "Ollama": {
        "Endpoint": "http://localhost:11434",
        "DefaultModel": "llama3.2",
        "IsEnabled": true
      }
    },
    "RoutingRules": [
      {
        "Name": "Code to OpenAI",
        "Priority": 10,
        "Condition": "category:code",
        "TargetProvider": "openai"
      },
      {
        "Name": "Planning to Claude",
        "Priority": 20,
        "Condition": "category:planning",
        "TargetProvider": "anthropic"
      },
      {
        "Name": "Private to Local",
        "Priority": 5,
        "Condition": "flag:private",
        "TargetProvider": "ollama"
      }
    ]
  }
}
```

## API Surface

### Conversation Endpoints

```
POST   /api/conversations                    Create conversation
GET    /api/conversations                    List conversations
GET    /api/conversations/{id}               Get conversation with messages
DELETE /api/conversations/{id}               Archive conversation

POST   /api/conversations/{id}/messages      Send message (streaming response)
GET    /api/conversations/{id}/messages      Get message history
```

### Provider Management

```
GET    /admin/providers                      List configured providers
GET    /admin/providers/{id}/health          Check provider health
PUT    /admin/providers/{id}                 Update provider config
```

## Implementation Phases

### Phase A: Provider Abstractions

- define `IModelProvider` interface and core DTOs
- implement OpenAI provider with streaming
- implement Anthropic provider with streaming
- implement Ollama provider with streaming
- add provider health checks

### Phase B: Conversation Layer

- add `conversations` and `messages` tables (migration)
- implement `ConversationRepository`
- implement `ConversationService` with message persistence
- add conversation API endpoints

### Phase C: Basic Routing

- implement task classifier (rule-based first, ML later)
- implement `DefaultRouter` with rule matching
- wire routing into conversation flow
- add memory injection from MCP

### Phase D: Chat UI

- add chat view to Portal frontend
- implement streaming message display
- add model indicator and token usage display
- add conversation list and management

### Phase E: Advanced Features

- multi-model parallel execution
- response merge coordinator
- rolling conversation summaries
- cost tracking and analytics

## User Provider Authentication

Users configure their own provider credentials rather than using system-level API keys. This supports both API keys and OAuth authentication.

### Authentication Methods

| Provider | API Key | OAuth |
|----------|---------|-------|
| Anthropic | Yes | Yes |
| OpenAI | Yes | Yes |
| Ollama | No (uses endpoint URL) | No |

### OAuth Flow

1. User initiates OAuth via `GET /api/providers/config/{providerId}/oauth/authorize`
2. App redirects to provider's authorization page (Anthropic Console / OpenAI Platform)
3. User grants access, provider redirects back with authorization code
4. App exchanges code for access + refresh tokens via `GET /api/providers/config/{providerId}/oauth/callback`
5. Tokens encrypted and stored per-user in `user_provider_configs` table
6. On API requests, access token used as bearer credential
7. Automatic token refresh when expiring (within 5 minutes of expiry)

### User Provider Config Schema

```sql
CREATE TABLE user_provider_configs (
    config_id UUID PRIMARY KEY,
    customer_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    provider_id TEXT NOT NULL,
    auth_type TEXT NOT NULL DEFAULT 'api_key',  -- 'api_key' or 'oauth'
    encrypted_api_key TEXT,
    encrypted_access_token TEXT,
    encrypted_refresh_token TEXT,
    token_expires_at TIMESTAMPTZ,
    settings_json JSONB,  -- default model, base URL for Ollama
    is_enabled BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ,
    CONSTRAINT uq_user_provider UNIQUE (user_id, provider_id)
);
```

### Provider Config Endpoints

```
GET    /api/providers/config                     List user's configured providers
GET    /api/providers/config/available           List available providers with auth options
GET    /api/providers/config/{id}                Get specific provider config
PUT    /api/providers/config/{id}                Save provider config (API key)
DELETE /api/providers/config/{id}                Delete provider config
POST   /api/providers/config/{id}/toggle         Enable/disable provider

GET    /api/providers/config/{id}/oauth/authorize    Get OAuth authorization URL
GET    /api/providers/config/{id}/oauth/callback     Handle OAuth callback
POST   /api/providers/config/{id}/oauth/disconnect   Revoke and disconnect OAuth
POST   /api/providers/config/{id}/oauth/refresh      Manually refresh token
```

### OAuth Configuration

```json
{
  "OpenCortex": {
    "Security": {
      "EncryptionKey": ""  // AES-256 key for credential encryption (user secrets)
    },
    "OAuth": {
      "Anthropic": {
        "ClientId": "",
        "ClientSecret": "",  // user secrets
        "RedirectUri": "https://your-app.com/api/providers/config/anthropic/oauth/callback"
      },
      "OpenAI": {
        "ClientId": "",
        "ClientSecret": "",  // user secrets
        "RedirectUri": "https://your-app.com/api/providers/config/openai/oauth/callback"
      }
    }
  }
}
```

### UserProviderFactory

The `IUserProviderFactory` creates provider instances with user-specific credentials:

```csharp
public interface IUserProviderFactory
{
    Task<IModelProvider?> GetProviderForUserAsync(Guid userId, string providerId, CancellationToken ct = default);
    Task<IReadOnlyList<IModelProvider>> GetProvidersForUserAsync(Guid userId, CancellationToken ct = default);
    Task<bool> HasConfiguredProvidersAsync(Guid userId, CancellationToken ct = default);
}
```

Features:
- Automatically refreshes expired OAuth tokens before creating provider
- Decrypts stored credentials using `ICredentialEncryption`
- Supports both API key and OAuth authentication per provider
- Falls back gracefully if credentials invalid or expired

## Security Considerations

- User credentials encrypted at rest with AES-256 (encryption key in user secrets)
- OAuth tokens automatically refreshed before expiration
- Provider credentials are per-user, not system-wide
- conversation access enforced by `customerId`
- tool calls validated against allowed MCP tools
- rate limiting per user and per provider

## Observability

- log provider latency and token usage per request
- track routing decisions for analysis
- expose provider health as Prometheus metrics
- alert on provider errors and fallback activations
