import { useEffect, useMemo, useRef, useState } from 'react';

type PortalView = 'signin' | 'documents' | 'account' | 'usage' | 'tools';

type PortalConfig = {
  apiBaseUrlConfigured: boolean;
  hostedAuthConfigured: boolean;
  firebaseProjectId?: string;
  mcpBaseUrl?: string;
  operatorConsoleUrl?: string;
  authMode?: string;
  notes?: string[];
};

type StoredAuthSession = {
  idToken: string;
  refreshToken: string;
  email: string;
  displayName: string;
  expiresAt: string;
};

type PortalContext = {
  displayName: string;
  email: string;
  role: string;
  customerId: string;
  customerSlug: string;
  customerName: string;
  brainId: string;
  brainName: string;
  planId: string;
};

type PortalBilling = {
  planId: string;
  subscriptionStatus: string;
  activeDocuments: number;
  maxDocuments: number;
  mcpQueriesUsed: number;
  mcpQueriesPerMonth: number;
  mcpWrite: boolean;
};

type BrainSummary = {
  brainId: string;
  name: string;
  mode: string;
  status: string;
};

type TokenSummary = {
  apiTokenId: string;
  name: string;
  tokenPrefix: string;
  scopes: string[];
  createdAt?: string | null;
  lastUsedAt?: string | null;
  expiresAt?: string | null;
  revokedAt?: string | null;
};

type CreatedTokenState = {
  token: string;
  meta: string;
};

type DocumentSummary = {
  managedDocumentId: string;
  title?: string;
  slug?: string;
  canonicalPath?: string;
  status?: string;
  updatedAt?: string;
  wordCount?: number;
};

type DocumentDetail = DocumentSummary & {
  content?: string;
  frontmatter?: Record<string, unknown> | null;
};

type DocumentListResponse = {
  documents?: DocumentSummary[];
};

type DocumentDraft = {
  title: string;
  slug: string;
  status: string;
  frontmatterText: string;
  content: string;
};

type DocumentSaveState = 'idle' | 'saving' | 'error' | 'warn' | 'info';

type DocumentVersionSummary = {
  managedDocumentVersionId: string;
  createdAt?: string;
  snapshotKind?: string;
  status?: string;
  wordCount?: number;
  snapshotBy?: string;
};

type DocumentVersionDetail = DocumentVersionSummary & {
  content?: string;
};

type DocumentVersionListResponse = {
  versions?: DocumentVersionSummary[];
};
type ToolQuerySummary = {
  totalResults: number;
  resultsWithKeywordSignal: number;
  resultsWithSemanticSignal: number;
  resultsWithGraphSignal: number;
};

type ToolQueryResultItem = {
  brainId: string;
  documentId?: string;
  canonicalPath?: string;
  title?: string;
  snippet?: string;
};

type ToolQueryResponse = {
  summary?: ToolQuerySummary;
  results?: ToolQueryResultItem[];
};

type ToolFetchedDocumentState = {
  status: 'loading' | 'ready' | 'error';
  document?: DocumentDetail;
  message?: string;
};

type IndexingRunSummary = {
  startedAt?: string;
  status?: string;
  triggerType?: string;
  documentsSeen?: number;
  documentsIndexed?: number;
  documentsFailed?: number;
  errorSummary?: string;
};

type IndexingRunResponse = {
  runs?: IndexingRunSummary[];
};

type ViewDefinition = {
  id: PortalView;
  title: string;
  lead: string;
  bullets: string[];
};

type DocumentGroup = {
  directoryPath: string;
  depth: number;
  label: string;
  documents: DocumentSummary[];
};

const storageKey = 'opencortex.portal.auth_session';
const sessionRefreshSkewMs = 5 * 60 * 1000;

const viewDefinitions: Record<PortalView, ViewDefinition> = {
  signin: {
    id: 'signin',
    title: 'Sign In',
    lead: 'Authenticate into the hosted customer workspace.',
    bullets: [
      'Browser auth stays on the hosted portal host.',
      'Firebase email/password and Google sign-in remain supported.',
      'React uses the same browser session contract as the current portal.'
    ]
  },
  documents: {
    id: 'documents',
    title: 'Documents',
    lead: 'Author managed-content documents with drafting, rendering, and version history in one workspace.',
    bullets: [
      'Document CRUD remains the primary workflow.',
      'Drafting, rendering, and version history stay together.',
      'Tiptap comes after current behavior survives intact.'
    ]
  },
  account: {
    id: 'account',
    title: 'Account',
    lead: 'Manage browser session posture, tenant settings, and MCP personal tokens.',
    bullets: [
      'Session state remains separate from document authoring.',
      'Token issuance remains a dedicated surface.',
      'Write posture still reflects the effective workspace plan.'
    ]
  },
  usage: {
    id: 'usage',
    title: 'Usage',
    lead: 'Inspect workspace context, quotas, and MCP usage from a dedicated operational view.',
    bullets: [
      'Usage posture stays visible from a dedicated operational view.',
      'Default brain context remains easy to inspect.',
      'Quota visibility is not sacrificed for the rewrite.'
    ]
  },
  tools: {
    id: 'tools',
    title: 'Tools',
    lead: 'Smoke-test retrieval, copy MCP setup, and inspect indexing activity for the active brain.',
    bullets: [
      'OQL smoke testing remains in the customer-safe portal.',
      'MCP setup and tool manifest access stay nearby.',
      'Full document fetch and indexing visibility stay nearby.'
    ]
  }
};

const orderedViews: PortalView[] = ['signin', 'documents', 'account', 'usage', 'tools'];

