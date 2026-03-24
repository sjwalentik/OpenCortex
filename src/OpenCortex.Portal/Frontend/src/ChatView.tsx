import { useCallback, useEffect, useRef, useState } from 'react';

export type ChatMessage = {
  messageId: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  thinkingContent?: string;
  providerId?: string;
  modelId?: string;
  latencyMs?: number;
  createdAt?: string;
  isStreaming?: boolean;
  toolCalls?: ToolCallDisplay[];
};

export type ToolCallDisplay = {
  id: string;
  name: string;
  arguments?: string;
  status: 'pending' | 'running' | 'success' | 'error';
  result?: string;
  error?: string;
  durationMs?: number;
};

export type Conversation = {
  conversationId: string;
  title?: string;
  createdAt: string;
  lastMessageAt?: string;
  status: string;
};

type ConversationDetailResponse = {
  conversationId: string;
  messages?: ChatMessage[];
  providerId?: string | null;
  modelId?: string | null;
};

export type ChatViewProps = {
  authSession: { idToken: string } | null;
  activeBrainId: string;
  hasConfiguredProviders: boolean;
  onOpenProviderSettings: () => void;
  onRefreshSession: () => Promise<string | null>;
};

type StreamChunk = {
  eventType?: 'content' | 'status' | 'heartbeat' | 'error' | 'iteration_start' | 'iteration_complete' | 'tool_calls' | 'tool_result' | 'complete' | 'workspace_provisioning' | 'workspace_ready' | 'workspace_error';
  stage?: string;
  message?: string;
  // Workspace events
  status?: string;
  podName?: string;
  containerId?: string;
  startupDurationMs?: number;
  retryable?: boolean;
  timestamp?: string;
  contentDelta?: string;
  thinkingDelta?: string;
  isComplete?: boolean;
  finishReason?: string;
  providerId?: string;
  routing?: {
    category?: string;
    confidence?: number;
  };
  // Agentic events
  iteration?: number;
  traceId?: string;
  durationMs?: number;
  hasToolCalls?: boolean;
  tokenUsage?: {
    promptTokens?: number;
    completionTokens?: number;
    totalTokens?: number;
  };
  toolCalls?: Array<{
    Id: string;
    Name: string;
    Arguments: string;
  }>;
  toolCallId?: string;
  toolName?: string;
  success?: boolean;
  output?: string;
  error?: string;
  telemetry?: {
    traceId: string;
    totalDurationMs: number;
    llmDurationMs: number;
    toolDurationMs: number;
    tokenUsage: {
      totalPromptTokens: number;
      totalCompletionTokens: number;
      totalTokens: number;
    };
    llmCallCount: number;
    toolCallCount: number;
  };
};

type StreamActivity = {
  stage: string;
  message: string;
  providerId?: string;
  timestamp?: string;
  isHeartbeat?: boolean;
};

type ChatProviderSummary = {
  providerId: string;
  name: string;
  type?: string;
};

type ChatProviderCatalogResponse = {
  providers?: ChatProviderSummary[];
};

type ChatProviderModel = {
  id: string;
  name?: string;
};

type ChatProviderModelsResponse = {
  models?: ChatProviderModel[];
};

type ModelCommand =
  | { kind: 'show' }
  | { kind: 'auto' }
  | { kind: 'set'; providerId: string; modelId: string }
  | { kind: 'error'; message: string };
type ContextPreview = {
  totalMessages: number;
  compactedMessages: number;
  summarizedMessages: number;
  verbatimMessages: number;
  estimatedCharacters: number;
  estimatedTokens: number;
  digest: string | null;
};

const MAX_VERBATIM_HISTORY_MESSAGES = 12;
const MAX_SUMMARY_ENTRIES = 18;
const MAX_SUMMARY_CHARACTERS = 3000;
const MAX_SUMMARY_SNIPPET_CHARACTERS = 220;

function applyStreamActivity(activities: StreamActivity[], chunk: StreamChunk): StreamActivity[] {
  if (!chunk.stage || !chunk.message) {
    return activities;
  }

  const next = [...activities];
  const activity: StreamActivity = {
    stage: chunk.stage,
    message: chunk.message,
    providerId: chunk.providerId,
    timestamp: chunk.timestamp,
    isHeartbeat: chunk.eventType === 'heartbeat',
  };

  const existingIndex = next.findIndex((item) => item.stage === chunk.stage);
  if (existingIndex >= 0) {
    next[existingIndex] = activity;
    return next;
  }

  next.push(activity);
  return next.slice(-4);
}


function normalizeProviderAlias(value: string) {
  switch (value.trim().toLowerCase()) {
    case 'codex':
    case 'chatgpt':
    case 'gpt':
      return 'openai-codex';
    case 'auto':
      return '';
    default:
      return value.trim().toLowerCase();
  }
}

