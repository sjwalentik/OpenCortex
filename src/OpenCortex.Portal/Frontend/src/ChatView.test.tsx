import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';

import { ChatView } from './ChatView';

if (!HTMLElement.prototype.scrollIntoView) {
  HTMLElement.prototype.scrollIntoView = () => {};
}

function jsonResponse(payload: unknown, status = 200) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: {
      'Content-Type': 'application/json',
    },
  });
}

describe('ChatView conversation titles', () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('shows a New Conversation fallback when a conversation has no saved title', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);

      if (url === '/portal-api/tenant/conversations?limit=50') {
        return jsonResponse({
          conversations: [
            {
              conversationId: 'conv-1',
              title: null,
              createdAt: '2026-03-25T12:00:00Z',
              status: 'active',
            },
          ],
        });
      }

      throw new Error(`Unhandled request: ${url}`);
    });

    vi.stubGlobal('fetch', fetchMock);

    render(
      <ChatView
        authSession={{ idToken: 'token-1' }}
        activeBrainId="brain-1"
        hasConfiguredProviders={false}
        onOpenProviderSettings={() => {}}
        onRefreshSession={async () => 'token-1'}
      />,
    );

    await waitFor(() => {
      expect(screen.getByText('New Conversation')).toBeTruthy();
    });
  });

  it('renames a conversation through the existing patch endpoint', async () => {
    let title = 'Old title';
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = String(init?.method || 'GET').toUpperCase();

      if (url === '/portal-api/tenant/conversations?limit=50' && method === 'GET') {
        return jsonResponse({
          conversations: [
            {
              conversationId: 'conv-1',
              title,
              createdAt: '2026-03-25T12:00:00Z',
              status: 'active',
            },
          ],
        });
      }

      if (url === '/portal-api/tenant/conversations/conv-1' && method === 'PATCH') {
        const body = JSON.parse(String(init?.body || '{}'));
        title = body.title;
        return jsonResponse({ message: 'Conversation updated.' });
      }

      throw new Error(`Unhandled request: ${method} ${url}`);
    });

    vi.stubGlobal('fetch', fetchMock);

    render(
      <ChatView
        authSession={{ idToken: 'token-1' }}
        activeBrainId="brain-1"
        hasConfiguredProviders={false}
        onOpenProviderSettings={() => {}}
        onRefreshSession={async () => 'token-1'}
      />,
    );

    await waitFor(() => {
      expect(screen.getByText('Old title')).toBeTruthy();
    });

    fireEvent.click(screen.getByTitle('Rename conversation'));

    const input = screen.getByPlaceholderText('Conversation title');
    fireEvent.change(input, { target: { value: 'Release planning notes' } });
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(screen.getByText('Release planning notes')).toBeTruthy();
    });

    expect(fetchMock).toHaveBeenCalledWith(
      '/portal-api/tenant/conversations/conv-1',
      expect.objectContaining({
        method: 'PATCH',
        body: JSON.stringify({ title: 'Release planning notes' }),
      }),
    );
  });

  it('refreshes the sidebar title from conversation detail after the first exchange', async () => {
    const encoder = new TextEncoder();
    let listTitle: string | null = null;

    const streamResponse = new Response(new ReadableStream({
      start(controller) {
        controller.enqueue(encoder.encode('data: {"contentDelta":"Generated answer"}\n'));
        controller.close();
      },
    }));

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = String(init?.method || 'GET').toUpperCase();

      if (url === '/portal-api/tenant/conversations?limit=50' && method === 'GET') {
        return jsonResponse({
          conversations: [
            {
              conversationId: 'conv-1',
              title: listTitle,
              createdAt: '2026-03-25T12:00:00Z',
              lastMessageAt: '2026-03-25T12:00:00Z',
              status: 'active',
            },
          ],
        });
      }

      if (url === '/portal-api/api/chat/providers' && method === 'GET') {
        return jsonResponse({
          providers: [{ providerId: 'provider-1', name: 'Provider 1' }],
        });
      }

      if (url === '/portal-api/tenant/conversations' && method === 'POST') {
        return jsonResponse({
          conversationId: 'conv-1',
          title: null,
          createdAt: '2026-03-25T12:00:00Z',
          status: 'active',
        }, 201);
      }

      if (url === '/portal-api/api/chat/completions/stream' && method === 'POST') {
        listTitle = 'Auto generated title';
        return streamResponse;
      }

      if (url === '/portal-api/tenant/conversations/conv-1?messageLimit=100' && method === 'GET') {
        return jsonResponse({
          conversationId: 'conv-1',
          title: 'Auto generated title',
          createdAt: '2026-03-25T12:00:00Z',
          lastMessageAt: '2026-03-25T12:01:00Z',
          status: 'active',
          providerId: null,
          modelId: null,
          messages: [
            {
              messageId: 'msg-user-1',
              role: 'user',
              content: 'Help me plan a release',
              createdAt: '2026-03-25T12:00:30Z',
            },
            {
              messageId: 'msg-assistant-1',
              role: 'assistant',
              content: 'Generated answer',
              createdAt: '2026-03-25T12:01:00Z',
            },
          ],
        });
      }

      throw new Error(`Unhandled request: ${method} ${url}`);
    });

    vi.stubGlobal('fetch', fetchMock);

    render(
      <ChatView
        authSession={{ idToken: 'token-1' }}
        activeBrainId="brain-1"
        hasConfiguredProviders={true}
        onOpenProviderSettings={() => {}}
        onRefreshSession={async () => 'token-1'}
      />,
    );

    await waitFor(() => {
      expect(screen.getByText('New Conversation')).toBeTruthy();
    });

    fireEvent.change(screen.getByPlaceholderText('Type a message or use /model <provider> <model>...'), {
      target: { value: 'Help me plan a release' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Send' }));

    await waitFor(() => {
      expect(screen.getByText('Auto generated title')).toBeTruthy();
    });
  });
});