function App() {
  const [config, setConfig] = useState<PortalConfig | null>(null);
  const [configError, setConfigError] = useState<string | null>(null);
  const [authSession, setAuthSession] = useState<StoredAuthSession | null>(() => loadStoredAuthSession());
  const [context, setContext] = useState<PortalContext | null>(null);
  const [billing, setBilling] = useState<PortalBilling | null>(null);
  const [brains, setBrains] = useState<BrainSummary[]>([]);
  const [tokens, setTokens] = useState<TokenSummary[]>([]);
  const [activeView, setActiveView] = useState<PortalView>(resolveViewFromHash(window.location.hash, loadStoredAuthSession()));
  const [workspaceError, setWorkspaceError] = useState<string | null>(null);
  const [workspaceLoading, setWorkspaceLoading] = useState(false);
  const [refreshNonce, setRefreshNonce] = useState(0);
  const [activeBrainId, setActiveBrainId] = useState('');
  const [documentFilter, setDocumentFilter] = useState('');
  const [documents, setDocuments] = useState<DocumentSummary[]>([]);
  const [documentsLoading, setDocumentsLoading] = useState(false);
  const [documentsError, setDocumentsError] = useState<string | null>(null);
  const [selectedDocumentId, setSelectedDocumentId] = useState<string | null>(null);
  const [selectedDocument, setSelectedDocument] = useState<DocumentDetail | null>(null);
  const [documentLoading, setDocumentLoading] = useState(false);
  const [documentError, setDocumentError] = useState<string | null>(null);
  const [documentRefreshNonce, setDocumentRefreshNonce] = useState(0);
  const [documentDetailNonce, setDocumentDetailNonce] = useState(0);
  const [isCreatingDocument, setIsCreatingDocument] = useState(false);
  const [documentDraft, setDocumentDraft] = useState<DocumentDraft>(buildEmptyDocumentDraft());
  const [documentSaveState, setDocumentSaveState] = useState<DocumentSaveState>('idle');
  const [documentSaveMessage, setDocumentSaveMessage] = useState('Make a change to enable save.');
  const [documentVersions, setDocumentVersions] = useState<DocumentVersionSummary[]>([]);
  const [versionsLoading, setVersionsLoading] = useState(false);
  const [versionsError, setVersionsError] = useState<string | null>(null);
  const [versionRefreshNonce, setVersionRefreshNonce] = useState(0);
  const [selectedVersionId, setSelectedVersionId] = useState<string | null>(null);
  const [selectedVersion, setSelectedVersion] = useState<DocumentVersionDetail | null>(null);
  const [versionLoading, setVersionLoading] = useState(false);
  const [versionError, setVersionError] = useState<string | null>(null);
    const [tokenNameInput, setTokenNameInput] = useState('');
  const [tokenExpiresAtInput, setTokenExpiresAtInput] = useState('');
  const [requestWriteScope, setRequestWriteScope] = useState(false);
  const [createdToken, setCreatedToken] = useState<CreatedTokenState | null>(null);
  const [accountActionMessage, setAccountActionMessage] = useState<string | null>(null);
  const [toolQueryBrainId, setToolQueryBrainId] = useState('');
  const [toolQuerySearch, setToolQuerySearch] = useState('identity');
  const [toolQueryRank, setToolQueryRank] = useState('hybrid');
  const [toolQueryWhere, setToolQueryWhere] = useState('');
  const [toolQueryLimit, setToolQueryLimit] = useState('5');
  const [toolQueryResults, setToolQueryResults] = useState<ToolQueryResponse | null>(null);
  const [toolFetchedDocuments, setToolFetchedDocuments] = useState<Record<string, ToolFetchedDocumentState>>({});
  const [toolActionMessage, setToolActionMessage] = useState<string | null>(null);
  const [indexingRuns, setIndexingRuns] = useState<IndexingRunSummary[]>([]);
  const [indexingLoading, setIndexingLoading] = useState(false);
  const [indexingError, setIndexingError] = useState<string | null>(null);
  const [indexingRefreshNonce, setIndexingRefreshNonce] = useState(0);

  useEffect(() => {
    let cancelled = false;

    async function loadConfig() {
      try {
        const response = await fetch('/portal-config', {
          headers: {
            Accept: 'application/json'
          }
        });

        if (!response.ok) {
          throw new Error(`Portal config request failed with ${response.status}.`);
        }

        const payload = (await response.json()) as PortalConfig;
        if (!cancelled) {
          setConfig(payload);
          setConfigError(null);
        }
      } catch (error) {
        if (!cancelled) {
          setConfigError(error instanceof Error ? error.message : 'Failed to load portal config.');
        }
      }
    }

    void loadConfig();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    const syncFromLocation = () => {
      const resolved = resolveViewFromHash(window.location.hash, authSession);
      setActiveView(resolved);

      const nextHash = `#${resolved}`;
      if (window.location.hash !== nextHash) {
        window.history.replaceState(null, '', nextHash);
      }
    };

    syncFromLocation();
    window.addEventListener('hashchange', syncFromLocation);

    return () => {
      window.removeEventListener('hashchange', syncFromLocation);
    };
  }, [authSession]);

  useEffect(() => {
    const onStorage = () => {
      setAuthSession(loadStoredAuthSession());
    };

    window.addEventListener('storage', onStorage);
    return () => {
      window.removeEventListener('storage', onStorage);
    };
  }, []);

  useEffect(() => {
    if (!authSession) {
      setContext(null);
      setBilling(null);
      setBrains([]);
      setTokens([]);
      setWorkspaceError(null);
      setActiveBrainId('');
      setCreatedToken(null);
      setAccountActionMessage(null);
      setToolQueryBrainId('');
      setToolQueryResults(null);
      setToolFetchedDocuments({});
      setToolActionMessage(null);
      setIndexingRuns([]);
      setIndexingLoading(false);
      setIndexingError(null);
      return;
    }

    let cancelled = false;

    async function refreshWorkspace() {
      setWorkspaceLoading(true);
      setWorkspaceError(null);

      try {
        const session = await ensureValidSession(authSession);
        if (cancelled) {
          return;
        }

        if (session !== authSession) {
          setAuthSession(session);
        }

        const [workspaceContext, workspaceBilling, workspaceBrains, workspaceTokens] = await Promise.all([
          portalFetch('/portal-api/tenant/me', session.idToken),
          portalFetch('/portal-api/tenant/billing/plan', session.idToken),
          portalFetch('/portal-api/tenant/brains', session.idToken),
          portalFetch('/portal-api/tenant/tokens', session.idToken)
        ]);

        if (cancelled) {
          return;
        }

        const nextBrains = (((workspaceBrains as { brains?: BrainSummary[] }).brains) || [])
          .filter((brain) => String(brain.mode || '').toLowerCase() === 'managed-content'
            && String(brain.status || '').toLowerCase() !== 'retired');
        const nextTokens = (((workspaceTokens as { tokens?: TokenSummary[] }).tokens) || []);

        setContext(workspaceContext as PortalContext);
        setBilling(workspaceBilling as PortalBilling);
        setBrains(nextBrains);
        setTokens(nextTokens);
      } catch (error) {
        if (cancelled) {
          return;
        }

        if (error instanceof Error && /sign in before using the portal/i.test(error.message)) {
          clearStoredAuthSession();
          setAuthSession(null);
          setContext(null);
          setBilling(null);
          setBrains([]);
          setTokens([]);
        }

        setWorkspaceError(error instanceof Error ? error.message : 'Failed to load workspace.');
      } finally {
        if (!cancelled) {
          setWorkspaceLoading(false);
        }
      }
    }

    void refreshWorkspace();

    return () => {
      cancelled = true;
    };
  }, [authSession, refreshNonce]);

  useEffect(() => {
    if (!authSession || brains.length === 0) {
      setActiveBrainId('');
      return;
    }

    setActiveBrainId((current) => selectActiveBrainId(current, brains, context));
  }, [authSession, brains, context]);

  useEffect(() => {
    if (!authSession || !activeBrainId) {
      setDocuments([]);
      setDocumentsError(null);
      setDocumentsLoading(false);
      setSelectedDocumentId(null);
      setSelectedDocument(null);
      setDocumentError(null);
      setDocumentLoading(false);
      setIsCreatingDocument(false);
      setDocumentDraft(buildEmptyDocumentDraft());
      setDocumentSaveState('idle');
      setDocumentSaveMessage('Make a change to enable save.');
      setDocumentVersions([]);
      setVersionsLoading(false);
      setVersionsError(null);
      setSelectedVersionId(null);
      setSelectedVersion(null);
      setVersionLoading(false);
      setVersionError(null);
      return;
    }

    let cancelled = false;

    async function loadDocuments() {
      setDocumentsLoading(true);
      setDocumentsError(null);

      try {
        const session = await ensureValidSession(authSession);
        if (cancelled) {
          return;
        }

        if (session !== authSession) {
          setAuthSession(session);
        }

        const response = (await portalFetch(
          `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents?limit=200`,
          session.idToken
        )) as DocumentListResponse;

        if (cancelled) {
          return;
        }

        const nextDocuments = Array.isArray(response.documents) ? response.documents : [];
        setDocuments(nextDocuments);

        setSelectedDocumentId((currentSelectedId) => {
          const preferredId = currentSelectedId && nextDocuments.some((document) => document.managedDocumentId === currentSelectedId)
            ? currentSelectedId
            : nextDocuments[0]?.managedDocumentId ?? null;

          if (!preferredId) {
            setSelectedDocument(null);
            setDocumentError(null);
          }

          return preferredId;
        });
      } catch (error) {
        if (cancelled) {
          return;
        }

        setDocuments([]);
        setSelectedDocumentId(null);
        setSelectedDocument(null);
        setDocumentsError(error instanceof Error ? error.message : 'Failed to load documents.');
      } finally {
        if (!cancelled) {
          setDocumentsLoading(false);
        }
      }
    }

    void loadDocuments();

    return () => {
      cancelled = true;
    };
  }, [authSession, activeBrainId, documentRefreshNonce]);

  useEffect(() => {
    if (!authSession || !activeBrainId || !selectedDocumentId) {
      setSelectedDocument(null);
      setDocumentError(null);
      setDocumentLoading(false);
      return;
    }

    let cancelled = false;

    async function loadDocument() {
      setDocumentLoading(true);
      setDocumentError(null);

      try {
        const session = await ensureValidSession(authSession);
        if (cancelled) {
          return;
        }

        if (session !== authSession) {
          setAuthSession(session);
        }

        const document = (await portalFetch(
          `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents/${encodeURIComponent(selectedDocumentId)}`,
          session.idToken
        )) as DocumentDetail;

        if (!cancelled) {
          setSelectedDocument(document);
          setIsCreatingDocument(false);
        }
      } catch (error) {
        if (cancelled) {
          return;
        }

        setSelectedDocument(null);
        setDocumentError(error instanceof Error ? error.message : 'Failed to load the selected document.');
      } finally {
        if (!cancelled) {
          setDocumentLoading(false);
        }
      }
    }

    void loadDocument();

    return () => {
      cancelled = true;
    };
  }, [authSession, activeBrainId, selectedDocumentId, documentDetailNonce]);

  useEffect(() => {
    if (isCreatingDocument) {
      setDocumentVersions([]);
      setVersionsLoading(false);
      setVersionsError(null);
      setSelectedVersionId(null);
      setSelectedVersion(null);
      setVersionLoading(false);
      setVersionError(null);
      return;
    }

    if (!selectedDocument) {
      setDocumentDraft(buildEmptyDocumentDraft());
      setDocumentSaveState('idle');
      setDocumentSaveMessage('Make a change to enable save.');
      return;
    }

    setDocumentDraft(buildDraftFromDocument(selectedDocument));
    setDocumentSaveState('info');
    setDocumentSaveMessage('All changes saved.');
  }, [isCreatingDocument, selectedDocument]);

  useEffect(() => {
    if (!authSession || !activeBrainId || !selectedDocumentId || isCreatingDocument) {
      setDocumentVersions([]);
      setVersionsLoading(false);
      setVersionsError(null);
      setSelectedVersionId(null);
      setSelectedVersion(null);
      setVersionLoading(false);
      setVersionError(null);
      return;
    }

    let cancelled = false;

    async function loadVersions() {
      setVersionsLoading(true);
      setVersionsError(null);

      try {
        const session = await ensureValidSession(authSession);
        if (cancelled) {
          return;
        }

        if (session !== authSession) {
          setAuthSession(session);
        }

        const response = (await portalFetch(
          `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents/${encodeURIComponent(selectedDocumentId)}/versions?limit=25`,
          session.idToken
        )) as DocumentVersionListResponse;

        if (cancelled) {
          return;
        }

        const nextVersions = Array.isArray(response.versions) ? response.versions : [];
        setDocumentVersions(nextVersions);
        setSelectedVersionId((currentSelectedVersionId) => (
          currentSelectedVersionId && nextVersions.some((version) => version.managedDocumentVersionId === currentSelectedVersionId)
            ? currentSelectedVersionId
            : null
        ));
      } catch (error) {
        if (cancelled) {
          return;
        }

        setDocumentVersions([]);
        setSelectedVersionId(null);
        setSelectedVersion(null);
        setVersionsError(error instanceof Error ? error.message : 'Failed to load document versions.');
      } finally {
        if (!cancelled) {
          setVersionsLoading(false);
        }
      }
    }

    void loadVersions();

    return () => {
      cancelled = true;
    };
  }, [authSession, activeBrainId, selectedDocumentId, isCreatingDocument, versionRefreshNonce]);

  useEffect(() => {
    if (!authSession || !activeBrainId || !selectedDocumentId || !selectedVersionId || isCreatingDocument) {
      setSelectedVersion(null);
      setVersionError(null);
      setVersionLoading(false);
      return;
    }

    let cancelled = false;

    async function loadVersionDetail() {
      setVersionLoading(true);
      setVersionError(null);

      try {
        const session = await ensureValidSession(authSession);
        if (cancelled) {
          return;
        }

        if (session !== authSession) {
          setAuthSession(session);
        }

        const version = (await portalFetch(
          `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents/${encodeURIComponent(selectedDocumentId)}/versions/${encodeURIComponent(selectedVersionId)}`,
          session.idToken
        )) as DocumentVersionDetail;

        if (!cancelled) {
          setSelectedVersion(version);
        }
      } catch (error) {
        if (cancelled) {
          return;
        }

        setSelectedVersion(null);
        setVersionError(error instanceof Error ? error.message : 'Failed to load the selected version.');
      } finally {
        if (!cancelled) {
          setVersionLoading(false);
        }
      }
    }

    void loadVersionDetail();

    return () => {
      cancelled = true;
    };
  }, [authSession, activeBrainId, selectedDocumentId, selectedVersionId, isCreatingDocument]);

  useEffect(() => {
    if (!authSession || brains.length === 0) {
      setToolQueryBrainId('');
      return;
    }

    setToolQueryBrainId((current) => {
      if (current && brains.some((brain) => brain.brainId === current)) {
        return current;
      }

      return activeBrainId || brains[0]?.brainId || '';
    });
  }, [authSession, brains, activeBrainId]);

  useEffect(() => {
    if (!authSession || !activeBrainId) {
      setIndexingRuns([]);
      setIndexingLoading(false);
      setIndexingError(null);
      return;
    }

    let cancelled = false;

    async function loadIndexingRuns() {
      setIndexingLoading(true);
      setIndexingError(null);

      try {
        const session = await ensureValidSession(authSession);
        if (cancelled) {
          return;
        }

        if (session !== authSession) {
          setAuthSession(session);
        }

        const response = (await portalFetch(
          `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/indexing/runs?limit=10`,
          session.idToken
        )) as IndexingRunResponse;

        if (!cancelled) {
          setIndexingRuns(Array.isArray(response.runs) ? response.runs : []);
        }
      } catch (error) {
        if (cancelled) {
          return;
        }

        setIndexingRuns([]);
        setIndexingError(error instanceof Error ? error.message : 'Failed to load indexing activity.');
      } finally {
        if (!cancelled) {
          setIndexingLoading(false);
        }
      }
    }

    void loadIndexingRuns();

    return () => {
      cancelled = true;
    };
  }, [authSession, activeBrainId, indexingRefreshNonce]);
  async function getValidSession() {
    if (!authSession) {
      throw new Error('Sign in before using the portal.');
    }

    const session = await ensureValidSession(authSession);
    if (session !== authSession) {
      setAuthSession(session);
    }

    return session;
  }

  const documentIsDirty = useMemo(
    () => hasUnsavedDocumentChanges(documentDraft, isCreatingDocument, selectedDocument),
    [documentDraft, isCreatingDocument, selectedDocument]
  );

  function handleDraftChange<K extends keyof DocumentDraft>(field: K, value: DocumentDraft[K]) {
    setDocumentDraft((current) => ({
      ...current,
      [field]: value,
    }));
    setDocumentSaveState('idle');
    setDocumentSaveMessage('Unsaved changes.');
  }

  function handleChangeBrain(nextBrainId: string) {
    if (nextBrainId === activeBrainId) {
      return;
    }

    if (!confirmDiscardDocumentChanges(documentIsDirty, 'Switch brains and discard unsaved document changes?')) {
      return;
    }

    setActiveBrainId(nextBrainId);
    setIsCreatingDocument(false);
    setSelectedDocumentId(null);
    setSelectedDocument(null);
    setDocumentDraft(buildEmptyDocumentDraft());
    setDocumentSaveState('idle');
    setDocumentSaveMessage('Make a change to enable save.');
    setDocumentVersions([]);
    setSelectedVersionId(null);
    setSelectedVersion(null);
  }

  function handleSelectDocument(nextDocumentId: string) {
    if (!nextDocumentId) {
      return;
    }

    if (!confirmDiscardDocumentChanges(documentIsDirty, 'Open another document and discard unsaved changes?')) {
      return;
    }

    setIsCreatingDocument(false);
    setSelectedDocumentId(nextDocumentId);
    setSelectedVersionId(null);
    setSelectedVersion(null);
    setDocumentSaveState('info');
    setDocumentSaveMessage('Loading document...');
  }

  function handleCreateDocument() {
    if (!confirmDiscardDocumentChanges(documentIsDirty, 'Create a new document and discard unsaved changes?')) {
      return;
    }

    setIsCreatingDocument(true);
    setSelectedDocumentId(null);
    setSelectedDocument(null);
    setDocumentDraft(buildEmptyDocumentDraft());
    setDocumentSaveState('idle');
    setDocumentSaveMessage('Start typing, then create the document.');
    setDocumentVersions([]);
    setSelectedVersionId(null);
    setSelectedVersion(null);
  }

  async function handleSaveDocument() {
    if (!activeBrainId) {
      setDocumentSaveState('warn');
      setDocumentSaveMessage('Select a managed-content brain before saving.');
      return;
    }

    try {
      setDocumentSaveState('saving');
      setDocumentSaveMessage(isCreatingDocument ? 'Creating document...' : 'Saving document...');
      const session = await getValidSession();
      const draft = normalizeDocumentDraft(documentDraft);
      const payload = buildDocumentPayload(draft);

      if (isCreatingDocument) {
        const created = await portalFetch(
          `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents`,
          session.idToken,
          {
            method: 'POST',
            body: JSON.stringify(payload),
          }
        ) as DocumentDetail;

        setIsCreatingDocument(false);
        setSelectedDocumentId(created.managedDocumentId);
        setDocumentSaveState('info');
        setDocumentSaveMessage(`Created document '${created.title || created.slug || created.managedDocumentId}'.`);
        setDocumentRefreshNonce((value) => value + 1);
        setDocumentDetailNonce((value) => value + 1);
        setVersionRefreshNonce((value) => value + 1);
        return;
      }

      if (!selectedDocument?.managedDocumentId) {
        setDocumentSaveState('warn');
        setDocumentSaveMessage('Select a document or create a new one before saving.');
        return;
      }

      const updated = await portalFetch(
        `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents/${encodeURIComponent(selectedDocument.managedDocumentId)}`,
        session.idToken,
        {
          method: 'PUT',
          body: JSON.stringify(payload),
        }
      ) as DocumentDetail;

      setDocumentSaveState('info');
      setDocumentSaveMessage(`Saved document '${updated.title || updated.slug || updated.managedDocumentId}'.`);
      setDocumentRefreshNonce((value) => value + 1);
      setDocumentDetailNonce((value) => value + 1);
      setVersionRefreshNonce((value) => value + 1);
    } catch (error) {
      setDocumentSaveState('error');
      setDocumentSaveMessage(error instanceof Error ? error.message : 'Failed to save the document.');
    }
  }

  function handleExportDocument() {
    try {
      const draft = normalizeDocumentDraft(documentDraft);
      if (!draft.title && !draft.content.trim()) {
        setDocumentSaveState('warn');
        setDocumentSaveMessage('Create or select a document before exporting Markdown.');
        return;
      }

      const markdown = buildMarkdownExport(draft, selectedDocument);
      const fileName = buildDocumentExportFileName(draft, selectedDocument);
      downloadTextFile(fileName, markdown, 'text/markdown;charset=utf-8');
      setDocumentSaveState('info');
      setDocumentSaveMessage(`Exported '${fileName}'.`);
    } catch (error) {
      setDocumentSaveState('error');
      setDocumentSaveMessage(error instanceof Error ? error.message : 'Failed to export the document.');
    }
  }

  async function handleDeleteDocument() {
    if (!selectedDocument?.managedDocumentId || isCreatingDocument) {
      setDocumentSaveState('warn');
      setDocumentSaveMessage('Select an existing document before deleting.');
      return;
    }

    const label = selectedDocument.title || selectedDocument.managedDocumentId;
    if (!window.confirm(`Delete document '${label}'?`)) {
      return;
    }

    try {
      const session = await getValidSession();
      await portalFetch(
        `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents/${encodeURIComponent(selectedDocument.managedDocumentId)}`,
        session.idToken,
        {
          method: 'DELETE',
        }
      );

      setSelectedDocumentId(null);
      setSelectedDocument(null);
      setDocumentDraft(buildEmptyDocumentDraft());
      setDocumentSaveState('info');
      setDocumentSaveMessage('Document deleted.');
      setDocumentRefreshNonce((value) => value + 1);
      setDocumentVersions([]);
      setSelectedVersionId(null);
      setSelectedVersion(null);
    } catch (error) {
      setDocumentSaveState('error');
      setDocumentSaveMessage(error instanceof Error ? error.message : 'Failed to delete the document.');
    }
  }

  function handleRevertDocument() {
    if (isCreatingDocument) {
      setDocumentDraft(buildEmptyDocumentDraft());
      setDocumentSaveState('idle');
      setDocumentSaveMessage('Start typing, then create the document.');
      return;
    }

    if (selectedDocument) {
      setDocumentDraft(buildDraftFromDocument(selectedDocument));
      setDocumentSaveState('info');
      setDocumentSaveMessage('Changes reverted to the last saved version.');
      return;
    }

    setDocumentDraft(buildEmptyDocumentDraft());
    setDocumentSaveState('idle');
    setDocumentSaveMessage('Make a change to enable save.');
  }


  async function handleImportDocument(file: File | null) {
    if (!file) {
      return;
    }

    if (!confirmDiscardDocumentChanges(documentIsDirty, 'Import Markdown and discard unsaved changes?')) {
      return;
    }

    try {
      const imported = parseImportedMarkdown(await file.text(), file.name);
      setIsCreatingDocument(true);
      setSelectedDocumentId(null);
      setSelectedDocument(null);
      setDocumentDraft(imported);
      setDocumentSaveState('info');
      setDocumentSaveMessage('Imported Markdown draft ready.');
      setDocumentVersions([]);
      setSelectedVersionId(null);
      setSelectedVersion(null);
    } catch (error) {
      setDocumentSaveState('error');
      setDocumentSaveMessage(error instanceof Error ? error.message : 'Failed to import Markdown.');
    }
  }

  function handleRefreshVersions() {
    if (!selectedDocument?.managedDocumentId || isCreatingDocument) {
      setVersionsError('Select a saved document before loading version history.');
      return;
    }

    setVersionRefreshNonce((value) => value + 1);
  }

  function handleSelectVersion(nextVersionId: string) {
    setSelectedVersionId((current) => current === nextVersionId ? null : nextVersionId);
  }

  async function handleRestoreVersion() {
    if (!selectedDocument?.managedDocumentId || !selectedVersion?.managedDocumentVersionId) {
      setVersionError('Select a document version before restoring.');
      return;
    }

    const restoreTimestamp = formatDateTime(selectedVersion.createdAt);
    if (!window.confirm(`Restore the selected version from ${restoreTimestamp}? Current draft changes will be replaced.`)) {
      return;
    }

    try {
      const session = await getValidSession();
      await portalFetch(
        `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents/${encodeURIComponent(selectedDocument.managedDocumentId)}/versions/${encodeURIComponent(selectedVersion.managedDocumentVersionId)}/restore`,
        session.idToken,
        {
          method: 'POST',
        }
      );

      setDocumentSaveState('info');
      setDocumentSaveMessage(`Restored version from ${restoreTimestamp}.`);
      setDocumentDetailNonce((value) => value + 1);
      setVersionRefreshNonce((value) => value + 1);
    } catch (error) {
      setVersionError(error instanceof Error ? error.message : 'Failed to restore the selected version.');
    }
  }
  async function handleRefreshSession() {
    try {
      await getValidSession();
      setRefreshNonce((value) => value + 1);
      setAccountActionMessage('Session refreshed.');
    } catch (error) {
      setAccountActionMessage(error instanceof Error ? error.message : 'Failed to refresh session.');
    }
  }

  async function handleCreateToken() {
    try {
      const session = await getValidSession();
      const name = tokenNameInput.trim();
      if (!name) {
        setAccountActionMessage('Token name is required.');
        return;
      }

      const scopes = ['mcp:read'];
      if (requestWriteScope) {
        scopes.push('mcp:write');
      }

      const created = await portalFetch('/portal-api/tenant/tokens', session.idToken, {
        method: 'POST',
        body: JSON.stringify({
          name,
          scopes,
          expiresAt: parseExpiresAt(tokenExpiresAtInput),
        }),
      }) as { name: string; tokenPrefix: string; scopes: string[]; token?: string };

      setCreatedToken({
        token: created.token || '',
        meta: `${created.name} | ${created.tokenPrefix} | ${created.scopes.join(', ')}`,
      });
      setTokenNameInput('');
      setTokenExpiresAtInput('');
      setRequestWriteScope(false);
      setAccountActionMessage('Token created. Save the raw token value before dismissing it.');
      setRefreshNonce((value) => value + 1);
    } catch (error) {
      setAccountActionMessage(error instanceof Error ? error.message : 'Failed to create token.');
    }
  }

  async function handleRevokeToken(apiTokenId: string) {
    if (!window.confirm(`Revoke token ${apiTokenId}?`)) {
      return;
    }

    try {
      const session = await getValidSession();
      await portalFetch(`/portal-api/tenant/tokens/${encodeURIComponent(apiTokenId)}`, session.idToken, {
        method: 'DELETE',
      });
      setAccountActionMessage(`Token ${apiTokenId} revoked.`);
      setRefreshNonce((value) => value + 1);
    } catch (error) {
      setAccountActionMessage(error instanceof Error ? error.message : 'Failed to revoke token.');
    }
  }

  async function handleCopyCreatedToken() {
    if (!createdToken?.token) {
      return;
    }

    try {
      await navigator.clipboard.writeText(createdToken.token);
      setAccountActionMessage('Raw token copied to clipboard.');
    } catch {
      setAccountActionMessage('Clipboard copy failed. Copy the token manually before dismissing it.');
    }
  }
  async function handleCopyMcpConfig(mcpConfigSnippet: string) {
    try {
      await navigator.clipboard.writeText(mcpConfigSnippet);
      setToolActionMessage('MCP configuration copied to clipboard.');
    } catch {
      setToolActionMessage('Clipboard copy failed. Copy the MCP configuration manually.');
    }
  }

  async function handleToolQuerySubmit() {
    try {
      const session = await getValidSession();
      const oql = buildToolOql(toolQueryBrainId || activeBrainId || '', toolQuerySearch, toolQueryRank, toolQueryWhere, toolQueryLimit);
      if (!oql) {
        setToolActionMessage('Select a brain before running a smoke test.');
        return;
      }

      const result = await portalFetch('/portal-api/tenant/query', session.idToken, {
        method: 'POST',
        body: JSON.stringify({ oql }),
      }) as ToolQueryResponse;

      setToolQueryResults(result);
      setToolFetchedDocuments({});
      setToolActionMessage(null);
    } catch (error) {
      setToolQueryResults(null);
      setToolFetchedDocuments({});
      setToolActionMessage(error instanceof Error ? error.message : 'Smoke test failed.');
    }
  }

  async function handleFetchToolResultDocument(result: ToolQueryResultItem) {
    const key = getToolResultKey(result);
    setToolFetchedDocuments((current) => ({
      ...current,
      [key]: { status: 'loading' },
    }));

    try {
      const session = await getValidSession();
      const documentUrl = result.documentId
        ? `/portal-api/tenant/brains/${encodeURIComponent(result.brainId)}/documents/${encodeURIComponent(result.documentId)}`
        : `/portal-api/tenant/brains/${encodeURIComponent(result.brainId)}/documents/by-path?canonicalPath=${encodeURIComponent(result.canonicalPath || '')}`;
      const document = await portalFetch(documentUrl, session.idToken) as DocumentDetail;

      setToolFetchedDocuments((current) => ({
        ...current,
        [key]: { status: 'ready', document },
      }));
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Document fetch failed.';
      setToolFetchedDocuments((current) => ({
        ...current,
        [key]: { status: 'error', message },
      }));
      setToolActionMessage(message);
    }
  }

  function handleRefreshIndexingRuns() {
    setIndexingRefreshNonce((value) => value + 1);
  }

  async function handleTriggerBrainReindex() {
    if (!activeBrainId) {
      setToolActionMessage('Select a brain before triggering reindex.');
      return;
    }

    if (!window.confirm('Trigger a full managed-content reindex for the active brain?')) {
      return;
    }

    try {
      const session = await getValidSession();
      await portalFetch(
        `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/reindex`,
        session.idToken,
        { method: 'POST' }
      );
      setToolActionMessage('Reindex started for the active brain.');
      setIndexingRefreshNonce((value) => value + 1);
    } catch (error) {
      setToolActionMessage(error instanceof Error ? error.message : 'Failed to start reindex.');
    }
  }
  const activeDefinition = viewDefinitions[activeView];
  const sessionChip = authSession ? (context?.customerName || authSession.email || 'Authenticated') : 'Signed out';
  const activeTokenCount = useMemo(
    () => tokens.filter((token) => !token.revokedAt).length,
    [tokens]
  );
  const activeToken = useMemo(
    () => tokens.find((token) => !token.revokedAt) ?? null,
    [tokens]
  );
  const toolOql = useMemo(
    () => buildToolOql(toolQueryBrainId || activeBrainId || '', toolQuerySearch, toolQueryRank, toolQueryWhere, toolQueryLimit),
    [toolQueryBrainId, activeBrainId, toolQuerySearch, toolQueryRank, toolQueryWhere, toolQueryLimit]
  );
  const mcpToolManifestUrl = useMemo(
    () => buildMcpToolManifestUrl(config?.mcpBaseUrl || ''),
    [config?.mcpBaseUrl]
  );
  const mcpConfigSnippet = useMemo(
    () => buildMcpConfigSnippet(config?.mcpBaseUrl || '', activeToken?.name || 'replace-with-token-name'),
    [config?.mcpBaseUrl, activeToken?.name]
  );
  const filteredDocuments = useMemo(
    () => applyDocumentFilter(documents, documentFilter),
    [documents, documentFilter]
  );
  const documentGroups = useMemo(
    () => buildDocumentDirectoryGroups(filteredDocuments),
    [filteredDocuments]
  );
  const activeBrain = brains.find((brain) => brain.brainId === activeBrainId) ?? null;

  return (
    <div className="app-shell">
      <header className="site-header">
        <div className="brand-block">
          <p className="eyebrow">OpenCortex Portal</p>
          <h1>{activeDefinition.title}</h1>
          <p className="lede">{activeDefinition.lead}</p>
        </div>

        <div className="header-meta">
          <div className="session-chip">{sessionChip}</div>
          <div className="action-row compact-actions">
            <button type="button" className="button" onClick={() => window.location.assign('/legacy')}>
              Open Classic Portal
            </button>
            <button
              type="button"
              className="button"
              onClick={() => setRefreshNonce((value) => value + 1)}
              disabled={!authSession || workspaceLoading}
            >
              Refresh Workspace
            </button>
            <button type="button" className="button button-danger" onClick={handleClearSession} disabled={!authSession}>
              Clear Session
            </button>
          </div>
          {authSession ? (
            <nav className="app-nav" aria-label="Portal sections">
              {orderedViews.filter((view) => view !== 'signin').map((view) => (
                <button
                  key={view}
                  type="button"
                  className={view === activeView ? 'app-nav-link active' : 'app-nav-link'}
                  onClick={() => navigateToView(view, authSession, setActiveView)}
                >
                  {viewDefinitions[view].title}
                </button>
              ))}
            </nav>
          ) : null}
        </div>
      </header>

      <section className="summary-grid">
        <article className="summary-card">
          <p className="panel-label">Workspace</p>
          <strong>{context?.customerName ?? (authSession?.email ? authSession.email : 'Signed Out')}</strong>
          <p>{context ? `${context.customerSlug} | default brain ${context.brainName}` : authSession ? `Session expires ${formatDateTime(authSession.expiresAt)}.` : 'Sign in to load workspace context.'}</p>
        </article>
        <article className="summary-card">
          <p className="panel-label">Plan</p>
          <strong>{billing?.planId ?? (authSession ? 'Loading' : 'Unknown')}</strong>
          <p>{billing ? `${billing.subscriptionStatus} | ${formatDocumentQuota(billing)}` : authSession ? 'Fetching billing state...' : 'Billing state appears after sign-in.'}</p>
        </article>
        <article className="summary-card">
          <p className="panel-label">MCP Access</p>
          <strong>{billing ? (billing.mcpWrite ? 'Read + Write' : 'Read Only') : authSession ? 'Loading' : 'Unknown'}</strong>
          <p>{billing ? (billing.mcpWrite ? 'This workspace can mint mcp:write tokens.' : 'Requested mcp:write tokens will be rejected until the plan allows it.') : authSession ? 'Fetching workspace policy...' : 'Scope and write posture appear after sign-in.'}</p>
        </article>
        <article className="summary-card">
          <p className="panel-label">Tokens</p>
          <strong>{authSession ? String(tokens.length) : '0'}</strong>
          <p>{authSession ? `${activeTokenCount} active token(s) loaded.` : 'No token records loaded.'}</p>
        </article>
      </section>

      {configError ? <section className="banner error-banner" role="alert">{configError}</section> : null}
      {workspaceError ? <section className="banner error-banner" role="alert">{workspaceError}</section> : null}
      {workspaceLoading ? <section className="banner info-banner">Refreshing workspace state...</section> : null}

      <main className="portal-main">
        {!authSession ? (
          <SignedOutState activeDefinition={activeDefinition} />
        ) : activeView === 'documents' ? (
          <DocumentsView
            activeBrain={activeBrain}
            activeBrainId={activeBrainId}
            brains={brains}
            documentDraft={documentDraft}
            documentError={documentError}
            documentFilter={documentFilter}
            documentGroups={documentGroups}
            documentIsDirty={documentIsDirty}
            documentLoading={documentLoading}
            documentSaveMessage={documentSaveMessage}
            documentSaveState={documentSaveState}
            documentVersions={documentVersions}
            documents={documents}
            documentsError={documentsError}
            documentsLoading={documentsLoading}
            filteredDocuments={filteredDocuments}
            isCreatingDocument={isCreatingDocument}
            onChangeBrain={handleChangeBrain}
            onChangeDocumentFilter={setDocumentFilter}
            onCreateDocument={handleCreateDocument}
            onDeleteDocument={handleDeleteDocument}
            onDraftChange={handleDraftChange}
            onExportDocument={handleExportDocument}
            onImportDocument={handleImportDocument}
            onRefreshDocuments={() => setDocumentRefreshNonce((value) => value + 1)}
            onRefreshVersions={handleRefreshVersions}
            onRestoreVersion={handleRestoreVersion}
            onRevertDocument={handleRevertDocument}
            onSaveDocument={handleSaveDocument}
            onSelectDocument={handleSelectDocument}
            onSelectVersion={handleSelectVersion}
            selectedDocument={selectedDocument}
            selectedDocumentId={selectedDocumentId}
            selectedVersion={selectedVersion}
            selectedVersionId={selectedVersionId}
            versionError={versionError}
            versionLoading={versionLoading}
            versionsError={versionsError}
            versionsLoading={versionsLoading}
          />
        ) : activeView === 'usage' ? (
          <UsageView
            activeBrainId={activeBrainId}
            authSession={authSession}
            billing={billing}
            context={context}
          />
        ) : activeView === 'tools' ? (
          <ToolsView
            activeBrainId={activeBrainId}
            activeToken={activeToken}
            billing={billing}
            config={config}
            indexingError={indexingError}
            indexingLoading={indexingLoading}
            indexingRuns={indexingRuns}
            mcpConfigSnippet={mcpConfigSnippet}
            mcpToolManifestUrl={mcpToolManifestUrl}
            context={context}
            onCopyMcpConfig={() => handleCopyMcpConfig(mcpConfigSnippet)}
            onFetchDocument={handleFetchToolResultDocument}
            onRefreshIndexing={handleRefreshIndexingRuns}
            onRunQuery={handleToolQuerySubmit}
            onTriggerReindex={handleTriggerBrainReindex}
            onUpdateToolQueryBrain={setToolQueryBrainId}
            onUpdateToolQueryLimit={setToolQueryLimit}
            onUpdateToolQueryRank={setToolQueryRank}
            onUpdateToolQuerySearch={setToolQuerySearch}
            onUpdateToolQueryWhere={setToolQueryWhere}
            toolActionMessage={toolActionMessage}
            toolFetchedDocuments={toolFetchedDocuments}
            toolOql={toolOql}
            toolQueryBrainId={toolQueryBrainId}
            toolQueryLimit={toolQueryLimit}
            toolQueryRank={toolQueryRank}
            toolQueryResults={toolQueryResults}
            toolQuerySearch={toolQuerySearch}
            toolQueryWhere={toolQueryWhere}
            brains={brains}
          />
        ) : activeView === 'account' ? (
          <AccountView
            accountActionMessage={accountActionMessage}
            authSession={authSession}
            billing={billing}
            context={context}
            createdToken={createdToken}
            onCopyCreatedToken={handleCopyCreatedToken}
            onCreateToken={handleCreateToken}
            onDismissCreatedToken={() => setCreatedToken(null)}
            onRefreshSession={handleRefreshSession}
            onRevokeToken={handleRevokeToken}
            onSignOut={handleClearSession}
            onRequestWriteScopeChange={setRequestWriteScope}
            onTokenExpiresAtInputChange={setTokenExpiresAtInput}
            onTokenNameInputChange={setTokenNameInput}
            requestWriteScope={requestWriteScope}
            tokenExpiresAtInput={tokenExpiresAtInput}
            tokenNameInput={tokenNameInput}
            tokens={tokens}
          />
        ) : (
          <DefaultSignedInView
            activeDefinition={activeDefinition}
            authSession={authSession}
            billing={billing}
            brains={brains}
            config={config}
            context={context}
          />
        )}
      </main>
    </div>
  );
}