function parseModelCommand(input: string): ModelCommand | null {
  const trimmed = input.trim();
  if (!trimmed.toLowerCase().startsWith('/model')) {
    return null;
  }

  const args = trimmed.split(/\s+/).slice(1);
  if (args.length === 0) {
    return { kind: 'show' };
  }

  if (args.length === 1 && args[0].toLowerCase() === 'auto') {
    return { kind: 'auto' };
  }

  if (args.length > 2) {
    return { kind: 'error', message: 'Usage: /model auto, /model <provider>, /model <provider> <model>, or /model <provider>/<model>.' };
  }

  let providerToken = args[0];
  let modelToken = args[1] || '';

  if (!modelToken && providerToken.includes('/')) {
    const slashIndex = providerToken.indexOf('/');
    modelToken = providerToken.slice(slashIndex + 1);
    providerToken = providerToken.slice(0, slashIndex);
  }

  return { kind: 'set', providerId: providerToken.trim(), modelId: modelToken.trim() };
}

function formatModelSelection(providerId: string, modelId: string) {
  if (!providerId) {
    return 'auto routing';
  }

  return modelId ? `${providerId}/${modelId}` : `${providerId} (provider default)`;
}

function normalizeMessageContent(content: string | undefined) {
  return (content || '').trim().replace(/\s+/g, ' ');
}

function truncate(value: string, maxLength: number) {
  return value.length <= maxLength ? value : `${value.slice(0, Math.max(0, maxLength - 3))}...`;
}

function buildHistorySummaryLine(message: Pick<ChatMessage, 'role' | 'content'>) {
  const label = message.role === 'system' ? 'system' : message.role === 'assistant' ? 'assistant' : 'user';
  const normalized = normalizeMessageContent(message.content);
  if (!normalized) {
    return null;
  }

  return `[${label}] ${truncate(normalized, MAX_SUMMARY_SNIPPET_CHARACTERS)}`;
}

function buildHistoryDigest(messages: Pick<ChatMessage, 'role' | 'content'>[]) {
  const lines: string[] = [];
  let consumedEntries = 0;
  let totalCharacters = 0;

  for (const message of messages) {
    const line = buildHistorySummaryLine(message);
    if (!line) {
      continue;
    }

    if (consumedEntries >= MAX_SUMMARY_ENTRIES) {
      break;
    }

    const projected = totalCharacters + line.length + 3;
    if (projected > MAX_SUMMARY_CHARACTERS) {
      break;
    }

    lines.push(`- ${line}`);
    totalCharacters = projected;
    consumedEntries += 1;
  }

  if (lines.length === 0) {
    return null;
  }

  const summarizableMessages = messages.filter((message) => !!buildHistorySummaryLine(message)).length;
  const omittedEntries = summarizableMessages - consumedEntries;
  if (omittedEntries > 0) {
    lines.push(`- ... ${omittedEntries} earlier messages omitted to stay within the shared context budget.`);
  }

  return `Earlier conversation digest:
${lines.join('\n')}`;
}

function estimateTokensFromCharacters(characters: number) {
  return Math.max(1, Math.ceil(characters / 4));
}

function buildContextPreview(messages: ChatMessage[], pendingInput: string): ContextPreview {
  const outgoingMessages = [...messages];
  const trimmedInput = pendingInput.trim();
  if (trimmedInput && !trimmedInput.startsWith('/model')) {
    outgoingMessages.push({
      messageId: 'preview-pending',
      role: 'user',
      content: trimmedInput,
    });
  }

  if (outgoingMessages.length <= MAX_VERBATIM_HISTORY_MESSAGES) {
    const estimatedCharacters = outgoingMessages.reduce((sum, message) => sum + normalizeMessageContent(message.content).length, 0);
    return {
      totalMessages: outgoingMessages.length,
      compactedMessages: outgoingMessages.length,
      summarizedMessages: 0,
      verbatimMessages: outgoingMessages.length,
      estimatedCharacters,
      estimatedTokens: estimateTokensFromCharacters(estimatedCharacters),
      digest: null,
    };
  }

  const olderMessages = outgoingMessages.slice(0, Math.max(0, outgoingMessages.length - MAX_VERBATIM_HISTORY_MESSAGES));
  const recentMessages = outgoingMessages.slice(-MAX_VERBATIM_HISTORY_MESSAGES);
  const digest = buildHistoryDigest(olderMessages);
  const digestLength = digest ? digest.length : 0;
  const recentLength = recentMessages.reduce((sum, message) => sum + normalizeMessageContent(message.content).length, 0);
  const estimatedCharacters = digestLength + recentLength;

  return {
    totalMessages: outgoingMessages.length,
    compactedMessages: recentMessages.length + (digest ? 1 : 0),
    summarizedMessages: olderMessages.length,
    verbatimMessages: recentMessages.length,
    estimatedCharacters,
    estimatedTokens: estimateTokensFromCharacters(estimatedCharacters),
    digest,
  };
}

