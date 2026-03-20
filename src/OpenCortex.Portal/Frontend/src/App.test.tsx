import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';

import App from './App';

type StoredAuthSession = {
  idToken: string;
  refreshToken: string;
  email: string;
  displayName: string;
  expiresAt: string;
};

type MemoryDocument = {
  managedDocumentId: string;
  title: string;
  slug: string;
  canonicalPath: string;
  status: string;
  updatedAt: string;
  wordCount: number;
  content: string;
  frontmatter: Record<string, unknown>;
};

const storageKey = 'opencortex.portal.auth_session';

if (!HTMLElement.prototype.scrollIntoView) {
  HTMLElement.prototype.scrollIntoView = () => {};
}

function buildSession(): StoredAuthSession {
  return {
    idToken: 'token-1',
    refreshToken: 'refresh-1',
    email: 'stephen@example.com',
    displayName: 'Stephen',
    expiresAt: '2099-03-19T00:00:00Z',
  };
}

function jsonResponse(payload: unknown, status = 200) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: {
      'Content-Type': 'application/json',
    },
  });
}

function createPortalFetchMock(initialMemories: MemoryDocument[]) {
  let memories = [...initialMemories];
  const requests: Array<{ url: string; method: string }> = [];

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const method = String(init?.method || 'GET').toUpperCase();
    requests.push({ url, method });

    if (url === '/portal-config') {
      return jsonResponse({
        apiBaseUrlConfigured: true,
        hostedAuthConfigured: false,
        notes: [],
      });
    }

    if (url === '/portal-api/tenant/me') {
      return jsonResponse({
        displayName: 'Stephen',
        email: 'stephen@example.com',
        role: 'owner',
        customerId: 'cust-1',
        customerSlug: 'demo',
        customerName: 'Demo Customer',
        brainId: 'brain-1',
        brainName: 'Daily Brain',
        planId: 'pro',
      });
    }

    if (url === '/portal-api/tenant/billing/plan') {
      return jsonResponse({
        planId: 'pro',
        subscriptionStatus: 'active',
        activeDocuments: 2,
        maxDocuments: 100,
        mcpQueriesUsed: 0,
        mcpQueriesPerMonth: 1000,
        mcpWrite: true,
      });
    }

    if (url === '/portal-api/tenant/brains') {
      return jsonResponse({
        brains: [
          {
            brainId: 'brain-1',
            name: 'Daily Brain',
            mode: 'managed-content',
            status: 'active',
          },
        ],
      });
    }

    if (url === '/portal-api/tenant/me/memory-brain') {
      return jsonResponse({
        configuredMemoryBrainId: 'brain-1',
        effectiveMemoryBrainId: 'brain-1',
        needsConfiguration: false,
      });
    }

    if (url === '/portal-api/tenant/tokens') {
      return jsonResponse({ tokens: [] });
    }

    if (url === '/portal-api/api/providers/config/available') {
      return jsonResponse({ providers: [] });
    }

    if (url === '/portal-api/api/providers/config/') {
      return jsonResponse({ count: 0, providers: [] });
    }

    if (url === '/portal-api/tenant/brains/brain-1/documents?limit=200&pathPrefix=memories%2F') {
      return jsonResponse({
        documents: memories.map((memory) => ({
          managedDocumentId: memory.managedDocumentId,
          title: memory.title,
          slug: memory.slug,
          canonicalPath: memory.canonicalPath,
          status: memory.status,
          updatedAt: memory.updatedAt,
          wordCount: memory.wordCount,
        })),
      });
    }

    const detailMatch = url.match(/^\/portal-api\/tenant\/brains\/brain-1\/documents\/(mem-[^/?]+)$/);
    if (detailMatch && method === 'GET') {
      const memory = memories.find((candidate) => candidate.managedDocumentId === detailMatch[1]);
      if (!memory) {
        return jsonResponse({ title: 'Not found', detail: 'Memory not found.' }, 404);
      }

      return jsonResponse(memory);
    }

    if (detailMatch && method === 'DELETE') {
      memories = memories.filter((candidate) => candidate.managedDocumentId !== detailMatch[1]);
      return jsonResponse({ deleted: true });
    }

    throw new Error(`Unhandled fetch request: ${method} ${url}`);
  });

  return { fetchMock, requests };
}

describe('App memories integration', () => {
  beforeEach(() => {
    window.localStorage.setItem(storageKey, JSON.stringify(buildSession()));
    window.history.replaceState(null, '', '/app#memories');
  });

  afterEach(() => {
    cleanup();
    window.localStorage.clear();
    window.history.replaceState(null, '', '/app');
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('loads the memories list and selected memory detail through App fetch wiring', async () => {
    const { fetchMock, requests } = createPortalFetchMock([
      {
        managedDocumentId: 'mem-1',
        title: '[decision] Use OQL path prefix on day one',
        slug: 'memories/decision/day-one-oql',
        canonicalPath: 'memories/decision/day-one-oql.md',
        status: 'published',
        updatedAt: '2026-03-19T12:00:00Z',
        wordCount: 12,
        content: 'Use OQL path prefix on day one.',
        frontmatter: { category: 'decision', confidence: 'high' },
      },
      {
        managedDocumentId: 'mem-2',
        title: '[preference] Prefer concise summaries',
        slug: 'memories/preference/concise-summaries',
        canonicalPath: 'memories/preference/concise-summaries.md',
        status: 'published',
        updatedAt: '2026-03-19T13:00:00Z',
        wordCount: 6,
        content: 'Prefer concise summaries.',
        frontmatter: { category: 'preference', confidence: 'medium' },
      },
    ]);

    vi.stubGlobal('fetch', fetchMock);

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('Use OQL path prefix on day one.')).toBeTruthy();
    });


    expect(requests.some((request) => request.url === '/portal-api/tenant/brains/brain-1/documents?limit=200&pathPrefix=memories%2F')).toBe(true);
    expect(requests.some((request) => request.url === '/portal-api/tenant/brains/brain-1/documents/mem-1')).toBe(true);
  });

  it('forgets the selected memory and refreshes the list through App fetch wiring', async () => {
    const { fetchMock, requests } = createPortalFetchMock([
      {
        managedDocumentId: 'mem-1',
        title: '[decision] Use OQL path prefix on day one',
        slug: 'memories/decision/day-one-oql',
        canonicalPath: 'memories/decision/day-one-oql.md',
        status: 'published',
        updatedAt: '2026-03-19T12:00:00Z',
        wordCount: 12,
        content: 'Use OQL path prefix on day one.',
        frontmatter: { category: 'decision', confidence: 'high' },
      },
      {
        managedDocumentId: 'mem-2',
        title: '[preference] Prefer concise summaries',
        slug: 'memories/preference/concise-summaries',
        canonicalPath: 'memories/preference/concise-summaries.md',
        status: 'published',
        updatedAt: '2026-03-19T13:00:00Z',
        wordCount: 6,
        content: 'Prefer concise summaries.',
        frontmatter: { category: 'preference', confidence: 'medium' },
      },
    ]);

    vi.stubGlobal('fetch', fetchMock);
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('Use OQL path prefix on day one.')).toBeTruthy();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Forget Memory' }));

    await waitFor(() => {
      expect(screen.getByText(/\[preference\] Prefer concise summaries/i)).toBeTruthy();
    });

    expect(requests.some((request) => request.method === 'DELETE' && request.url === '/portal-api/tenant/brains/brain-1/documents/mem-1')).toBe(true);
  });
});