type SignedOutStateProps = {
  activeDefinition: ViewDefinition;
};

function SignedOutState({ activeDefinition }: SignedOutStateProps) {
  return (
    <section className="portal-layout">
      <article className="panel portal-hero">
        <p className="eyebrow">Sign In</p>
        <h2>The portal uses the same browser session contract.</h2>
        <p className="summary-detail">
          Use the classic portal if you need the older sign-in surface. This app reads the same stored browser session and tenant bootstrap endpoints.
        </p>
        <ul className="feature-list">
          {activeDefinition.bullets.map((bullet) => (
            <li key={bullet}>{bullet}</li>
          ))}
        </ul>
        <div className="action-row">
          <button type="button" className="button button-primary" onClick={() => window.location.assign('/legacy')}>
            Open Classic Sign-In
          </button>
        </div>
      </article>

      <section className="portal-grid">
        <article className="panel slice-card">
          <p className="panel-label">Current Contract</p>
          <h3>Same storage key and refresh flow</h3>
          <p>The portal reads `opencortex.portal.auth_session` and reuses `/portal-auth/refresh` before calling tenant APIs.</p>
        </article>
        <article className="panel slice-card">
          <p className="panel-label">Cutover Rule</p>
          <h3>Legacy Fallback</h3>
          <p>The classic portal remains available at `/legacy` for fallback access.</p>
        </article>
      </section>
    </section>
  );
}