export function ChatView({
  authSession,
  activeBrainId,
  hasConfiguredProviders,
  onOpenProviderSettings,
  onRefreshSession,
}: ChatViewProps) {
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [conversationsLoading, setConversationsLoading] = useState(false);
  const [activeConversationId, setActiveConversationId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [messagesLoading, setMessagesLoading] = useState(false);
  const [inputValue, setInputValue] = useState('');
  const [isStreaming, setIsStreaming] = useState(false);
  const [streamingProviderId, setStreamingProviderId] = useState<string | null>(null);
  const [streamActivities, setStreamActivities] = useState<StreamActivity[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [agenticMode, setAgenticMode] = useState(false);
  const [currentIteration, setCurrentIteration] = useState(0);
  const [currentTraceId, setCurrentTraceId] = useState<string | null>(null);
  const [pendingToolCalls, setPendingToolCalls] = useState<ToolCallDisplay[]>([]);
  const [telemetrySummary, setTelemetrySummary] = useState<StreamChunk['telemetry'] | null>(null);
  const [availableProviders, setAvailableProviders] = useState<ChatProviderSummary[]>([]);
  const [providerModels, setProviderModels] = useState<ChatProviderModel[]>([]);
  const [selectedProviderId, setSelectedProviderId] = useState('');
  const [selectedModelId, setSelectedModelId] = useState('');
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const abortControllerRef = useRef<AbortController | null>(null);
  const activeConversationIdRef = useRef<string | null>(null);

  const scrollToBottom = useCallback(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, []);

  useEffect(() => {
    scrollToBottom();
  }, [messages, scrollToBottom]);

  useEffect(() => {
    if (!authSession) {
      return;
    }

    void loadConversations();
  }, [authSession]);

  useEffect(() => {
    if (!authSession || !hasConfiguredProviders) {
      setAvailableProviders([]);
      setProviderModels([]);
      setSelectedProviderId('');
      setSelectedModelId('');
      return;
    }

    void loadProviders();
  }, [authSession, hasConfiguredProviders]);

  useEffect(() => {
    if (!selectedProviderId) {
      setProviderModels([]);
      setSelectedModelId('');
      return;
    }

    void loadProviderModels(selectedProviderId);
  }, [selectedProviderId]);

  useEffect(() => {
    activeConversationIdRef.current = activeConversationId;
  }, [activeConversationId]);

  async function fetchWithAuth(url: string, init: RequestInit = {}) {
    async function execute() {
      const idToken = await onRefreshSession();
      if (!idToken) {
        throw new Error('Not authenticated');
      }

      const headers = new Headers(init.headers || {});
      headers.set('Authorization', `Bearer ${idToken}`);
      if (init.body && !headers.has('Content-Type')) {
        headers.set('Content-Type', 'application/json');
      }

      return fetch(url, { ...init, headers });
    }

    let response = await execute();
    if (response.status === 401) {
      response = await execute();
    }

    return response;
  }

  async function portalFetch(url: string, init: RequestInit = {}) {
    const response = await fetchWithAuth(url, init);
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || `HTTP ${response.status}`);
    }
    return response;
  }

  async function loadConversations() {
    setConversationsLoading(true);
    setError(null);
    try {
      const response = await portalFetch('/portal-api/tenant/conversations?limit=50');
      const data = await response.json();
      setConversations(data.conversations || []);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load conversations');
    } finally {
      setConversationsLoading(false);
    }
  }

  async function loadProviders() {
    try {
      const response = await portalFetch('/portal-api/api/chat/providers');
      const data = (await response.json()) as ChatProviderCatalogResponse;
      const providers = data.providers || [];

      setAvailableProviders(providers);

      if (selectedProviderId && !providers.some((provider) => provider.providerId === selectedProviderId)) {
        setSelectedProviderId('');
        setSelectedModelId('');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load providers');
    }
  }

  async function loadProviderModels(providerId: string) {
    try {
      const response = await portalFetch(`/portal-api/api/chat/providers/${encodeURIComponent(providerId)}/models`);
      const data = (await response.json()) as ChatProviderModelsResponse;
      const models = data.models || [];
      setProviderModels(models);

      if (selectedModelId && !models.some((model) => model.id === selectedModelId)) {
        setSelectedModelId('');
      }

      return models;
    } catch (err) {
      setProviderModels([]);
      setSelectedModelId('');
      setError(err instanceof Error ? err.message : 'Failed to load models');
      return [];
    }
  }

  function addLocalSystemMessage(content: string) {
    setMessages((prev) => [...prev, {
      messageId: `local-system-${Date.now()}`,
      role: 'system',
      content,
      createdAt: new Date().toISOString(),
    }]);
  }

  async function saveConversationRoutingPreference(
    conversationId: string,
    providerId: string,
    modelId: string,
  ) {
    await portalFetch(`/portal-api/tenant/conversations/${conversationId}`, {
      method: 'PATCH',
      body: JSON.stringify({
        title: null,
        updateRouting: true,
        providerId: providerId || null,
        modelId: modelId || null,
      }),
    });
  }

  async function applyProviderSelection(
    providerId: string,
    modelId: string,
  ) {
    setSelectedProviderId(providerId);
    setSelectedModelId(modelId);

    if (activeConversationIdRef.current) {
      await saveConversationRoutingPreference(activeConversationIdRef.current, providerId, modelId);
    }
  }

  async function handleModelCommand(command: ModelCommand) {
    setInputValue('');

    if (command.kind === 'show') {
      addLocalSystemMessage(`Current model selection: ${formatModelSelection(selectedProviderId, selectedModelId)}`);
      return;
    }

    if (command.kind === 'error') {
      addLocalSystemMessage(command.message);
      return;
    }

    if (command.kind === 'auto') {
      await applyProviderSelection('', '');
      addLocalSystemMessage('Model routing reset to auto.');
      return;
    }

    const normalizedProviderId = normalizeProviderAlias(command.providerId);
    const provider = availableProviders.find((candidate) => candidate.providerId === normalizedProviderId);

    if (!provider) {
      if (!selectedProviderId) {
        addLocalSystemMessage(`Unknown provider '${command.providerId}'. Choose one of: ${availableProviders.map((candidate) => candidate.providerId).join(', ') || 'none configured'}.`);
        return;
      }

      const models = await loadProviderModels(selectedProviderId);
      const desiredModelId = command.modelId || command.providerId;
      if (models.length > 0 && !models.some((model) => model.id === desiredModelId)) {
        addLocalSystemMessage(`Unknown model '${desiredModelId}' for ${selectedProviderId}.`);
        return;
      }

      await applyProviderSelection(selectedProviderId, desiredModelId);
      addLocalSystemMessage(`Model selection updated to ${formatModelSelection(selectedProviderId, desiredModelId)}.`);
      return;
    }

    const models = await loadProviderModels(normalizedProviderId);
    const desiredModelId = command.modelId || '';
    if (desiredModelId && models.length > 0 && !models.some((model) => model.id === desiredModelId)) {
      addLocalSystemMessage(`Unknown model '${desiredModelId}' for ${normalizedProviderId}.`);
      return;
    }

    await applyProviderSelection(normalizedProviderId, desiredModelId);
    addLocalSystemMessage(`Model selection updated to ${formatModelSelection(normalizedProviderId, desiredModelId)}.`);
  }

  async function handleProviderSelectionChange(nextProviderId: string) {
    await applyProviderSelection(nextProviderId, '');
  }

  async function handleModelSelectionChange(nextModelId: string) {
    await applyProviderSelection(selectedProviderId, nextModelId);
  }


  async function loadConversation(conversationId: string) {
    setMessagesLoading(true);
    setError(null);
    try {
      const response = await portalFetch(`/portal-api/tenant/conversations/${conversationId}?messageLimit=40`);
      const data = await response.json() as ConversationDetailResponse;
      setMessages(data.messages || []);
      setSelectedProviderId(data.providerId || '');
      setSelectedModelId(data.modelId || '');
      setActiveConversationId(conversationId);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load conversation');
    } finally {
      setMessagesLoading(false);
    }
  }

  async function createConversation(): Promise<string | null> {
    try {
      const response = await portalFetch('/portal-api/tenant/conversations', {
        method: 'POST',
        body: JSON.stringify({
          title: 'New Conversation',
          brainId: activeBrainId || null,
          providerId: selectedProviderId || null,
          modelId: selectedModelId || null,
        }),
      });
      const data = await response.json();
      await loadConversations();
      return data.conversationId;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create conversation');
      return null;
    }
  }

  async function archiveConversation(conversationId: string) {
    try {
      await portalFetch(`/portal-api/tenant/conversations/${conversationId}`, {
        method: 'DELETE',
      });
      if (activeConversationId === conversationId) {
        setActiveConversationId(null);
        setMessages([]);
      }
      await loadConversations();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to archive conversation');
    }
  }

  async function sendMessage() {
    if (!hasConfiguredProviders || !inputValue.trim() || isStreaming) return;

    const modelCommand = parseModelCommand(inputValue);
    if (modelCommand) {
      await handleModelCommand(modelCommand);
      return;
    }

    let conversationId = activeConversationId;
    if (!conversationId) {
      conversationId = await createConversation();
      if (!conversationId) return;
      setActiveConversationId(conversationId);
    }

    const userMessage: ChatMessage = {
      messageId: `temp-${Date.now()}`,
      role: 'user',
      content: inputValue.trim(),
      createdAt: new Date().toISOString(),
    };

    const assistantMessage: ChatMessage = {
      messageId: `temp-assistant-${Date.now()}`,
      role: 'assistant',
      content: '',
      isStreaming: true,
      createdAt: new Date().toISOString(),
      toolCalls: [],
    };

    setMessages((prev) => [...prev, userMessage, assistantMessage]);
    setInputValue('');
    setIsStreaming(true);
    setCurrentIteration(0);
    setCurrentTraceId(null);
    setPendingToolCalls([]);
    setTelemetrySummary(null);
    setStreamActivities([{ stage: 'queued', message: agenticMode ? 'Starting agentic execution...' : 'Sending request...', timestamp: new Date().toISOString() }]);
    setError(null);

    const endpoint = agenticMode
      ? '/portal-api/api/chat/completions/agentic/stream'
      : '/portal-api/api/chat/completions/stream';

    try {
      abortControllerRef.current = new AbortController();

      const response = await fetchWithAuth(endpoint, {
        method: 'POST',
        body: JSON.stringify({
          messages: [...messages, userMessage].map((m) => ({
            role: m.role,
            content: m.content,
          })),
          conversationId,
          brainId: activeBrainId || null,
          providerId: selectedProviderId || null,
          modelId: selectedModelId || null,
          enableTools: agenticMode,
        }),
        signal: abortControllerRef.current.signal,
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const reader = response.body?.getReader();
      if (!reader) {
        throw new Error('No response body');
      }

      const decoder = new TextDecoder();
      let buffer = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() || '';

        for (const line of lines) {
          if (!line.startsWith('data: ')) continue;
          const data = line.slice(6);
          if (data === '[DONE]') continue;

          try {
            const chunk: StreamChunk = JSON.parse(data);

            if (chunk.providerId) {
              setStreamingProviderId(chunk.providerId);
            }

            // Handle workspace provisioning events
            if (chunk.eventType === 'workspace_provisioning') {
              setStreamActivities((prev) => applyStreamActivity(prev, {
                eventType: 'status',
                stage: `workspace-${chunk.status}`,
                message: chunk.message || 'Initializing workspace...',
                timestamp: chunk.timestamp,
              }));
            }

            if (chunk.eventType === 'workspace_ready') {
              const startupTime = chunk.startupDurationMs || 0;
              const identifier = chunk.podName || chunk.containerId || 'ready';
              setStreamActivities((prev) => applyStreamActivity(prev, {
                eventType: 'status',
                stage: 'workspace-ready',
                message: `Workspace ready (${startupTime}ms)`,
                timestamp: chunk.timestamp,
              }));
            }

            if (chunk.eventType === 'workspace_error') {
              setStreamActivities((prev) => applyStreamActivity(prev, {
                eventType: 'status',
                stage: 'workspace-error',
                message: `Workspace error: ${chunk.error}${chunk.retryable ? ' (will retry)' : ''}`,
                timestamp: chunk.timestamp,
              }));
            }

            // Handle agentic-specific events
            if (chunk.eventType === 'iteration_start') {
              setCurrentIteration(chunk.iteration || 0);
              setCurrentTraceId(chunk.traceId || null);
              setStreamActivities((prev) => applyStreamActivity(prev, {
                eventType: 'status',
                stage: `iteration-${chunk.iteration}`,
                message: `Iteration ${chunk.iteration}: Thinking...`,
                timestamp: chunk.timestamp,
              }));
            }

            if (chunk.eventType === 'iteration_complete') {
              const tokens = chunk.tokenUsage?.totalTokens || 0;
              setStreamActivities((prev) => applyStreamActivity(prev, {
                eventType: 'status',
                stage: `iteration-${chunk.iteration}-done`,
                message: `Iteration ${chunk.iteration} complete (${tokens} tokens, ${chunk.durationMs}ms)`,
                timestamp: chunk.timestamp,
              }));
            }

            if (chunk.eventType === 'tool_calls' && chunk.toolCalls) {
              const newToolCalls: ToolCallDisplay[] = chunk.toolCalls.map((tc) => ({
                id: tc.Id,
                name: tc.Name,
                arguments: tc.Arguments,
                status: 'running' as const,
              }));
              setPendingToolCalls(newToolCalls);
              setMessages((prev) => {
                const updated = [...prev];
                const lastIndex = updated.length - 1;
                if (lastIndex >= 0 && updated[lastIndex].isStreaming) {
                  updated[lastIndex] = {
                    ...updated[lastIndex],
                    toolCalls: [...(updated[lastIndex].toolCalls || []), ...newToolCalls],
                  };
                }
                return updated;
              });
              setStreamActivities((prev) => applyStreamActivity(prev, {
                eventType: 'status',
                stage: 'tool-execution',
                message: `Executing ${chunk.toolCalls.length} tool(s): ${chunk.toolCalls.map((tc) => tc.Name).join(', ')}`,
                timestamp: chunk.timestamp,
              }));
            }

            if (chunk.eventType === 'tool_result') {
              setMessages((prev) => {
                const updated = [...prev];
                const lastIndex = updated.length - 1;
                if (lastIndex >= 0 && updated[lastIndex].isStreaming && updated[lastIndex].toolCalls) {
                  const toolCalls = updated[lastIndex].toolCalls!.map((tc) =>
                    tc.id === chunk.toolCallId
                      ? {
                          ...tc,
                          status: chunk.success ? 'success' as const : 'error' as const,
                          result: chunk.output,
                          error: chunk.error,
                          durationMs: chunk.durationMs,
                        }
                      : tc
                  );
                  updated[lastIndex] = { ...updated[lastIndex], toolCalls };
                }
                return updated;
              });
            }

            if (chunk.eventType === 'complete' && chunk.telemetry) {
              setTelemetrySummary(chunk.telemetry);
            }

            if (chunk.eventType === 'status' || chunk.eventType === 'heartbeat' || chunk.eventType === 'error') {
              setStreamActivities((prev) => applyStreamActivity(prev, chunk));
              if (chunk.eventType === 'error') {
                const errorText = chunk.message || chunk.error;
                if (errorText) setError(errorText);
              }
            }

            if ((chunk.contentDelta || chunk.thinkingDelta) && activeConversationIdRef.current === conversationId) {
              setMessages((prev) => {
                const updated = [...prev];
                const lastIndex = updated.length - 1;
                if (lastIndex >= 0 && updated[lastIndex].isStreaming) {
                  updated[lastIndex] = {
                    ...updated[lastIndex],
                    ...(chunk.contentDelta ? { content: `${updated[lastIndex].content}${chunk.contentDelta}` } : {}),
                    ...(chunk.thinkingDelta ? { thinkingContent: `${updated[lastIndex].thinkingContent ?? ''}${chunk.thinkingDelta}` } : {}),
                  };
                }
                return updated;
              });
            }
          } catch {
            // Ignore parse errors in the SSE stream.
          }
        }
      }

      await loadConversations();
      if (activeConversationIdRef.current === conversationId) {
        await loadConversation(conversationId);
      }
    } catch (err) {
      if (err instanceof Error && err.name === 'AbortError') {
        setStreamActivities((prev) => applyStreamActivity(prev, {
          eventType: 'status',
          stage: 'stopped',
          message: 'Generation stopped.',
          timestamp: new Date().toISOString(),
          providerId: streamingProviderId || undefined,
        }));
      } else {
        setError(err instanceof Error ? err.message : 'Failed to send message');
        if (activeConversationIdRef.current === conversationId) {
          setMessages((prev) => prev.filter((m) => !m.messageId.startsWith('temp-')));
        }
      }
    } finally {
      setIsStreaming(false);
      setStreamingProviderId(null);
      setCurrentIteration(0);
      setPendingToolCalls([]);
      abortControllerRef.current = null;
    }
  }

  function handleStopStreaming() {
    abortControllerRef.current?.abort();
    setIsStreaming(false);
    setStreamingProviderId(null);
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      void sendMessage();
    }
  }

  function handleNewConversation() {
    setActiveConversationId(null);
    setMessages([]);
    setError(null);
    setStreamActivities([]);
  }

  function formatTime(dateString?: string) {
    if (!dateString) return '';
    const date = new Date(dateString);
    return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }

  function formatDate(dateString?: string) {
    if (!dateString) return '';
    const date = new Date(dateString);
    const now = new Date();
    const isToday = date.toDateString() === now.toDateString();
    if (isToday) return 'Today';
    const yesterday = new Date(now);
    yesterday.setDate(yesterday.getDate() - 1);
    if (date.toDateString() === yesterday.toDateString()) return 'Yesterday';
    return date.toLocaleDateString([], { month: 'short', day: 'numeric' });
  }

  const currentActivity = streamActivities[streamActivities.length - 1] ?? null;
  const showStreamingActivity = isStreaming && currentActivity;
  const contextPreview = buildContextPreview(messages, inputValue);

  return (
    <section className="chat-layout">
      <aside className="chat-sidebar">
        <div className="chat-sidebar-header">
          <h3>Conversations</h3>
          <button
            type="button"
            className="button button-small"
            onClick={handleNewConversation}
          >
            New
          </button>
        </div>
        {conversationsLoading ? (
          <p className="chat-sidebar-loading">Loading...</p>
        ) : conversations.length === 0 ? (
          <p className="chat-sidebar-empty">No conversations yet</p>
        ) : (
          <ul className="chat-conversation-list">
            {conversations.map((conv) => (
              <li
                key={conv.conversationId}
                className={conv.conversationId === activeConversationId ? 'active' : ''}
              >
                <button
                  type="button"
                  className="chat-conversation-item"
                  onClick={() => void loadConversation(conv.conversationId)}
                >
                  <span className="chat-conversation-title">
                    {conv.title || 'Untitled'}
                  </span>
                  <span className="chat-conversation-date">
                    {formatDate(conv.lastMessageAt || conv.createdAt)}
                  </span>
                </button>
                <button
                  type="button"
                  className="chat-conversation-archive"
                  onClick={(e) => {
                    e.stopPropagation();
                    void archiveConversation(conv.conversationId);
                  }}
                  title="Archive conversation"
                >
                  &times;
                </button>
              </li>
            ))}
          </ul>
        )}
      </aside>

      <div className="chat-main">
        <div className="chat-messages">
          {messagesLoading ? (
            <div className="chat-messages-loading">Loading messages...</div>
          ) : messages.length === 0 ? (
            <div className="chat-messages-empty">
              {hasConfiguredProviders ? (
                <>
                  <h2>Start a new conversation</h2>
                  <p>Send a message to begin chatting with the AI assistant.</p>
                </>
              ) : (
                <>
                  <h2>Configure a provider first</h2>
                  <p>Chat now runs against your own provider settings. Connect OpenAI Codex, OpenAI, Anthropic, or Ollama under Account before sending a message.</p>
                  <button
                    type="button"
                    className="button button-primary"
                    onClick={onOpenProviderSettings}
                  >
                    Open Provider Settings
                  </button>
                </>
              )}
            </div>
          ) : (
            messages.map((message) => (
              <div
                key={message.messageId}
                className={`chat-message chat-message-${message.role}`}
              >
                <div className="chat-message-header">
                  <span className="chat-message-role">
                    {message.role === 'user' ? 'You' : message.role === 'system' ? 'System' : 'Assistant'}
                  </span>
                  {message.providerId && (
                    <span className="chat-message-provider">{message.providerId}</span>
                  )}
                  {message.modelId && (
                    <span className="chat-message-provider">{message.modelId}</span>
                  )}
                  <span className="chat-message-time">{formatTime(message.createdAt)}</span>
                </div>
                {message.toolCalls && message.toolCalls.length > 0 && (
                  <div className="chat-tool-calls">
                    {message.toolCalls.map((tc) => (
                      <details key={tc.id} className={`chat-tool-call chat-tool-call-${tc.status}`}>
                        <summary>
                          <span className={`chat-tool-status chat-tool-status-${tc.status}`}>
                            {tc.status === 'running' ? '[...]' : tc.status === 'success' ? '[ok]' : tc.status === 'error' ? '[x]' : '[ ]'}
                          </span>
                          <span className="chat-tool-name">{tc.name}</span>
                          {tc.durationMs !== undefined && (
                            <span className="chat-tool-duration">{tc.durationMs}ms</span>
                          )}
                        </summary>
                        {tc.arguments && (
                          <div className="chat-tool-args">
                            <strong>Arguments:</strong>
                            <pre>{tc.arguments}</pre>
                          </div>
                        )}
                        {tc.result && (
                          <div className="chat-tool-result">
                            <strong>Result:</strong>
                            <pre>{tc.result.length > 500 ? tc.result.slice(0, 500) + '...' : tc.result}</pre>
                          </div>
                        )}
                        {tc.error && (
                          <div className="chat-tool-error">
                            <strong>Error:</strong> {tc.error}
                          </div>
                        )}
                      </details>
                    ))}
                  </div>
                )}
                {message.thinkingContent && (
                  <details className="chat-thinking">
                    <summary className="chat-thinking-summary">Reasoning</summary>
                    <div className="chat-thinking-content">{message.thinkingContent}</div>
                  </details>
                )}
                <div className="chat-message-content">
                  {message.content}
                  {message.isStreaming && <span className="chat-cursor" />}
                </div>
              </div>
            ))
          )}
          <div ref={messagesEndRef} />
        </div>

        {error && (
          <div className="chat-error">
            {error}
            <button type="button" onClick={() => setError(null)}>&times;</button>
          </div>
        )}

        {showStreamingActivity && (
          <div className="chat-activity-indicator">
            <span className="chat-activity-dot" />
            <div className="chat-activity-copy">
              <div className="chat-activity-current">
                {currentActivity.message}
              </div>
              <div className="chat-activity-trail">
                {streamActivities.map((activity) => (
                  <span
                    key={`${activity.stage}-${activity.message}`}
                    className={`chat-activity-step ${activity.stage === currentActivity.stage ? 'active' : ''}`}
                  >
                    {activity.message}
                  </span>
                ))}
              </div>
            </div>
          </div>
        )}

        {!hasConfiguredProviders ? (
          <div className="chat-provider-warning">
            Add a provider under Account to enable routing and responses.
          </div>
        ) : null}

        <div className="chat-input-container">
          <div className="chat-input-options">
            <label className="field">
              <span>Provider</span>
              <select
                value={selectedProviderId}
                onChange={(e) => void handleProviderSelectionChange(e.target.value)}
                disabled={isStreaming || !hasConfiguredProviders}
              >
                <option value="">Auto routing</option>
                {availableProviders.map((provider) => (
                  <option key={provider.providerId} value={provider.providerId}>
                    {provider.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>Model</span>
              <select
                value={selectedModelId}
                onChange={(e) => void handleModelSelectionChange(e.target.value)}
                disabled={isStreaming || !selectedProviderId || providerModels.length === 0}
              >
                <option value="">Provider default</option>
                {providerModels.map((model) => (
                  <option key={model.id} value={model.id}>
                    {model.name || model.id}
                  </option>
                ))}
              </select>
            </label>
            <label className="chat-toggle">
              <input
                type="checkbox"
                checked={agenticMode}
                onChange={(e) => setAgenticMode(e.target.checked)}
                disabled={isStreaming}
              />
              <span className="chat-toggle-label">Enable Tools (Agentic)</span>
            </label>
            {currentTraceId && (
              <span className="chat-trace-id" title="Trace ID for debugging">
                Trace: {currentTraceId.slice(0, 8)}...
              </span>
            )}
          </div>
          <div className="chat-context-preview">
            <details>
              <summary>
                Next request context: {contextPreview.compactedMessages} messages, about {contextPreview.estimatedTokens} tokens
              </summary>
              <div className="chat-context-preview-details">
                <div>Total turns queued: {contextPreview.totalMessages}</div>
                <div>Verbatim turns: {contextPreview.verbatimMessages}</div>
                <div>Summarized older turns: {contextPreview.summarizedMessages}</div>
                <div>Approx chars sent: {contextPreview.estimatedCharacters}</div>
                <div>Route: {formatModelSelection(selectedProviderId, selectedModelId)}</div>
                <div>Mode: {agenticMode ? 'Agentic' : 'Standard chat'}</div>
                {contextPreview.digest ? (
                  <pre className="chat-context-preview-digest">{contextPreview.digest}</pre>
                ) : (
                  <div>No summary digest yet. The next request is still within the verbatim window.</div>
                )}
              </div>
            </details>
          </div>
          <textarea
            placeholder={hasConfiguredProviders ? (agenticMode ? 'Ask me to do something with tools (e.g., "List files in microsoft/vscode repo")...' : 'Type a message or use /model <provider> <model>...') : 'Configure a provider in Account before chatting.'}
            value={inputValue}
            onChange={(e) => setInputValue(e.target.value)}
            onKeyDown={handleKeyDown}
            rows={3}
          />
          <div className="chat-input-actions">
            {isStreaming ? (
              <button
                type="button"
                className="button button-danger"
                onClick={handleStopStreaming}
              >
                Stop
              </button>
            ) : (
              <button
                type="button"
                className="button button-primary"
                onClick={() => void sendMessage()}
                disabled={!hasConfiguredProviders || !inputValue.trim()}
              >
                Send
              </button>
            )}
          </div>
        </div>
        {telemetrySummary && (
          <div className="chat-telemetry">
            <details>
              <summary>Telemetry: {telemetrySummary.tokenUsage.totalTokens} tokens, {telemetrySummary.llmCallCount} LLM calls, {telemetrySummary.toolCallCount} tool calls</summary>
              <div className="chat-telemetry-details">
                <div>Total: {telemetrySummary.totalDurationMs}ms</div>
                <div>LLM: {telemetrySummary.llmDurationMs}ms</div>
                <div>Tools: {telemetrySummary.toolDurationMs}ms</div>
                <div>Prompt tokens: {telemetrySummary.tokenUsage.totalPromptTokens}</div>
                <div>Completion tokens: {telemetrySummary.tokenUsage.totalCompletionTokens}</div>
              </div>
            </details>
          </div>
        )}
      </div>
    </section>
  );
}

