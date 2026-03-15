import { useCallback, useEffect, useRef, useState } from 'react';

export type ChatMessage = {
  messageId: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  providerId?: string;
  modelId?: string;
  latencyMs?: number;
  createdAt?: string;
  isStreaming?: boolean;
};

export type Conversation = {
  conversationId: string;
  title?: string;
  createdAt: string;
  lastMessageAt?: string;
  status: string;
};

export type ChatViewProps = {
  authSession: { idToken: string } | null;
  activeBrainId: string;
  hasConfiguredProviders: boolean;
  onOpenProviderSettings: () => void;
  onRefreshSession: () => Promise<string | null>;
};

type StreamChunk = {
  eventType?: 'content' | 'status' | 'heartbeat' | 'error';
  stage?: string;
  message?: string;
  timestamp?: string;
  contentDelta?: string;
  isComplete?: boolean;
  finishReason?: string;
  providerId?: string;
  routing?: {
    category?: string;
    confidence?: number;
  };
};

type StreamActivity = {
  stage: string;
  message: string;
  providerId?: string;
  timestamp?: string;
  isHeartbeat?: boolean;
};

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

  async function loadConversation(conversationId: string) {
    setMessagesLoading(true);
    setError(null);
    try {
      const response = await portalFetch(`/portal-api/tenant/conversations/${conversationId}?messageLimit=100`);
      const data = await response.json();
      setMessages(data.messages || []);
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
    };

    setMessages((prev) => [...prev, userMessage, assistantMessage]);
    setInputValue('');
    setIsStreaming(true);
    setStreamActivities([{ stage: 'queued', message: 'Sending request...', timestamp: new Date().toISOString() }]);
    setError(null);

    try {
      abortControllerRef.current = new AbortController();

      const response = await fetchWithAuth('/portal-api/api/chat/completions/stream', {
        method: 'POST',
        body: JSON.stringify({
          messages: [...messages, userMessage].map((m) => ({
            role: m.role,
            content: m.content,
          })),
          conversationId,
          brainId: activeBrainId || null,
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

            if (chunk.eventType === 'status' || chunk.eventType === 'heartbeat' || chunk.eventType === 'error') {
              setStreamActivities((prev) => applyStreamActivity(prev, chunk));
              if (chunk.eventType === 'error' && chunk.message) {
                setError(chunk.message);
              }
            }

            if (chunk.contentDelta && activeConversationIdRef.current === conversationId) {
              setMessages((prev) => {
                const updated = [...prev];
                const lastIndex = updated.length - 1;
                if (lastIndex >= 0 && updated[lastIndex].isStreaming) {
                  updated[lastIndex] = {
                    ...updated[lastIndex],
                    content: `${updated[lastIndex].content}${chunk.contentDelta}`,
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
  const showStreamingActivity = isStreaming && currentActivity && activeConversationId === streamingConversationId;

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
                  <p>Chat now runs against your own provider settings. Connect OpenAI, Anthropic, or Ollama under Account before sending a message.</p>
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
                    {message.role === 'user' ? 'You' : 'Assistant'}
                  </span>
                  {message.providerId && (
                    <span className="chat-message-provider">{message.providerId}</span>
                  )}
                  <span className="chat-message-time">{formatTime(message.createdAt)}</span>
                </div>
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
          <textarea
            className="chat-input"
            value={inputValue}
            onChange={(e) => setInputValue(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={hasConfiguredProviders ? 'Type a message...' : 'Configure a provider in Account before chatting.'}
            disabled={isStreaming || !hasConfiguredProviders}
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
      </div>
    </section>
  );
}