type DefaultSignedInViewProps = {
  activeDefinition: ViewDefinition;
  authSession: StoredAuthSession;
  billing: PortalBilling | null;
  brains: BrainSummary[];
  config: PortalConfig | null;
  context: PortalContext | null;
};

function DefaultSignedInView({
  activeDefinition,
  authSession,
  billing,
  brains,
  config,
  context
}: DefaultSignedInViewProps) {
  return (
    <section className="portal-layout">
      <article className="panel portal-hero">
        <p className="eyebrow">{activeDefinition.title}</p>
        <h2>{activeDefinition.lead}</h2>
        <ul className="feature-list">
          {activeDefinition.bullets.map((bullet) => (
            <li key={bullet}>{bullet}</li>
          ))}
        </ul>
      </article>

      <section className="portal-grid">
        <article className="panel slice-card">
          <p className="panel-label">Session</p>
          <h3>{context?.displayName ?? (authSession.displayName || authSession.email)}</h3>
          <p>{context ? `${context.email} | ${context.role}` : `Session expires ${formatDateTime(authSession.expiresAt)}.`}</p>
        </article>
        <article className="panel slice-card">
          <p className="panel-label">Brains</p>
          <h3>{brains.length}</h3>
          <p>{brains.length > 0 ? `${brains[0].name} is the first available workspace brain.` : 'No brains loaded yet.'}</p>
        </article>
        <article className="panel slice-card">
          <p className="panel-label">Usage</p>
          <h3>{billing ? `${billing.activeDocuments} / ${formatLimit(billing.maxDocuments)}` : 'Loading'}</h3>
          <p>{billing ? `${billing.mcpQueriesUsed} / ${formatLimit(billing.mcpQueriesPerMonth)} MCP queries used.` : 'Waiting for billing posture.'}</p>
        </article>
        <article className="panel slice-card">
          <p className="panel-label">Portal Shell</p>
          <h3>Shared state is in one place</h3>
          <p>Route switching, session bootstrap, and workspace status all run through the same portal shell.</p>
        </article>
      </section>

      <section className="panel notes-panel">
        <div className="panel-header">
          <div>
            <h3>Portal Notes</h3>
            <p className="summary-detail">Live notes from `/portal-config` keep the portal aligned with the current host configuration.</p>
          </div>
        </div>

        <div className="notes-list">
          {(config?.notes ?? ['Portal configuration notes will appear here once loaded.']).map((note) => (
            <article key={note} className="note-card">{note}</article>
          ))}
        </div>
      </section>
    </section>
  );
}

type ToolsViewProps = {
  activeBrainId: string;
  activeToken: TokenSummary | null;
  billing: PortalBilling | null;
  brains: BrainSummary[];
  config: PortalConfig | null;
  context: PortalContext | null;
  indexingError: string | null;
  indexingLoading: boolean;
  indexingRuns: IndexingRunSummary[];
  mcpConfigSnippet: string;
  mcpToolManifestUrl: string;
  onCopyMcpConfig: () => void;
  onFetchDocument: (result: ToolQueryResultItem) => void;
  onRefreshIndexing: () => void;
  onRunQuery: () => void;
  onTriggerReindex: () => void;
  onUpdateToolQueryBrain: (value: string) => void;
  onUpdateToolQueryLimit: (value: string) => void;
  onUpdateToolQueryRank: (value: string) => void;
  onUpdateToolQuerySearch: (value: string) => void;
  onUpdateToolQueryWhere: (value: string) => void;
  toolActionMessage: string | null;
  toolFetchedDocuments: Record<string, ToolFetchedDocumentState>;
  toolOql: string;
  toolQueryBrainId: string;
  toolQueryLimit: string;
  toolQueryRank: string;
  toolQueryResults: ToolQueryResponse | null;
  toolQuerySearch: string;
  toolQueryWhere: string;
};

function ToolsView({
  activeBrainId,
  activeToken,
  billing,
  brains,
  config,
  context,
  indexingError,
  indexingLoading,
  indexingRuns,
  mcpConfigSnippet,
  mcpToolManifestUrl,
  onCopyMcpConfig,
  onFetchDocument,
  onRefreshIndexing,
  onRunQuery,
  onTriggerReindex,
  onUpdateToolQueryBrain,
  onUpdateToolQueryLimit,
  onUpdateToolQueryRank,
  onUpdateToolQuerySearch,
  onUpdateToolQueryWhere,
  toolActionMessage,
  toolFetchedDocuments,
  toolOql,
  toolQueryBrainId,
  toolQueryLimit,
  toolQueryRank,
  toolQueryResults,
  toolQuerySearch,
  toolQueryWhere
}: ToolsViewProps) {
  return (
    <section className="portal-layout">
      <article className="panel portal-hero">
        <p className="eyebrow">Tools</p>
        <h2>Workspace tools stay close to retrieval, MCP setup, and indexing.</h2>
        <p className="summary-detail">
          Run tenant-scoped OQL smoke tests, fetch the full document behind a result, copy MCP setup details, and inspect recent indexing activity without leaving the portal.
        </p>
      </article>

      {toolActionMessage ? <section className="banner info-banner">{toolActionMessage}</section> : null}
      {indexingError ? <section className="banner error-banner" role="alert">{indexingError}</section> : null}

      <section className="panel">
        <div className="panel-header">
          <div>
            <h3>Workspace Context</h3>
            <p className="summary-detail">Live tenant identity, quota posture, and active managed-content brain for the current browser session.</p>
          </div>
        </div>
        <dl className="facts-list">
          <div className="fact-row"><dt>User</dt><dd>{context?.displayName || 'Not loaded'}</dd></div>
          <div className="fact-row"><dt>Email</dt><dd>{context?.email || 'Not loaded'}</dd></div>
          <div className="fact-row"><dt>Role</dt><dd>{context?.role || 'Not loaded'}</dd></div>
          <div className="fact-row"><dt>Customer</dt><dd>{context ? `${context.customerName} (${context.customerSlug})` : 'Not loaded'}</dd></div>
          <div className="fact-row"><dt>Brain</dt><dd>{context?.brainName || 'Not loaded'}</dd></div>
          <div className="fact-row"><dt>Selected Brain</dt><dd>{activeBrainId || 'Not selected'}</dd></div>
          <div className="fact-row"><dt>Documents</dt><dd>{billing ? `${billing.activeDocuments} active of ${formatLimit(billing.maxDocuments)}` : 'Not loaded'}</dd></div>
          <div className="fact-row"><dt>MCP Queries</dt><dd>{billing ? `${billing.mcpQueriesUsed} used of ${formatLimit(billing.mcpQueriesPerMonth)}` : 'Not loaded'}</dd></div>
        </dl>
      </section>

      <section className="tools-layout">
        <section className="panel">
          <div className="panel-header">
            <div>
              <h3>OQL Smoke Test</h3>
              <p className="summary-detail">Run a quick tenant-scoped retrieval query against the selected brain.</p>
            </div>
          </div>

          <div className="query-form">
            <label className="field">
              <span>Brain</span>
              <select value={toolQueryBrainId} onChange={(event) => onUpdateToolQueryBrain(event.target.value)} disabled={brains.length === 0}>
                {brains.length === 0 ? <option value="">No managed-content brains found</option> : null}
                {brains.map((brain) => (
                  <option key={brain.brainId} value={brain.brainId}>{brain.name} ({brain.brainId})</option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>Search</span>
              <input type="text" placeholder="identity" value={toolQuerySearch} onChange={(event) => onUpdateToolQuerySearch(event.target.value)} />
            </label>
            <label className="field">
              <span>Rank</span>
              <select value={toolQueryRank} onChange={(event) => onUpdateToolQueryRank(event.target.value)}>
                <option value="keyword">keyword</option>
                <option value="semantic">semantic</option>
                <option value="hybrid">hybrid</option>
              </select>
            </label>
            <label className="field">
              <span>Where</span>
              <input type="text" placeholder="type = &quot;reference&quot;" value={toolQueryWhere} onChange={(event) => onUpdateToolQueryWhere(event.target.value)} />
            </label>
            <label className="field">
              <span>Limit</span>
              <input type="number" min={1} max={25} value={toolQueryLimit} onChange={(event) => onUpdateToolQueryLimit(event.target.value)} />
            </label>
            <label className="field field-wide">
              <span>Generated OQL</span>
              <textarea rows={6} readOnly className="document-frontmatter-editor" value={toolOql} />
            </label>
            <button type="button" className="button button-primary" onClick={onRunQuery}>Run Smoke Test</button>
          </div>

          <div className="tool-query-result stack compact">
            {!toolQueryResults ? (
              <div className="empty-state">No smoke test executed yet.</div>
            ) : !toolQueryResults.results?.length ? (
              <>
                {toolQueryResults.summary ? (
                  <p className="result-summary">
                    {toolQueryResults.summary.totalResults} result(s) | keyword {toolQueryResults.summary.resultsWithKeywordSignal} | semantic {toolQueryResults.summary.resultsWithSemanticSignal} | graph {toolQueryResults.summary.resultsWithGraphSignal}
                  </p>
                ) : null}
                <div className="empty-state">No results returned.</div>
              </>
            ) : (
              <>
                {toolQueryResults.summary ? (
                  <p className="result-summary">
                    {toolQueryResults.summary.totalResults} result(s) | keyword {toolQueryResults.summary.resultsWithKeywordSignal} | semantic {toolQueryResults.summary.resultsWithSemanticSignal} | graph {toolQueryResults.summary.resultsWithGraphSignal}
                  </p>
                ) : null}
                {toolQueryResults.results.map((result) => {
                  const fetchState = toolFetchedDocuments[getToolResultKey(result)] || null;
                  return (
                    <article key={getToolResultKey(result)} className="result-card">
                      <div className="result-card-header">
                        <div>
                          <strong>{result.title || '(untitled)'}</strong>
                          <div className="result-card-meta">{result.canonicalPath || ''}</div>
                        </div>
                        <div className="action-row">
                          <button
                            type="button"
                            className="button"
                            disabled={!(result.documentId || result.canonicalPath) || fetchState?.status === 'loading'}
                            onClick={() => onFetchDocument(result)}
                          >
                            {fetchState?.status === 'ready' ? 'Refresh Document' : 'Fetch Full Document'}
                          </button>
                        </div>
                      </div>
                      <p>{result.snippet || ''}</p>
                      <div className="tool-document-panel">
                        {renderToolFetchState(result, fetchState)}
                      </div>
                    </article>
                  );
                })}
              </>
            )}
          </div>
        </section>

        <section className="panel">
          <div className="panel-header">
            <div>
              <h3>MCP Setup</h3>
              <p className="summary-detail">Copy-ready connection details for a desktop client or agent runtime.</p>
            </div>
          </div>

          <div className="facts-list compact-facts">
            <div className="fact-row"><dt>MCP URL</dt><dd>{config?.mcpBaseUrl || 'Not configured'}</dd></div>
            <div className="fact-row"><dt>Recommended Token</dt><dd>{activeToken ? `${activeToken.name} (${activeToken.tokenPrefix})` : 'Create a token under Account.'}</dd></div>
            <div className="fact-row"><dt>Operator Console</dt><dd>{config?.operatorConsoleUrl || 'Not configured'}</dd></div>
            <div className="fact-row"><dt>Tool Manifest</dt><dd>{mcpToolManifestUrl || 'Not configured'}</dd></div>
          </div>

          <label className="field">
            <span>Example Configuration</span>
            <textarea rows={10} readOnly className="document-content-editor" value={mcpConfigSnippet} />
          </label>

          <div className="action-row">
            <button type="button" className="button button-primary" onClick={onCopyMcpConfig}>Copy MCP Config</button>
          </div>
        </section>

        <section className="panel panel-wide">
          <div className="panel-header">
            <div>
              <h3>Indexing Activity</h3>
              <p className="summary-detail">Recent runs for the active brain and a tenant-scoped reindex action.</p>
            </div>
            <div className="action-row">
              <button type="button" className="button" onClick={onRefreshIndexing} disabled={!activeBrainId || indexingLoading}>Refresh Indexing</button>
              <button type="button" className="button button-primary" onClick={onTriggerReindex} disabled={!activeBrainId || !Boolean(billing?.mcpWrite)}>Run Reindex</button>
            </div>
          </div>

          {!activeBrainId ? (
            <div className="empty-state">Select a brain to load indexing activity.</div>
          ) : indexingLoading ? (
            <div className="empty-state">Loading indexing activity...</div>
          ) : indexingRuns.length === 0 ? (
            <div className="empty-state">No indexing runs recorded for this brain yet.</div>
          ) : (
            <div className="indexing-run-list">
              {indexingRuns.map((run, index) => (
                <article key={`${run.startedAt || 'run'}-${index}`} className="token-record-card">
                  <div className="token-record-header">
                    <div>
                      <h3>{formatDateTime(run.startedAt)}</h3>
                      <p className="summary-detail">{run.triggerType || 'unknown trigger'}</p>
                    </div>
                    <span className="status-chip status-chip-active">{run.status || 'unknown'}</span>
                  </div>
                  <div className="token-record-meta">
                    <span>Seen {String(run.documentsSeen ?? 0)}</span>
                    <span>Indexed {String(run.documentsIndexed ?? 0)}</span>
                    <span>Failed {String(run.documentsFailed ?? 0)}</span>
                    <span>{run.errorSummary || 'No error summary.'}</span>
                  </div>
                </article>
              ))}
            </div>
          )}
        </section>
      </section>
    </section>
  );
}
function buildMcpToolManifestUrl(baseUrl: string) {
  if (!baseUrl) {
    return '';
  }

  return baseUrl.endsWith('/mcp')
    ? `${baseUrl.slice(0, -4)}/tool-manifest`
    : `${baseUrl.replace(/\/$/, '')}/tool-manifest`;
}

function buildMcpConfigSnippet(url: string, tokenName: string) {
  return JSON.stringify({
    mcpServers: {
      OpenCortex: {
        url: url || 'https://your-mcp-host/mcp',
        headers: {
          Authorization: 'Bearer oct_replace_with_token',
        },
        notes: `Token label: ${tokenName}`,
      },
    },
  }, null, 2);
}

function buildToolOql(brainId: string, search: string, rank: string, where: string, limit: string) {
  if (!brainId) {
    return '';
  }

  const lines = [`FROM brain("${brainId}")`];
  if (search.trim()) {
    lines.push(`SEARCH "${search.trim()}"`);
  }
  if (where.trim()) {
    lines.push(`WHERE ${where.trim()}`);
  }
  lines.push(`RANK ${rank || 'hybrid'}`);
  lines.push(`LIMIT ${limit || '5'}`);
  return lines.join('\n');
}

function getToolResultKey(result: ToolQueryResultItem) {
  return result.documentId || `${result.brainId || ''}::${result.canonicalPath || ''}`;
}

function renderToolFetchState(result: ToolQueryResultItem, fetchState: ToolFetchedDocumentState | null) {
  if (!result.documentId && !result.canonicalPath) {
    return <div className="empty-state">This result does not expose a retrievable document id or canonical path.</div>;
  }

  if (!fetchState) {
    return <p className="tool-fetch-note">Fetch the stored document to inspect the full markdown behind this ranked snippet.</p>;
  }

  if (fetchState.status === 'loading') {
    return <div className="empty-state">Fetching full document...</div>;
  }

  if (fetchState.status === 'error') {
    return <div className="empty-state">{fetchState.message || 'Document fetch failed.'}</div>;
  }

  const document = fetchState.document;
  return (
    <>
      <div className="tool-document-meta">
        {document?.status || 'draft'} | {document?.canonicalPath || ''} | {String((document as { wordCount?: number } | undefined)?.wordCount ?? 0)} words | updated {formatDateTime(document?.updatedAt)}
      </div>
      <div className="tool-document-preview" dangerouslySetInnerHTML={{ __html: renderMarkdown(document?.content || '') }} />
    </>
  );
}
type AccountViewProps = {
  accountActionMessage: string | null;
  authSession: StoredAuthSession;
  billing: PortalBilling | null;
  context: PortalContext | null;
  createdToken: CreatedTokenState | null;
  onCopyCreatedToken: () => void;
  onCreateToken: () => void;
  onDismissCreatedToken: () => void;
  onRefreshSession: () => void;
  onRequestWriteScopeChange: (value: boolean) => void;
  onRevokeToken: (apiTokenId: string) => void;
  onSignOut: () => void;
  onTokenExpiresAtInputChange: (value: string) => void;
  onTokenNameInputChange: (value: string) => void;
  requestWriteScope: boolean;
  tokenExpiresAtInput: string;
  tokenNameInput: string;
  tokens: TokenSummary[];
};

function AccountView({
  accountActionMessage,
  authSession,
  billing,
  context,
  createdToken,
  onCopyCreatedToken,
  onCreateToken,
  onDismissCreatedToken,
  onRefreshSession,
  onRequestWriteScopeChange,
  onRevokeToken,
  onSignOut,
  onTokenExpiresAtInputChange,
  onTokenNameInputChange,
  requestWriteScope,
  tokenExpiresAtInput,
  tokenNameInput,
  tokens
}: AccountViewProps) {
  const activeTokenCount = tokens.filter((token) => !token.revokedAt).length;

  return (
    <section className="portal-layout">
      <article className="panel portal-hero">
        <p className="eyebrow">Account</p>
        <h2>Tenant settings and token access are now using live account data.</h2>
        <p className="summary-detail">
          Session controls, workspace posture, MCP token issuance, and token revocation now live in the main portal experience.
        </p>
      </article>

      <section className="account-layout">
        <section className="panel">
          <div className="panel-header">
            <div>
              <h3>Browser Session</h3>
              <p className="summary-detail">Authenticated session state and tenant identity for the current browser.</p>
            </div>
          </div>

          <dl className="facts-list compact-facts">
            <div className="fact-row"><dt>Email</dt><dd>{context?.email || authSession.email}</dd></div>
            <div className="fact-row"><dt>User</dt><dd>{context?.displayName || authSession.displayName || authSession.email}</dd></div>
            <div className="fact-row"><dt>Workspace</dt><dd>{context ? `${context.customerName} (${context.customerSlug})` : 'Not loaded'}</dd></div>
            <div className="fact-row"><dt>Session Expires</dt><dd>{formatDateTime(authSession.expiresAt)}</dd></div>
            <div className="fact-row"><dt>Plan</dt><dd>{billing?.planId || context?.planId || 'Unknown'}</dd></div>
            <div className="fact-row"><dt>Token Count</dt><dd>{`${activeTokenCount} active of ${tokens.length}`}</dd></div>
          </dl>

          <p className="summary-detail">{accountActionMessage || 'Session details appear after sign-in.'}</p>
          <div className="action-row">
            <button type="button" className="button" onClick={onRefreshSession}>Refresh Session</button>
            <button type="button" className="button button-danger" onClick={onSignOut}>Sign Out</button>
          </div>
        </section>

        <section className="panel">
          <div className="panel-header">
            <div>
              <h3>Create Token</h3>
              <p className="summary-detail">New token values are shown once. Save them before dismissing the panel.</p>
            </div>
          </div>

          <div className="create-token-form">
            <label className="field">
              <span>Name</span>
              <input type="text" placeholder="Claude Desktop" value={tokenNameInput} onChange={(event) => onTokenNameInputChange(event.target.value)} />
            </label>
            <label className="field">
              <span>Expires At</span>
              <input type="datetime-local" value={tokenExpiresAtInput} onChange={(event) => onTokenExpiresAtInputChange(event.target.value)} />
            </label>
            <div className="scope-fieldset">
              <p className="panel-label">Scopes</p>
              <label className="scope-option">
                <input type="checkbox" checked readOnly />
                <span><code>mcp:read</code> is always included.</span>
              </label>
              <label className="scope-option">
                <input type="checkbox" checked={requestWriteScope} onChange={(event) => onRequestWriteScopeChange(event.target.checked)} />
                <span>Request <code>mcp:write</code> when the effective plan allows it.</span>
              </label>
            </div>
            <button type="button" className="button button-primary" onClick={onCreateToken}>Create Token</button>
          </div>

          {createdToken ? (
            <section className="created-token-panel">
              <div className="panel-header compact-header">
                <div>
                  <h3>New Token</h3>
                  <p className="summary-detail">{createdToken.meta}</p>
                </div>
                <button type="button" className="button" onClick={onDismissCreatedToken}>Dismiss</button>
              </div>
              <textarea readOnly rows={4} value={createdToken.token} className="document-content-editor" />
              <div className="action-row">
                <button type="button" className="button button-primary" onClick={onCopyCreatedToken}>Copy Token</button>
              </div>
            </section>
          ) : null}
        </section>
      </section>

      <section className="panel">
        <div className="panel-header">
          <div>
            <h3>Issued Tokens</h3>
            <p className="summary-detail">Revocation is immediate. Expired or revoked entries stay visible for audit.</p>
          </div>
        </div>

        {tokens.length === 0 ? (
          <div className="empty-state">No tokens have been issued for this workspace yet.</div>
        ) : (
          <div className="token-record-list">
            {tokens.map((token) => {
              const status = getTokenStatus(token);
              return (
                <article key={token.apiTokenId} className="token-record-card">
                  <div className="token-record-header">
                    <div>
                      <h3>{token.name}</h3>
                      <p className="summary-detail">{token.apiTokenId}</p>
                    </div>
                    <span className={`status-chip ${status.className}`}>{status.label}</span>
                  </div>
                  <div className="token-record-meta">
                    <span>{token.tokenPrefix}</span>
                    <span>{(token.scopes || []).join(', ')}</span>
                    <span>{formatDateTime(token.createdAt || undefined)}</span>
                    <span>{formatDateTime(token.lastUsedAt || undefined)}</span>
                    <span>{formatDateTime(token.expiresAt || undefined)}</span>
                  </div>
                  {!token.revokedAt ? (
                    <div className="action-row">
                      <button type="button" className="button button-danger" onClick={() => onRevokeToken(token.apiTokenId)}>Revoke</button>
                    </div>
                  ) : null}
                </article>
              );
            })}
          </div>
        )}
      </section>
    </section>
  );
}
type UsageViewProps = {
  activeBrainId: string;
  authSession: StoredAuthSession;
  billing: PortalBilling | null;
  context: PortalContext | null;
};

function UsageView({ activeBrainId, authSession, billing, context }: UsageViewProps) {
  return (
    <section className="portal-layout">
      <article className="panel portal-hero">
        <p className="eyebrow">Usage</p>
        <h2>Operational visibility is now using real workspace data.</h2>
        <p className="summary-detail">
          The Usage view now mirrors the current portal posture for document quota, MCP query usage, default brain context, and session expiry.
        </p>
      </article>

      <section className="summary-grid">
        <article className="summary-card">
          <p className="panel-label">Documents</p>
          <strong>{billing ? `${billing.activeDocuments} / ${formatLimit(billing.maxDocuments)}` : 'Loading'}</strong>
          <p>{billing ? 'Active managed-content documents in this workspace.' : 'Fetching document quota...'}</p>
        </article>
        <article className="summary-card">
          <p className="panel-label">MCP Queries</p>
          <strong>{billing ? `${billing.mcpQueriesUsed} / ${formatLimit(billing.mcpQueriesPerMonth)}` : 'Loading'}</strong>
          <p>{billing ? 'Current monthly MCP query posture.' : 'Fetching MCP usage...'}</p>
        </article>
        <article className="summary-card">
          <p className="panel-label">Default Brain</p>
          <strong>{context?.brainName || 'Loading'}</strong>
          <p>{activeBrainId ? `Current selected brain ${activeBrainId}.` : 'Fetching default brain...'}</p>
        </article>
        <article className="summary-card">
          <p className="panel-label">Session</p>
          <strong>{formatDateTime(authSession.expiresAt)}</strong>
          <p>{context ? `Signed in as ${context.email}.` : 'Browser session is active.'}</p>
        </article>
      </section>

      <section className="panel notes-panel">
        <div className="panel-header">
          <div>
            <h3>Usage Notes</h3>
            <p className="summary-detail">Operational visibility now sits beside the rest of the portal instead of living in a separate shell.</p>
          </div>
        </div>

        <div className="notes-list">
          <article className="note-card">The active workspace plan stays visible in the shared shell above.</article>
          <article className="note-card">The selected document brain follows the same managed-content filtering as the legacy shell.</article>
        </div>
      </section>
    </section>
  );
}
type DocumentsViewProps = {
  activeBrain: BrainSummary | null;
  activeBrainId: string;
  brains: BrainSummary[];
  documentDraft: DocumentDraft;
  documentError: string | null;
  documentFilter: string;
  documentGroups: DocumentGroup[];
  documentIsDirty: boolean;
  documentLoading: boolean;
  documentSaveMessage: string;
  documentSaveState: DocumentSaveState;
  documentVersions: DocumentVersionSummary[];
  documents: DocumentSummary[];
  documentsError: string | null;
  documentsLoading: boolean;
  filteredDocuments: DocumentSummary[];
  isCreatingDocument: boolean;
  onChangeBrain: (brainId: string) => void;
  onChangeDocumentFilter: (filter: string) => void;
  onCreateDocument: () => void;
  onDeleteDocument: () => void;
  onDraftChange: <K extends keyof DocumentDraft>(field: K, value: DocumentDraft[K]) => void;
  onExportDocument: () => void;
  onImportDocument: (file: File | null) => void;
  onRefreshDocuments: () => void;
  onRefreshVersions: () => void;
  onRestoreVersion: () => void;
  onRevertDocument: () => void;
  onSaveDocument: () => void;
  onSelectDocument: (documentId: string) => void;
  onSelectVersion: (versionId: string) => void;
  selectedDocument: DocumentDetail | null;
  selectedDocumentId: string | null;
  selectedVersion: DocumentVersionDetail | null;
  selectedVersionId: string | null;
  versionError: string | null;
  versionLoading: boolean;
  versionsError: string | null;
  versionsLoading: boolean;
};

function DocumentsView({
  activeBrain,
  activeBrainId,
  brains,
  documentDraft,
  documentError,
  documentFilter,
  documentGroups,
  documentIsDirty,
  documentLoading,
  documentSaveMessage,
  documentSaveState,
  documentVersions,
  documents,
  documentsError,
  documentsLoading,
  filteredDocuments,
  isCreatingDocument,
  onChangeBrain,
  onChangeDocumentFilter,
  onCreateDocument,
  onDeleteDocument,
  onDraftChange,
  onExportDocument,
  onImportDocument,
  onRefreshDocuments,
  onRefreshVersions,
  onRestoreVersion,
  onRevertDocument,
  onSaveDocument,
  onSelectDocument,
  onSelectVersion,
  selectedDocument,
  selectedDocumentId,
  selectedVersion,
  selectedVersionId,
  versionError,
  versionLoading,
  versionsError,
  versionsLoading
}: DocumentsViewProps) {
  const importInputRef = useRef<HTMLInputElement | null>(null);
  const renderedDocumentMarkup = useMemo(
    () => ({ __html: renderMarkdown(documentDraft.content || '') }),
    [documentDraft.content]
  );
  const renderedVersionMarkup = useMemo(
    () => ({ __html: renderMarkdown(selectedVersion?.content || '') }),
    [selectedVersion]
  );
  const saveStatusClassName = [
    'document-save-status',
    documentSaveState === 'saving' || documentSaveState === 'info'
      ? 'status-info'
      : documentSaveState === 'error'
        ? 'status-error'
        : documentSaveState === 'warn'
          ? 'status-warn'
          : ''
  ].filter(Boolean).join(' ');
  const detailTitle = isCreatingDocument
    ? 'New Document'
    : (selectedDocument?.title || 'Document Editor');
  const detailMeta = isCreatingDocument
    ? (documentIsDirty ? 'Unsaved draft for the active managed-content brain.' : 'Ready to create a new managed-content document.')
    : selectedDocument
      ? `${selectedDocument.status || 'draft'} | ${selectedDocument.slug || '(no slug)'} | ${selectedDocument.canonicalPath || '(no canonical path)'} | updated ${formatDateTime(selectedDocument.updatedAt)}`
      : 'Select a document to inspect or edit its content.';

  return (
    <section className="portal-layout">
      <article className="panel portal-hero">
        <p className="eyebrow">Documents</p>
        <h2>Managed-content authoring is now part of the main portal experience.</h2>
        <p className="summary-detail">
          The document rail, editor draft, export flow, save path, delete path, and version history all use the tenant APIs directly.
        </p>
      </article>

      <section className="panel workspace-toolbar">
        <label className="field">
          <span>Brain</span>
          <select value={activeBrainId} onChange={(event) => onChangeBrain(event.target.value)} disabled={brains.length === 0}>
            {brains.length === 0 ? <option value="">No brains available</option> : null}
            {brains.map((brain) => (
              <option key={brain.brainId} value={brain.brainId}>
                {brain.name} | {brain.mode} | {brain.status}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>Filter</span>
          <input
            type="search"
            placeholder="Filter by title, slug, or path"
            value={documentFilter}
            onChange={(event) => onChangeDocumentFilter(event.target.value)}
          />
        </label>
        <div className="action-row toolbar-actions">
          <button type="button" className="button button-primary" onClick={onCreateDocument} disabled={!activeBrainId || documentsLoading}>
            New Document
          </button>
          <button type="button" className="button" onClick={() => importInputRef.current?.click()} disabled={!activeBrainId || documentsLoading}>
            Import Markdown
          </button>
          <button type="button" className="button" onClick={onRefreshDocuments} disabled={!activeBrainId || documentsLoading}>
            Refresh Documents
          </button>
          <input
            ref={importInputRef}
            type="file"
            accept=".md,text/markdown"
            className="hidden-input"
            onChange={(event) => {
              const file = event.target.files?.[0] || null;
              onImportDocument(file);
              event.target.value = '';
            }}
          />
        </div>
      </section>

      {documentsError ? <section className="banner error-banner" role="alert">{documentsError}</section> : null}
      {documentError ? <section className="banner error-banner" role="alert">{documentError}</section> : null}
      {versionsError ? <section className="banner error-banner" role="alert">{versionsError}</section> : null}
      {versionError ? <section className="banner error-banner" role="alert">{versionError}</section> : null}
      {documentsLoading ? <section className="banner info-banner">Refreshing document rail...</section> : null}

      <section className="documents-layout">
        <aside className="panel rail-panel">
          <div className="panel-header compact-header">
            <div>
              <h3>Document List</h3>
              <p className="summary-detail">
                {activeBrain
                  ? `${documents.length} document(s) loaded for ${activeBrain.name}.`
                  : 'Select a managed-content brain to inspect its documents.'}
              </p>
            </div>
          </div>

          {!activeBrainId ? (
            <div className="empty-state">No managed-content brain is available for this workspace yet.</div>
          ) : filteredDocuments.length === 0 ? (
            <div className="empty-state">
              {documents.length === 0 ? 'No documents exist in this brain yet.' : 'No documents match the current filter.'}
            </div>
          ) : (
            <div className="document-list" aria-label="Managed document list">
              {documentGroups.map((group) => (
                <section key={group.directoryPath || '__root'} className="document-folder-group">
                  <div
                    className={group.directoryPath ? 'document-folder-header' : 'document-folder-header root-folder'}
                    style={group.directoryPath ? { paddingLeft: `${group.depth * 16}px` } : undefined}
                  >
                    <span className="document-folder-name">{group.directoryPath ? group.label : 'Root'}</span>
                    <span className="document-folder-count">{group.documents.length}</span>
                  </div>

                  {group.documents.map((document) => {
                    const fileName = getDocumentFileName(document);
                    const pathDisplay = document.canonicalPath || `${document.slug || fileName}.md`;
                    const selected = !isCreatingDocument && document.managedDocumentId === selectedDocumentId;

                    return (
                      <button
                        key={document.managedDocumentId}
                        type="button"
                        className={selected ? 'document-list-item selected-row' : 'document-list-item'}
                        style={{ marginLeft: `${group.depth * 16}px` }}
                        onClick={() => onSelectDocument(document.managedDocumentId)}
                      >
                        <span className="document-list-title">{fileName}</span>
                        <span className="document-list-subtitle">{document.title || '(untitled)'}</span>
                        <span className="document-list-meta">
                          <span>{document.status || 'draft'}</span>
                          <span>{pathDisplay}</span>
                          <span>{formatDateTime(document.updatedAt)}</span>
                        </span>
                      </button>
                    );
                  })}
                </section>
              ))}
            </div>
          )}
        </aside>

        <section className="panel editor-panel">
          <div className="panel-header compact-header">
            <div>
              <h3>{detailTitle}</h3>
              <p className="summary-detail">{detailMeta}</p>
            </div>
            <div className="action-row">
              <button type="button" className="button button-primary" onClick={onSaveDocument} disabled={!activeBrainId || documentSaveState === 'saving'}>
                Save Document
              </button>
              <button type="button" className="button" onClick={onExportDocument}>
                Export Markdown
              </button>
              <button type="button" className="button" onClick={onRevertDocument}>
                Revert
              </button>
              <button type="button" className="button button-danger" onClick={onDeleteDocument} disabled={isCreatingDocument || !selectedDocument}>
                Delete
              </button>
            </div>
          </div>

          <p className={saveStatusClassName}>{documentSaveMessage}</p>
          {documentLoading ? <p className="document-save-status">Loading selected document...</p> : null}

          <div className="document-editor-grid">
            <label className="field">
              <span>Title</span>
              <input
                type="text"
                placeholder="Untitled document"
                value={documentDraft.title}
                onChange={(event) => onDraftChange('title', event.target.value)}
                disabled={!activeBrainId}
              />
            </label>
            <label className="field">
              <span>Filename / Path</span>
              <input
                type="text"
                placeholder="folder/file-name"
                value={documentDraft.slug}
                onChange={(event) => onDraftChange('slug', event.target.value)}
                disabled={!activeBrainId}
              />
            </label>
            <label className="field">
              <span>Status</span>
              <select
                value={documentDraft.status}
                onChange={(event) => onDraftChange('status', event.target.value)}
                disabled={!activeBrainId}
              >
                <option value="draft">draft</option>
                <option value="published">published</option>
                <option value="archived">archived</option>
              </select>
            </label>
          </div>

          <div className="document-detail-stack">
            <label className="field">
              <span>Frontmatter</span>
              <textarea
                className="document-frontmatter-editor"
                rows={6}
                placeholder="category: reference&#10;owner: steph"
                value={documentDraft.frontmatterText}
                onChange={(event) => onDraftChange('frontmatterText', event.target.value)}
                disabled={!activeBrainId}
              />
            </label>

            <label className="field">
              <span>Markdown Content</span>
              <textarea
                className="document-content-editor"
                rows={16}
                placeholder="Write Markdown content here."
                value={documentDraft.content}
                onChange={(event) => onDraftChange('content', event.target.value)}
                disabled={!activeBrainId}
              />
            </label>

            <section className="rendered-section">
              <div className="panel-header compact-header">
                <div>
                  <h3>Rendered Document</h3>
                  <p className="summary-detail">Live browser rendering of the current Markdown draft.</p>
                </div>
              </div>
              <div className="markdown-preview" dangerouslySetInnerHTML={renderedDocumentMarkup} />
            </section>

            <section className="version-section">
              <div className="panel-header compact-header">
                <div>
                  <h3>Version History</h3>
                  <p className="summary-detail">Saved snapshots for the current document. Select one to expand it. Only one version stays open at a time.</p>
                </div>
                <div className="action-row">
                  <button type="button" className="button" onClick={onRefreshVersions} disabled={!selectedDocument || isCreatingDocument || versionsLoading}>
                    Refresh Versions
                  </button>
                </div>
              </div>

              {versionsLoading ? <p className="document-save-status">Loading version history...</p> : null}

              {isCreatingDocument ? (
                <div className="empty-state">Save the new document to start accumulating versions.</div>
              ) : !selectedDocument ? (
                <div className="empty-state">Select a document to load version history.</div>
              ) : documentVersions.length === 0 ? (
                <div className="empty-state">No saved versions exist for this document yet.</div>
              ) : (
                <div className="version-list" role="list" aria-label="Managed document version list">
                  {documentVersions.map((version) => {
                    const selected = version.managedDocumentVersionId === selectedVersionId;
                    const showInlinePreview = selected && selectedVersion?.managedDocumentVersionId === version.managedDocumentVersionId;
                    const isCurrentVersion = isCurrentDocumentVersion(selectedDocument, version);

                    return (
                      <article key={version.managedDocumentVersionId} className={selected ? 'version-card selected-row' : 'version-card'}>
                        <button
                          type="button"
                          className="version-list-item"
                          aria-expanded={selected}
                          onClick={() => onSelectVersion(version.managedDocumentVersionId)}
                        >
                          <span className="version-list-title">{formatDateTime(version.createdAt)}</span>
                          <span className="version-list-meta">
                            <span>{version.snapshotKind || 'snapshot'}</span>
                            <span>{version.status || 'draft'}</span>
                            <span>{String(version.wordCount || 0)} words</span>
                            <span>{version.snapshotBy || 'Unknown'}</span>
                          </span>
                        </button>

                        {selected ? (
                          <section className="version-card-body">
                            <div className="panel-header compact-header">
                              <div>
                                <h3>Rendered Version</h3>
                                <p className="summary-detail">
                                  {selectedVersion
                                    ? `${selectedVersion.snapshotKind || 'snapshot'} | ${selectedVersion.status || 'draft'} | ${formatDateTime(selectedVersion.createdAt)} | ${selectedVersion.snapshotBy || 'Unknown'}`
                                    : 'Loading selected version...'}
                                </p>
                              </div>
                              <div className="action-row">
                                <button type="button" className="button" onClick={onRestoreVersion} disabled={isCurrentVersion || !showInlinePreview || versionLoading}>
                                  {isCurrentVersion ? 'Current Version' : 'Restore This Version'}
                                </button>
                              </div>
                            </div>
                            {versionLoading && !showInlinePreview ? <p className="document-save-status">Loading selected version...</p> : null}
                            <div className="markdown-preview version-preview" dangerouslySetInnerHTML={showInlinePreview ? renderedVersionMarkup : { __html: '<p>Loading selected version...</p>' }} />
                          </section>
                        ) : null}
                      </article>
                    );
                  })}
                </div>
              )}
            </section>
          </div>
        </section>
      </section>
    </section>
  );
}
function navigateToView(view: PortalView, authSession: StoredAuthSession | null, setActiveView: (view: PortalView) => void) {
  const resolved = canNavigateToView(view, authSession) ? view : resolveDefaultView(authSession);
  const nextHash = `#${resolved}`;

  if (window.location.hash !== nextHash) {
    window.location.hash = nextHash;
  }

  setActiveView(resolved);
}

function normalizeView(value: string) {
  const normalized = value.toLowerCase();
  return orderedViews.includes(normalized as PortalView) ? (normalized as PortalView) : resolveDefaultView(loadStoredAuthSession());
}

function resolveDefaultView(authSession: StoredAuthSession | null) {
  return authSession ? 'documents' : 'signin';
}

function canNavigateToView(view: PortalView, authSession: StoredAuthSession | null) {
  if (!authSession) {
    return view === 'signin';
  }

  return view !== 'signin';
}

function resolveViewFromHash(hash: string, authSession: StoredAuthSession | null): PortalView {
  const requested = normalizeView(hash.replace(/^#/, ''));
  if (!authSession) {
    return 'signin';
  }

  if (requested === 'signin') {
    return 'documents';
  }

  return requested;
}

function loadStoredAuthSession(): StoredAuthSession | null {
  const raw = window.localStorage.getItem(storageKey);
  if (!raw) {
    return null;
  }

  try {
    return JSON.parse(raw) as StoredAuthSession;
  } catch {
    window.localStorage.removeItem(storageKey);
    return null;
  }
}

function clearStoredAuthSession() {
  window.localStorage.removeItem(storageKey);
}

async function ensureValidSession(session: StoredAuthSession) {
  if (isSessionFresh(session)) {
    return session;
  }

  const refreshed = await postJson('/portal-auth/refresh', { refreshToken: session.refreshToken }) as { idToken: string; refreshToken?: string; expiresIn: string; };
  const updatedSession: StoredAuthSession = {
    ...session,
    idToken: refreshed.idToken,
    refreshToken: refreshed.refreshToken || session.refreshToken,
    expiresAt: buildExpiryTimestamp(refreshed.expiresIn)
  };

  window.localStorage.setItem(storageKey, JSON.stringify(updatedSession));
  return updatedSession;
}

async function portalFetch(url: string, idToken: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers || {});
  headers.set('Authorization', `Bearer ${idToken}`);

  if (init.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  const response = await fetch(url, {
    ...init,
    headers,
  });

  const contentType = response.headers.get('content-type') || '';
  const payload = contentType.includes('application/json') ? await response.json() : await response.text();
  if (!response.ok) {
    throw new Error(extractErrorMessage(payload, response.status));
  }

  return payload;
}

async function postJson(url: string, body: unknown) {
  const response = await fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(body)
  });

  const contentType = response.headers.get('content-type') || '';
  const payload = contentType.includes('application/json') ? await response.json() : await response.text();
  if (!response.ok) {
    throw new Error(extractErrorMessage(payload, response.status));
  }

  return payload;
}

function extractErrorMessage(payload: unknown, status: number) {
  if (typeof payload === 'string' && payload.trim()) {
    return payload;
  }

  if (typeof payload === 'object' && payload !== null) {
    const candidate = payload as { detail?: string; title?: string; message?: string };
    if (candidate.detail) {
      return `${candidate.title || 'Request failed'}: ${candidate.detail}`;
    }

    if (candidate.message) {
      return candidate.message;
    }

    if (candidate.title) {
      return candidate.title;
    }
  }

  return `Request failed with HTTP ${status}.`;
}

function buildExpiryTimestamp(expiresInSeconds: string) {
  const seconds = Number.parseInt(expiresInSeconds, 10);
  const safeSeconds = Number.isFinite(seconds) ? seconds : 3600;
  return new Date(Date.now() + safeSeconds * 1000).toISOString();
}

function isSessionFresh(session: StoredAuthSession) {
  const expiresAt = new Date(session.expiresAt).valueOf();
  if (Number.isNaN(expiresAt)) {
    return false;
  }

  return expiresAt - Date.now() > sessionRefreshSkewMs;
}

function selectActiveBrainId(current: string, brains: BrainSummary[], context: PortalContext | null) {
  if (current && brains.some((brain) => brain.brainId === current)) {
    return current;
  }

  const preferred = context?.brainId || '';
  if (preferred && brains.some((brain) => brain.brainId === preferred)) {
    return preferred;
  }

  return brains[0]?.brainId || '';
}

function applyDocumentFilter(documents: DocumentSummary[], filter: string) {
  const normalized = filter.trim().toLowerCase();
  if (!normalized) {
    return [...documents];
  }

  return documents.filter((document) =>
    String(document.title || '').toLowerCase().includes(normalized)
    || String(document.slug || '').toLowerCase().includes(normalized)
    || String(document.canonicalPath || '').toLowerCase().includes(normalized)
  );
}

function buildDocumentDirectoryGroups(documents: DocumentSummary[]): DocumentGroup[] {
  const groups = new Map<string, DocumentSummary[]>();

  for (const document of documents) {
    const directoryPath = getDocumentDirectoryPath(document);
    if (!groups.has(directoryPath)) {
      groups.set(directoryPath, []);
    }

    groups.get(directoryPath)?.push(document);
  }

  return Array.from(groups.entries())
    .sort((left, right) => {
      if (!left[0]) {
        return -1;
      }

      if (!right[0]) {
        return 1;
      }

      return left[0].localeCompare(right[0]);
    })
    .map(([directoryPath, groupedDocuments]) => ({
      directoryPath,
      depth: directoryPath ? directoryPath.split('/').length : 0,
      label: directoryPath ? (directoryPath.split('/').at(-1) || 'Root') : 'Root',
      documents: groupedDocuments.sort((left, right) => getDocumentFileName(left).localeCompare(getDocumentFileName(right)))
    }));
}

function getDocumentDirectoryPath(document: DocumentSummary) {
  const slug = String(document.slug || '').replace(/\\/g, '/');
  const lastSlash = slug.lastIndexOf('/');
  return lastSlash <= 0 ? '' : slug.slice(0, lastSlash);
}

function getDocumentFileName(document: DocumentSummary) {
  const slug = String(document.slug || document.title || 'document').replace(/\\/g, '/');
  const baseName = slug.split('/').at(-1) || 'document';
  return baseName || 'document';
}

function buildEmptyDocumentDraft(): DocumentDraft {
  return {
    title: '',
    slug: '',
    status: 'draft',
    frontmatterText: '',
    content: '',
  };
}

function buildDraftFromDocument(document: DocumentDetail): DocumentDraft {
  return {
    title: document.title || '',
    slug: document.slug || '',
    status: document.status || 'draft',
    frontmatterText: serializeFrontmatter(document.frontmatter || {}),
    content: document.content || '',
  };
}

function normalizeDocumentDraft(draft: DocumentDraft): DocumentDraft {
  return {
    title: String(draft.title || '').trim(),
    slug: String(draft.slug || '').trim(),
    status: String(draft.status || 'draft').trim() || 'draft',
    frontmatterText: String(draft.frontmatterText || '').replace(/\r\n/g, '\n').trim(),
    content: String(draft.content || '').replace(/\r\n/g, '\n'),
  };
}

function hasUnsavedDocumentChanges(draft: DocumentDraft, isCreatingDocument: boolean, selectedDocument: DocumentDetail | null) {
  const currentDraft = normalizeDocumentDraft(draft);
  if (isCreatingDocument) {
    return JSON.stringify(currentDraft) !== JSON.stringify(normalizeDocumentDraft(buildEmptyDocumentDraft()));
  }

  if (!selectedDocument) {
    return false;
  }

  return JSON.stringify(currentDraft) !== JSON.stringify(normalizeDocumentDraft(buildDraftFromDocument(selectedDocument)));
}

function confirmDiscardDocumentChanges(isDirty: boolean, message: string) {
  if (!isDirty) {
    return true;
  }

  return window.confirm(message);
}

function buildDocumentPayload(draft: DocumentDraft) {
  if (!draft.title) {
    throw new Error('Title is required.');
  }

  return {
    title: draft.title,
    slug: draft.slug || null,
    status: draft.status || 'draft',
    content: draft.content || '',
    frontmatter: parseFrontmatterText(draft.frontmatterText),
  };
}

function parseImportedMarkdown(markdown: string, fileName: string): DocumentDraft {
  const normalized = String(markdown || '').replace(/\r\n/g, '\n');
  let frontmatterText = '';
  let content = normalized;

  if (normalized.startsWith('---\n')) {
    const endIndex = normalized.indexOf('\n---\n', 4);
    const alternateEndIndex = normalized.indexOf('\n...\n', 4);
    const closingIndex = endIndex >= 0 ? endIndex : alternateEndIndex;

    if (closingIndex < 0) {
      throw new Error('Imported Markdown frontmatter is missing a closing --- or ... line.');
    }

    frontmatterText = normalized.slice(4, closingIndex).trim();
    content = normalized.slice(closingIndex + 5);
  }

  const parsedFrontmatter = parseFrontmatterText(frontmatterText);
  const title = deriveImportedDocumentTitle(content, parsedFrontmatter, fileName);
  const slug = deriveImportedDocumentSlug(parsedFrontmatter, title, fileName);

  return {
    title,
    slug,
    status: 'draft',
    frontmatterText: serializeFrontmatter(parsedFrontmatter),
    content: content.replace(/^\n+/, ''),
  };
}

function deriveImportedDocumentTitle(content: string, frontmatter: Record<string, string>, fileName: string) {
  const frontmatterTitle = String(frontmatter.title || '').trim();
  if (frontmatterTitle) {
    return frontmatterTitle;
  }

  const headingMatch = String(content || '').match(/^\s*#\s+(.+)$/m);
  if (headingMatch?.[1]) {
    return headingMatch[1].trim();
  }

  const fileStem = String(fileName || '').replace(/\.[^.]+$/, '').trim();
  if (fileStem) {
    return fileStem;
  }

  return 'Imported document';
}

function deriveImportedDocumentSlug(frontmatter: Record<string, string>, title: string, fileName: string) {
  const explicitSlug = String(frontmatter.slug || '').trim();
  if (explicitSlug) {
    return normalizeDocumentPath(explicitSlug);
  }

  const titleSlug = normalizeDocumentPath(title);
  if (titleSlug) {
    return titleSlug;
  }

  return normalizeDocumentPath(String(fileName || '').replace(/\.[^.]+$/, ''));
}

function buildMarkdownExport(draft: DocumentDraft, selectedDocument: DocumentDetail | null) {
  const frontmatter = parseFrontmatterText(draft.frontmatterText);
  const parts: string[] = [];

  if (Object.keys(frontmatter).length > 0) {
    parts.push('---');
    parts.push(serializeFrontmatter(frontmatter));
    parts.push('---');
    parts.push('');
  }

  parts.push(String(draft.content || '').replace(/\r\n/g, '\n').replace(/\s+$/, ''));
  return parts.join('\n').replace(/\n{3,}/g, '\n\n').trimEnd() + '\n';
}

function buildDocumentExportFileName(draft: DocumentDraft, selectedDocument: DocumentDetail | null) {
  const slug = normalizeDocumentPath(draft.slug || draft.title || selectedDocument?.slug || selectedDocument?.title || 'document');
  return `${slug || 'document'}.md`;
}

function normalizeDocumentPath(value: string) {
  const normalized = String(value || '')
    .trim()
    .replace(/\\/g, '/')
    .replace(/\.md$/i, '');

  if (!normalized) {
    return '';
  }

  return normalized
    .split('/')
    .map((segment) => String(segment || '')
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, ''))
    .filter(Boolean)
    .join('/');
}

function downloadTextFile(fileName: string, content: string, mimeType: string) {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

function parseFrontmatterText(value: string) {
  const result: Record<string, string> = {};
  const lines = String(value || '').split(/\r?\n/);

  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index].trim();
    if (!line || line === '---' || line === '...' || line.startsWith('#')) {
      continue;
    }

    const separatorIndex = line.indexOf(':');
    if (separatorIndex <= 0) {
      throw new Error(`Frontmatter line ${index + 1} must use 'key: value'.`);
    }

    const key = line.slice(0, separatorIndex).trim();
    const lineValue = line.slice(separatorIndex + 1).trim();
    if (!key) {
      throw new Error(`Frontmatter line ${index + 1} is missing a key.`);
    }

    result[key] = lineValue;
  }

  return result;
}

function serializeFrontmatter(frontmatter: Record<string, unknown>) {
  return Object.entries(frontmatter)
    .map(([key, value]) => `${key}: ${value}`)
    .join('\n');
}
function isCurrentDocumentVersion(document: DocumentDetail | null, version: DocumentVersionSummary | DocumentVersionDetail) {
  if (!document?.updatedAt || !version.createdAt) {
    return false;
  }

  const documentTimestamp = new Date(document.updatedAt).valueOf();
  const versionTimestamp = new Date(version.createdAt).valueOf();
  if (Number.isNaN(documentTimestamp) || Number.isNaN(versionTimestamp)) {
    return false;
  }

  return documentTimestamp === versionTimestamp;
}
function getTokenStatus(token: TokenSummary) {
  if (token.revokedAt) {
    return { label: 'Revoked', className: 'status-chip-revoked' };
  }

  if (token.expiresAt && new Date(token.expiresAt) <= new Date()) {
    return { label: 'Expired', className: 'status-chip-expired' };
  }

  return { label: 'Active', className: 'status-chip-active' };
}

function parseExpiresAt(value: string) {
  if (!value) {
    return null;
  }

  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? null : date.toISOString();
}
function renderMarkdown(markdown: string) {
  const source = String(markdown || '').replace(/\r\n/g, '\n').trim();
  if (!source) {
    return '<p>Nothing to preview yet.</p>';
  }

  const lines = source.split('\n');
  const blocks: string[] = [];
  let paragraph: string[] = [];
  let listItems: string[] = [];
  let codeFence: string[] | null = null;

  const flushParagraph = () => {
    if (paragraph.length === 0) {
      return;
    }

    blocks.push(`<p>${renderInlineMarkdown(paragraph.join(' '))}</p>`);
    paragraph = [];
  };

  const flushList = () => {
    if (listItems.length === 0) {
      return;
    }

    blocks.push(`<ul>${listItems.map((item) => `<li>${renderInlineMarkdown(item)}</li>`).join('')}</ul>`);
    listItems = [];
  };

  const flushCodeFence = () => {
    if (codeFence === null) {
      return;
    }

    blocks.push(`<pre><code>${escapeHtml(codeFence.join('\n'))}</code></pre>`);
    codeFence = null;
  };

  for (const line of lines) {
    if (line.startsWith('```')) {
      flushParagraph();
      flushList();
      if (codeFence === null) {
        codeFence = [];
      } else {
        flushCodeFence();
      }

      continue;
    }

    if (codeFence !== null) {
      codeFence.push(line);
      continue;
    }

    const trimmed = line.trim();
    if (!trimmed) {
      flushParagraph();
      flushList();
      continue;
    }

    const headingMatch = trimmed.match(/^(#{1,3})\s+(.*)$/);
    if (headingMatch) {
      flushParagraph();
      flushList();
      const level = headingMatch[1].length;
      blocks.push(`<h${level}>${renderInlineMarkdown(headingMatch[2])}</h${level}>`);
      continue;
    }

    if (trimmed.startsWith('- ') || trimmed.startsWith('* ')) {
      flushParagraph();
      listItems.push(trimmed.slice(2).trim());
      continue;
    }

    if (trimmed.startsWith('> ')) {
      flushParagraph();
      flushList();
      blocks.push(`<blockquote><p>${renderInlineMarkdown(trimmed.slice(2).trim())}</p></blockquote>`);
      continue;
    }

    paragraph.push(trimmed);
  }

  flushParagraph();
  flushList();
  flushCodeFence();
  return blocks.join('');
}

function renderInlineMarkdown(value: string) {
  return escapeHtml(value)
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/\*([^*]+)\*/g, '<em>$1</em>')
    .replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g, '<a href="$2" target="_blank" rel="noreferrer">$1</a>');
}

function escapeHtml(value: unknown) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function formatDateTime(value?: string) {
  if (!value) {
    return 'Unknown';
  }

  return new Date(value).toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit'
  });
}

function formatLimit(value: number) {
  return value < 0 ? 'unlimited' : String(value);
}

function formatDocumentQuota(billing: PortalBilling) {
  return `${billing.activeDocuments} active of ${formatLimit(billing.maxDocuments)} documents`;
}

function handleClearSession() {
  clearStoredAuthSession();
  window.location.hash = '#signin';
  window.location.reload();
}

export default App;














































