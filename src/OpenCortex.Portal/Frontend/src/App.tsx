import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ChatView } from './ChatView';
import { DocumentsView } from './DocumentsView';
import { MemoriesView } from './MemoriesView';
import { normalizeDocumentLinkPath } from './documentLinks';
import { normalizeDocumentDraft, normalizeDocumentPath } from './documentDraft';
import { useManagedDocumentWorkspace } from './useManagedDocumentWorkspace';

type PortalView = 'signin' | 'documents' | 'memories' | 'chat' | 'account' | 'usage' | 'tools';

type PortalConfig = {
  apiBaseUrlConfigured: boolean;
  hostedAuthConfigured: boolean;
  firebaseProjectId?: string;
  firebaseApiKey?: string;
  firebaseAuthDomain?: string;
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

type PortalAuthResponse = {
  idToken: string;
  refreshToken: string;
  email: string;
  displayName?: string;
  expiresAt?: string;
  expiresIn: string;
};

type AuthPendingAction = 'signin' | 'register' | 'google' | null;

type FirebaseUser = {
  getIdToken(): Promise<string>;
  getIdTokenResult(): Promise<{ expirationTime?: string } | null>;
  refreshToken?: string;
  email?: string | null;
  displayName?: string | null;
};

type FirebaseUserCredential = {
  user?: FirebaseUser | null;
};

type FirebaseAuthProvider = {
  setCustomParameters(parameters: Record<string, string>): void;
};

type FirebaseAuthInstance = {
  signInWithPopup(provider: FirebaseAuthProvider): Promise<FirebaseUserCredential>;
  signOut(): Promise<void>;
};

type FirebaseAuthFactory = {
  (): FirebaseAuthInstance;
  GoogleAuthProvider: new () => FirebaseAuthProvider;
};

type FirebaseNamespace = {
  apps: unknown[];
  initializeApp(config: { apiKey?: string; authDomain?: string; projectId?: string }): unknown;
  auth: FirebaseAuthFactory;
};

declare global {
  interface Window {
    firebase?: FirebaseNamespace;
  }
}

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
  slug?: string;
  name: string;
  mode: string;
  status: string;
};

type MemoryBrainPreference = {
  configuredMemoryBrainId?: string | null;
  effectiveMemoryBrainId?: string | null;
  needsConfiguration?: boolean;
  error?: string | null;
};

export function resolveDocumentLinkBrainId(
  canonicalPath: string,
  activeBrainId: string,
  effectiveMemoryBrainId?: string | null,
) {
  if (canonicalPath.startsWith('memories/')) {
    return effectiveMemoryBrainId || '';
  }

  return activeBrainId;
}

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

type ProviderSettings = {
  defaultModel?: string | null;
  baseUrl?: string | null;
  maxTokens?: number | null;
  temperature?: number | null;
};

type ConfiguredProviderSummary = {
  providerId: string;
  authType?: string;
  isEnabled: boolean;
  hasCredentials: boolean;
  settings?: ProviderSettings | null;
  tokenExpiresAt?: string | null;
  createdAt?: string | null;
  updatedAt?: string | null;
};

type AvailableProvider = {
  providerId: string;
  name: string;
  authTypes: string[];
  defaultModel: string;
  configUrl?: string | null;
  oauthConfigured?: boolean;
  OAuthConfigured?: boolean;
};

type AvailableProviderResponse = {
  providers?: AvailableProvider[];
};

type ConfiguredProviderResponse = {
  count?: number;
  providers?: ConfiguredProviderSummary[];
};

type ProviderEditorState = {
  authType: string;
  apiKey: string;
  defaultModel: string;
  baseUrl: string;
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
  memories: {
    id: 'memories',
    title: 'Memories',
    lead: 'Author and manage agent memory records as first-class Markdown documents under the reserved memories path.',
    bullets: [
      'Memory records use the same editor, save path, import/export flow, and version history as documents.',
      'The view is scoped to memories/ inside the active managed-content brain.',
      'Forget remains explicit so stale memories can be removed safely.'
    ]
  },
  chat: {
    id: 'chat',
    title: 'Chat',
    lead: 'Converse with AI models through intelligent routing and streaming responses.',
    bullets: [
      'Multi-model orchestration routes to the best provider.',
      'Real-time streaming with activity indicators.',
      'Conversation history persists across sessions.'
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

const orderedViews: PortalView[] = ['signin', 'documents', 'memories', 'chat', 'account', 'usage', 'tools'];

function App() {
  const [config, setConfig] = useState<PortalConfig | null>(null);
  const [configError, setConfigError] = useState<string | null>(null);
  const [authSession, setAuthSession] = useState<StoredAuthSession | null>(() => loadStoredAuthSession());
  const [authEmailInput, setAuthEmailInput] = useState('');
  const [authPasswordInput, setAuthPasswordInput] = useState('');
  const [authActionMessage, setAuthActionMessage] = useState<string | null>(null);
  const [authError, setAuthError] = useState<string | null>(null);
  const [authPendingAction, setAuthPendingAction] = useState<AuthPendingAction>(null);
  const [firebaseAuth, setFirebaseAuth] = useState<FirebaseAuthInstance | null>(null);
  const [googleAuthStatus, setGoogleAuthStatus] = useState('Google sign-in is available when the Google provider is enabled in Firebase Authentication.');
  const [context, setContext] = useState<PortalContext | null>(null);
  const [billing, setBilling] = useState<PortalBilling | null>(null);
  const [brains, setBrains] = useState<BrainSummary[]>([]);
  const [memoryBrainPreference, setMemoryBrainPreference] = useState<MemoryBrainPreference | null>(null);
  const [tokens, setTokens] = useState<TokenSummary[]>([]);
  const [availableProviders, setAvailableProviders] = useState<AvailableProvider[]>([]);
  const [configuredProviders, setConfiguredProviders] = useState<ConfiguredProviderSummary[]>([]);
  const [activeView, setActiveView] = useState<PortalView>(resolveViewFromHash(window.location.hash, loadStoredAuthSession()));
  const [workspaceError, setWorkspaceError] = useState<string | null>(null);
  const [workspaceLoading, setWorkspaceLoading] = useState(false);
  const [refreshNonce, setRefreshNonce] = useState(0);
  const [activeBrainId, setActiveBrainId] = useState('');
  const [documentFilter, setDocumentFilter] = useState('');
  const [memoryFilter, setMemoryFilter] = useState('');
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
    if (!config) {
      return;
    }

    let cancelled = false;
    let retryHandle: number | null = null;
    let attempts = 0;

    const syncGoogleAuth = () => {
      if (cancelled) {
        return;
      }

      if (!config.hostedAuthConfigured || !config.firebaseProjectId || !config.firebaseApiKey) {
        setFirebaseAuth(null);
        setGoogleAuthStatus('Configure Firebase project and API key values before enabling browser auth.');
        return;
      }

      const auth = initializeFirebaseAuth(config);
      if (auth) {
        setFirebaseAuth(auth);
        setGoogleAuthStatus('Google sign-in uses the Firebase Authentication provider settings for this project.');
        return;
      }

      setFirebaseAuth(null);
      setGoogleAuthStatus('Waiting for the Firebase browser auth SDK to load.');
      if (attempts < 20) {
        attempts += 1;
        retryHandle = window.setTimeout(syncGoogleAuth, 250);
      }
    };

    syncGoogleAuth();

    return () => {
      cancelled = true;
      if (retryHandle !== null) {
        window.clearTimeout(retryHandle);
      }
    };
  }, [config]);

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
    if (authSession) {
      setAuthError(null);
      setAuthPendingAction(null);
    } else {
      setAuthActionMessage(null);
      setAuthError(null);
      setAuthPendingAction(null);
    }
  }, [authSession]);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const providerConnected = params.get('providerConnected');
    const providerError = params.get('providerError');
    const providerId = params.get('providerId');

    if (!providerConnected && !providerError) {
      return;
    }

    const providerLabel = providerId || providerConnected || 'provider';
    setAccountActionMessage(providerConnected
      ? `${providerLabel} connected successfully.`
      : providerError || `Failed to connect ${providerLabel}.`);

    if (authSession) {
      navigateToView('account', authSession, setActiveView);
      setRefreshNonce((value) => value + 1);
    }

    const nextUrl = new URL(window.location.href);
    nextUrl.searchParams.delete('providerConnected');
    nextUrl.searchParams.delete('providerError');
    nextUrl.searchParams.delete('providerId');
    const search = nextUrl.searchParams.toString();
    const hash = nextUrl.hash || '#account';
    window.history.replaceState(null, '', `${nextUrl.pathname}${search ? `?${search}` : ''}${hash}`);
  }, [authSession]);

  useEffect(() => {
    if (!authSession) {
      setAuthError(null);
      setAuthPendingAction(null);
      setContext(null);
      setBilling(null);
      setBrains([]);
      setMemoryBrainPreference(null);
      setTokens([]);
      setAvailableProviders([]);
      setConfiguredProviders([]);
      setWorkspaceError(null);
      setActiveBrainId('');
      setDocumentFilter('');
      setMemoryFilter('');
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

        const [workspaceContext, workspaceBilling, workspaceBrains, workspaceMemoryBrain, workspaceTokens, providerCatalog, providerConfigs] = await Promise.all([
          portalFetch('/portal-api/tenant/me', session.idToken),
          portalFetch('/portal-api/tenant/billing/plan', session.idToken),
          portalFetch('/portal-api/tenant/brains', session.idToken),
          portalFetch('/portal-api/tenant/me/memory-brain', session.idToken),
          portalFetch('/portal-api/tenant/tokens', session.idToken),
          portalFetch('/portal-api/api/providers/config/available', session.idToken),
          portalFetch('/portal-api/api/providers/config/', session.idToken)
        ]);

        if (cancelled) {
          return;
        }

        const nextBrains = (((workspaceBrains as { brains?: BrainSummary[] }).brains) || [])
          .filter((brain) => String(brain.mode || '').toLowerCase() === 'managed-content'
            && String(brain.status || '').toLowerCase() !== 'retired');
        const nextTokens = (((workspaceTokens as { tokens?: TokenSummary[] }).tokens) || []);
        const nextAvailableProviders = (((providerCatalog as AvailableProviderResponse).providers) || []);
        const nextConfiguredProviders = (((providerConfigs as ConfiguredProviderResponse).providers) || []);

        setContext(workspaceContext as PortalContext);
        setBilling(workspaceBilling as PortalBilling);
        setBrains(nextBrains);
        setMemoryBrainPreference(workspaceMemoryBrain as MemoryBrainPreference);
        setTokens(nextTokens);
        setAvailableProviders(nextAvailableProviders);
        setConfiguredProviders(nextConfiguredProviders);
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
          setMemoryBrainPreference(null);
          setTokens([]);
          setAvailableProviders([]);
          setConfiguredProviders([]);
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

  const getValidSession = useCallback(async () => {
    if (!authSession) {
      throw new Error('Sign in before using the portal.');
    }

    const session = await ensureValidSession(authSession);
    if (session !== authSession) {
      setAuthSession(session);
    }

    return session;
  }, [authSession]);

  const {
    documentDraft,
    documentError,
    documentIsDirty,
    documentLoading,
    documentSaveMessage,
    documentSaveState,
    documentVersions,
    documents,
    documentsError,
    documentsLoading,
    isCreatingDocument,
    selectedDocument,
    selectedDocumentId,
    selectedVersion,
    selectedVersionId,
    versionError,
    versionLoading,
    versionsError,
    versionsLoading,
    handleCreateDocument,
    handleDeleteDocument,
    handleDraftChange,
    handleExportDocument,
    handleImportDocument,
    handleRefreshDocuments,
    handleRefreshVersions,
    handleRestoreVersion,
    handleRevertDocument,
    handleSaveDocument,
    handleSelectDocument: selectDocumentById,
    handleSelectVersion,
    showDocumentStatus,
  } = useManagedDocumentWorkspace({
    activeBrainId,
    enabled: Boolean(authSession && activeBrainId),
    hasSession: Boolean(authSession),
    singularLabel: 'document',
    deleteActionLabel: 'Delete',
    deletePastTense: 'deleted',
    getValidSession,
    portalFetch,
    downloadTextFile,
    formatDateTime,
    listQuery: {
      excludePathPrefix: 'memories/',
    },
  });

  const {
    documentDraft: memoryDraft,
    documentError: memoryError,
    documentIsDirty: memoryIsDirty,
    documentLoading: memoryLoading,
    documentSaveMessage: memorySaveMessage,
    documentSaveState: memorySaveState,
    documentVersions: memoryVersions,
    documents: memories,
    documentsError: memoriesError,
    documentsLoading: memoriesLoading,
    isCreatingDocument: isCreatingMemory,
    selectedDocument: selectedMemory,
    selectedDocumentId: selectedMemoryId,
    selectedVersion: selectedMemoryVersion,
    selectedVersionId: selectedMemoryVersionId,
    versionError: memoryVersionError,
    versionLoading: memoryVersionLoading,
    versionsError: memoryVersionsError,
    versionsLoading: memoryVersionsLoading,
    handleCreateDocument: handleCreateMemory,
    handleDeleteDocument: handleDeleteMemory,
    handleDraftChange: handleMemoryDraftChange,
    handleExportDocument: handleExportMemory,
    handleImportDocument: handleImportMemory,
    handleRefreshDocuments: handleRefreshMemories,
    handleRefreshVersions: handleRefreshMemoryVersions,
    handleRestoreVersion: handleRestoreMemoryVersion,
    handleRevertDocument: handleRevertMemory,
    handleSaveDocument: handleSaveMemory,
    handleSelectDocument: selectMemoryById,
    handleSelectVersion: handleSelectMemoryVersion,
    showDocumentStatus: showMemoryStatus,
  } = useManagedDocumentWorkspace({
    activeBrainId: memoryBrainPreference?.effectiveMemoryBrainId || '',
    enabled: activeView === 'memories' && Boolean(authSession && memoryBrainPreference?.effectiveMemoryBrainId),
    hasSession: Boolean(authSession),
    singularLabel: 'memory',
    deleteActionLabel: 'Forget',
    deletePastTense: 'forgotten',
    getValidSession,
    portalFetch,
    downloadTextFile,
    formatDateTime,
    listQuery: {
      pathPrefix: 'memories/',
    },
    normalizeDraftForEdit: normalizeMemoryDraftForEdit,
    normalizeDraftForSave: normalizeMemoryDraftForSave,
  });
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

function handleChangeBrain(nextBrainId: string) {
  if (nextBrainId === activeBrainId) {
    return;
  }

  if (!confirmDiscardDocumentChanges(documentIsDirty || memoryIsDirty, 'Switch brains and discard unsaved changes?')) {
    return;
  }

  setActiveBrainId(nextBrainId);
}

function handleSelectDocument(nextDocumentId: string) {
  void selectDocumentById(nextDocumentId);
}

function handleSelectMemory(nextMemoryId: string) {
  void selectMemoryById(nextMemoryId);
}

async function handleOpenDocumentLink(rawPath: string) {
  const canonicalPath = normalizeDocumentLinkPath(rawPath);
  const isMemoryPath = canonicalPath.startsWith('memories/');
  const showStatus = isMemoryPath ? showMemoryStatus : showDocumentStatus;
  const targetBrainId = resolveDocumentLinkBrainId(
    canonicalPath,
    activeBrainId,
    memoryBrainPreference?.effectiveMemoryBrainId,
  );

  if (!canonicalPath) {
    showStatus('warn', 'Enter a valid managed document path like daily/notes.md.');
    return;
  }

  if (!targetBrainId) {
    showStatus(
      'warn',
      isMemoryPath
        ? 'Select or configure a memory brain before opening memory links.'
        : 'Select a managed-content brain before opening document links.',
    );
    return;
  }

  const targetIsMemory = canonicalPath.startsWith('memories/');
  const targetDocuments = targetIsMemory ? memories : documents;
  const targetIsDirty = targetIsMemory ? memoryIsDirty : documentIsDirty;
  const targetView = targetIsMemory ? 'memories' : 'documents';
  const selectTargetDocument = targetIsMemory ? selectMemoryById : selectDocumentById;
  const refreshTargetDocuments = targetIsMemory ? handleRefreshMemories : handleRefreshDocuments;

  if (!confirmDiscardDocumentChanges(targetIsDirty, `Open '${canonicalPath}' and discard unsaved changes?`)) {
    return;
  }

  const existingDocument = targetDocuments.find((document) => {
    const candidatePath = normalizeDocumentLinkPath(document.canonicalPath || document.slug || '');
    return candidatePath === canonicalPath;
  });

  if (existingDocument?.managedDocumentId) {
    const selected = selectTargetDocument(existingDocument.managedDocumentId, {
      loadingMessage: `Loading '${existingDocument.title || existingDocument.canonicalPath || canonicalPath}'...`,
      skipConfirm: true,
    });

    if (selected && authSession) {
      navigateToView(targetView, authSession, setActiveView);
    }

    return;
  }

  try {
    const session = await getValidSession();
    const resolvedDocument = await portalFetch(
      `/portal-api/tenant/brains/${encodeURIComponent(targetBrainId)}/documents/by-path?canonicalPath=${encodeURIComponent(canonicalPath)}`,
      session.idToken,
    ) as DocumentDetail;

    const selected = selectTargetDocument(resolvedDocument.managedDocumentId, {
      loadingMessage: `Loading '${resolvedDocument.title || resolvedDocument.canonicalPath || canonicalPath}'...`,
      skipConfirm: true,
    });

    if (!selected) {
      return;
    }

    refreshTargetDocuments();
    if (authSession) {
      navigateToView(targetView, authSession, setActiveView);
    }
  } catch (error) {
    showStatus('error', error instanceof Error ? error.message : `Failed to open '${canonicalPath}'.`);
  }
}
  async function handleAuthenticate(endpoint: '/portal-auth/login' | '/portal-auth/register', action: Exclude<AuthPendingAction, null>) {
    const email = authEmailInput.trim();
    const password = authPasswordInput;

    if (!email || !password) {
      setAuthError('Email and password are required.');
      return;
    }

    setAuthPendingAction(action);
    setAuthError(null);
    setAuthActionMessage(null);

    try {
      const session = buildStoredAuthSession(await postJson(endpoint, {
        email,
        password,
      }) as PortalAuthResponse);

      saveStoredAuthSession(session);
      setAuthSession(session);
      setAuthPasswordInput('');
      setAuthActionMessage(action === 'signin' ? 'Signed in.' : 'Account created and signed in.');
      navigateToView('documents', session, setActiveView);
    } catch (error) {
      setAuthError(error instanceof Error ? error.message : 'Authentication failed.');
    } finally {
      setAuthPendingAction(null);
    }
  }

  async function handleSignIn() {
    await handleAuthenticate('/portal-auth/login', 'signin');
  }

  async function handleCreateAccount() {
    await handleAuthenticate('/portal-auth/register', 'register');
  }

  async function handleGoogleSignIn() {
    if (!config?.hostedAuthConfigured) {
      setAuthError('Configure Firebase project and API key values before using Google sign-in.');
      return;
    }

    const auth = firebaseAuth ?? initializeFirebaseAuth(config);
    if (!auth) {
      setAuthError('Firebase browser auth is not ready yet. Try again in a moment.');
      setGoogleAuthStatus('Waiting for the Firebase browser auth SDK to load.');
      return;
    }

    setFirebaseAuth(auth);
    setAuthPendingAction('google');
    setAuthError(null);
    setAuthActionMessage('Starting Google sign-in...');

    try {
      const provider = new window.firebase!.auth.GoogleAuthProvider();
      provider.setCustomParameters({ prompt: 'select_account' });

      const userCredential = await auth.signInWithPopup(provider);
      const user = userCredential?.user;
      if (!user) {
        throw new Error('Google authentication did not return a Firebase user.');
      }

      const session = await buildStoredAuthSessionFromFirebaseUser(user);
      saveStoredAuthSession(session);
      setAuthSession(session);
      setAuthEmailInput(session.email || authEmailInput);
      setAuthPasswordInput('');
      setAuthActionMessage('Signed in with Google.');
      navigateToView('documents', session, setActiveView);
    } catch (error) {
      setAuthError(normalizeFirebaseClientError(error));
      setAuthActionMessage(null);
    } finally {
      setAuthPendingAction(null);
    }
  }

  async function handleSignOut() {
    if (!confirmDiscardDocumentChanges(documentIsDirty || memoryIsDirty, 'Sign out and discard unsaved changes?')) {
      return;
    }

    clearStoredAuthSession();

    try {
      if (firebaseAuth) {
        await firebaseAuth.signOut();
      }
    } catch {
      // Local browser session is already cleared; ignore Firebase sign-out errors.
    }

    setAuthSession(null);
    setAuthPasswordInput('');
    setCreatedToken(null);
    setAccountActionMessage(null);
    setAuthActionMessage('Signed out of the browser session.');
    navigateToView('signin', null, setActiveView);
  }

  async function handleRefreshSession() {
    try {
      const session = await getValidSession();
      setRefreshNonce((value) => value + 1);
      setAccountActionMessage('Session refreshed.');
      return session.idToken;
    } catch (error) {
      setAccountActionMessage(error instanceof Error ? error.message : 'Failed to refresh session.');
      return null;
    }
  }

  async function handleSaveProviderConfig(providerId: string, editor: ProviderEditorState) {
    try {
      const session = await getValidSession();
      const request = buildProviderConfigRequest(providerId, editor, configuredProviders);
      await portalFetch(`/portal-api/api/providers/config/${encodeURIComponent(providerId)}`, session.idToken, {
        method: 'PUT',
        body: JSON.stringify(request),
      });
      setAccountActionMessage(`${providerId} settings saved.`);
      setRefreshNonce((value) => value + 1);
    } catch (error) {
      setAccountActionMessage(error instanceof Error ? error.message : `Failed to save ${providerId} settings.`);
    }
  }

  async function handleSaveMemoryBrain(memoryBrainId: string) {
    try {
      const session = await getValidSession();
      const response = await portalFetch('/portal-api/tenant/me/memory-brain', session.idToken, {
        method: 'PUT',
        body: JSON.stringify({
          memoryBrainId: memoryBrainId || null,
        }),
      }) as MemoryBrainPreference;

      setMemoryBrainPreference(response);
      setAccountActionMessage(memoryBrainId
        ? 'Memory brain preference saved.'
        : 'Memory brain preference cleared.');
      setRefreshNonce((value) => value + 1);
    } catch (error) {
      setAccountActionMessage(error instanceof Error ? error.message : 'Failed to save memory brain preference.');
    }
  }

  async function handleToggleProvider(providerId: string) {
    try {
      const session = await getValidSession();
      await portalFetch(`/portal-api/api/providers/config/${encodeURIComponent(providerId)}/toggle`, session.idToken, {
        method: 'POST',
      });
      setAccountActionMessage(`${providerId} availability updated.`);
      setRefreshNonce((value) => value + 1);
    } catch (error) {
      setAccountActionMessage(error instanceof Error ? error.message : `Failed to update ${providerId}.`);
    }
  }

  async function handleDeleteProvider(providerId: string) {
    if (!window.confirm(`Delete the stored ${providerId} configuration?`)) {
      return;
    }

    try {
      const session = await getValidSession();
      await portalFetch(`/portal-api/api/providers/config/${encodeURIComponent(providerId)}`, session.idToken, {
        method: 'DELETE',
      });
      setAccountActionMessage(`${providerId} configuration deleted.`);
      setRefreshNonce((value) => value + 1);
    } catch (error) {
      setAccountActionMessage(error instanceof Error ? error.message : `Failed to delete ${providerId}.`);
    }
  }

  async function handleStartProviderOAuth(providerId: string) {
    try {
      const session = await getValidSession();
      const returnUrl = buildPortalOAuthReturnUrl();
      const response = await portalFetch(
        `/portal-api/api/providers/config/${encodeURIComponent(providerId)}/oauth/authorize?returnUrl=${encodeURIComponent(returnUrl)}`,
        session.idToken) as { authorizationUrl?: string };

      if (!response.authorizationUrl) {
        throw new Error(`No OAuth authorization URL was returned for ${providerId}.`);
      }

      window.location.assign(response.authorizationUrl);
    } catch (error) {
      setAccountActionMessage(error instanceof Error ? error.message : `Failed to start OAuth for ${providerId}.`);
    }
  }

  async function handleDisconnectProviderOAuth(providerId: string) {
    if (!window.confirm(`Disconnect ${providerId} and remove the stored OAuth token?`)) {
      return;
    }

    try {
      const session = await getValidSession();
      await portalFetch(`/portal-api/api/providers/config/${encodeURIComponent(providerId)}/oauth/disconnect`, session.idToken, {
        method: 'POST',
      });
      setAccountActionMessage(`${providerId} disconnected.`);
      setRefreshNonce((value) => value + 1);
    } catch (error) {
      setAccountActionMessage(error instanceof Error ? error.message : `Failed to disconnect ${providerId}.`);
    }
  }

  function handleOpenProviderSettings() {
    if (!authSession) {
      return;
    }

    navigateToView('account', authSession, setActiveView);
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
  const hasConfiguredProviders = useMemo(
    () => configuredProviders.some((provider) => isProviderReady(provider)),
    [configuredProviders]
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
const filteredMemories = useMemo(
  () => applyDocumentFilter(memories, memoryFilter),
  [memories, memoryFilter]
);
const documentGroups = useMemo(
  () => buildDocumentDirectoryGroups(filteredDocuments),
  [filteredDocuments]
);
const memoryDocumentGroups = useMemo(
  () => buildDocumentDirectoryGroups(filteredMemories),
  [filteredMemories]
);
const availableManagedDocumentLinks = useMemo(
  () => [...documents, ...memories],
  [documents, memories]
);
const activeBrain = brains.find((brain) => brain.brainId === activeBrainId) ?? null;
const effectiveMemoryBrainId = memoryBrainPreference?.effectiveMemoryBrainId || '';
const memoryActiveBrain = brains.find((brain) => brain.brainId === effectiveMemoryBrainId) ?? null;
const memoryViewBrains = memoryActiveBrain ? [memoryActiveBrain] : [];

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
            <button type="button" className="button button-danger" onClick={handleSignOut} disabled={!authSession}>
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
          <SignedOutState
            activeDefinition={activeDefinition}
            authActionMessage={authActionMessage}
            authEmailInput={authEmailInput}
            authError={authError}
            authPasswordInput={authPasswordInput}
            authPendingAction={authPendingAction}
            config={config}
            googleAuthReady={Boolean(firebaseAuth)}
            googleAuthStatus={googleAuthStatus}
            onAuthEmailInputChange={setAuthEmailInput}
            onAuthPasswordInputChange={setAuthPasswordInput}
            onCreateAccount={handleCreateAccount}
            onGoogleSignIn={handleGoogleSignIn}
            onSignIn={handleSignIn}
          />
        ) : activeView === 'documents' ? (
          <DocumentsView
            activeBrain={activeBrain}
            activeBrainId={activeBrainId}
            availableDocumentLinks={availableManagedDocumentLinks}
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
            onOpenDocumentLink={handleOpenDocumentLink}
            onRefreshDocuments={handleRefreshDocuments}
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
        ) : activeView === 'memories' ? (
          <MemoriesView
            activeBrain={memoryActiveBrain}
            activeBrainId={effectiveMemoryBrainId}
            availableDocumentLinks={availableManagedDocumentLinks}
            brains={memoryViewBrains}
            documentDraft={memoryDraft}
            documentError={memoryError}
            documentFilter={memoryFilter}
            documentGroups={memoryDocumentGroups}
            documentIsDirty={memoryIsDirty}
            documentLoading={memoryLoading}
            documentSaveMessage={memorySaveMessage}
            documentSaveState={memorySaveState}
            documentVersions={memoryVersions}
            documents={memories}
            documentsError={memoriesError}
            documentsLoading={memoriesLoading}
            filteredDocuments={filteredMemories}
            isCreatingDocument={isCreatingMemory}
            onChangeBrain={handleChangeBrain}
            onChangeDocumentFilter={setMemoryFilter}
            onCreateDocument={handleCreateMemory}
            onDeleteDocument={handleDeleteMemory}
            onDraftChange={handleMemoryDraftChange}
            onExportDocument={handleExportMemory}
            onImportDocument={handleImportMemory}
            onOpenDocumentLink={handleOpenDocumentLink}
            onRefreshDocuments={handleRefreshMemories}
            onRefreshVersions={handleRefreshMemoryVersions}
            onRestoreVersion={handleRestoreMemoryVersion}
            onRevertDocument={handleRevertMemory}
            onSaveDocument={handleSaveMemory}
            onSelectDocument={handleSelectMemory}
            onSelectVersion={handleSelectMemoryVersion}
            selectedDocument={selectedMemory}
            selectedDocumentId={selectedMemoryId}
            selectedVersion={selectedMemoryVersion}
            selectedVersionId={selectedMemoryVersionId}
            versionError={memoryVersionError}
            versionLoading={memoryVersionLoading}
            versionsError={memoryVersionsError}
            versionsLoading={memoryVersionsLoading}
          />
        ) : activeView === 'chat' ? (
          <ChatView
            authSession={authSession}
            activeBrainId={activeBrainId}
            hasConfiguredProviders={hasConfiguredProviders}
            onOpenProviderSettings={handleOpenProviderSettings}
            onRefreshSession={handleRefreshSession}
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
            availableProviders={availableProviders}
            billing={billing}
            brains={brains}
            configuredProviders={configuredProviders}
            context={context}
            createdToken={createdToken}
            memoryBrainPreference={memoryBrainPreference}
            onCopyCreatedToken={handleCopyCreatedToken}
            onCreateToken={handleCreateToken}
            onDeleteProvider={handleDeleteProvider}
            onDisconnectProviderOAuth={handleDisconnectProviderOAuth}
            onDismissCreatedToken={() => setCreatedToken(null)}
            onRefreshSession={handleRefreshSession}
            onRequestWriteScopeChange={setRequestWriteScope}
            onRevokeToken={handleRevokeToken}
            onSaveMemoryBrain={handleSaveMemoryBrain}
            onSaveProviderConfig={handleSaveProviderConfig}
            onSignOut={handleSignOut}
            onStartProviderOAuth={handleStartProviderOAuth}
            onToggleProvider={handleToggleProvider}
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
  authActionMessage: string | null;
  authEmailInput: string;
  authError: string | null;
  authPasswordInput: string;
  authPendingAction: AuthPendingAction;
  config: PortalConfig | null;
  googleAuthReady: boolean;
  googleAuthStatus: string;
  onAuthEmailInputChange: (value: string) => void;
  onAuthPasswordInputChange: (value: string) => void;
  onCreateAccount: () => Promise<void>;
  onGoogleSignIn: () => Promise<void>;
  onSignIn: () => Promise<void>;
};

function SignedOutState({
  activeDefinition,
  authActionMessage,
  authEmailInput,
  authError,
  authPasswordInput,
  authPendingAction,
  config,
  googleAuthReady,
  googleAuthStatus,
  onAuthEmailInputChange,
  onAuthPasswordInputChange,
  onCreateAccount,
  onGoogleSignIn,
  onSignIn
}: SignedOutStateProps) {
  const hostedAuthAvailable = config?.hostedAuthConfigured !== false;
  const authDisabled = !hostedAuthAvailable || authPendingAction !== null;
  const googleAuthDisabled = authPendingAction !== null || !hostedAuthAvailable || !googleAuthReady;

  return (
    <section className="portal-layout">
      <section className="auth-layout">
        <article className="panel portal-hero auth-intro">
          <p className="eyebrow">Sign In</p>
          <h2>Authenticate directly into the React portal.</h2>
          <p className="summary-detail auth-copy">
            This host now owns its email and password entry flow. It writes the same browser session contract, then loads workspace state without sending you back through the classic shell.
          </p>
          <ul className="feature-list">
            {activeDefinition.bullets.map((bullet) => (
              <li key={bullet}>{bullet}</li>
            ))}
          </ul>
          <div className="feature-stack">
            <article className="slice-card feature-card">
              <p className="panel-label">Session Contract</p>
              <h3>Same storage key, new entry surface</h3>
              <p>The React portal writes the shared auth session itself and still reuses the refresh endpoint before tenant API calls.</p>
            </article>
            <article className="slice-card feature-card">
              <p className="panel-label">Fallback</p>
              <h3>Classic shell remains available</h3>
              <p>The classic portal still lives at /legacy, but it is no longer required just to start a session.</p>
            </article>
          </div>
        </article>

        <article className="panel auth-panel">
          <p className="panel-label">Portal Access</p>
          <h3>Sign in with hosted auth</h3>
          <p className="summary-detail">
            Use the same hosted email/password backend the classic portal uses today.
          </p>
          <form className="session-form" onSubmit={(event) => {
            event.preventDefault();
            void onSignIn();
          }}>
            <label className="field" htmlFor="auth-email">
              <span>Email</span>
              <input
                id="auth-email"
                type="email"
                value={authEmailInput}
                onChange={(event) => onAuthEmailInputChange(event.target.value)}
                autoComplete="email"
                placeholder="you@company.com"
                disabled={authDisabled}
              />
            </label>
            <label className="field" htmlFor="auth-password">
              <span>Password</span>
              <input
                id="auth-password"
                type="password"
                value={authPasswordInput}
                onChange={(event) => onAuthPasswordInputChange(event.target.value)}
                autoComplete="current-password"
                placeholder="Enter your password"
                disabled={authDisabled}
              />
            </label>
            {authError ? <p className="banner error-banner" role="alert">{authError}</p> : null}
            {authActionMessage ? <p className="banner info-banner session-status">{authActionMessage}</p> : null}
            {!hostedAuthAvailable ? (
              <p className="banner error-banner" role="alert">
                Hosted auth is not configured for this portal host yet.
              </p>
            ) : null}
            <div className="action-row">
              <button type="submit" className="button button-primary" disabled={authDisabled}>
                {authPendingAction === 'signin' ? 'Signing In...' : 'Sign In'}
              </button>
              <button type="button" className="button" onClick={() => void onCreateAccount()} disabled={authDisabled}>
                {authPendingAction === 'register' ? 'Creating Account...' : 'Create Account'}
              </button>
            </div>
          </form>
          <div className="google-auth-panel">
            <p className="summary-detail">{googleAuthStatus}</p>
            <button type="button" className="button google-signin-button" onClick={() => void onGoogleSignIn()} disabled={googleAuthDisabled}>
              {authPendingAction === 'google' ? 'Connecting to Google...' : 'Continue with Google'}
            </button>
          </div>
          <p className="auth-fallback-note">
            Need the previous shell for comparison? <a href="/legacy">Open the classic portal</a>.
          </p>
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
type AccountViewProps = {
  accountActionMessage: string | null;
  authSession: StoredAuthSession;
  availableProviders: AvailableProvider[];
  billing: PortalBilling | null;
  brains: BrainSummary[];
  configuredProviders: ConfiguredProviderSummary[];
  context: PortalContext | null;
  createdToken: CreatedTokenState | null;
  memoryBrainPreference: MemoryBrainPreference | null;
  onCopyCreatedToken: () => void;
  onCreateToken: () => void;
  onDeleteProvider: (providerId: string) => Promise<void>;
  onDisconnectProviderOAuth: (providerId: string) => Promise<void>;
  onDismissCreatedToken: () => void;
  onRefreshSession: () => Promise<string | null>;
  onRequestWriteScopeChange: (value: boolean) => void;
  onRevokeToken: (apiTokenId: string) => void;
  onSaveMemoryBrain: (memoryBrainId: string) => Promise<void>;
  onSaveProviderConfig: (providerId: string, editor: ProviderEditorState) => Promise<void>;
  onSignOut: () => void;
  onStartProviderOAuth: (providerId: string) => Promise<void>;
  onToggleProvider: (providerId: string) => Promise<void>;
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
  availableProviders,
  billing,
  brains,
  configuredProviders,
  context,
  createdToken,
  memoryBrainPreference,
  onCopyCreatedToken,
  onCreateToken,
  onDeleteProvider,
  onDisconnectProviderOAuth,
  onDismissCreatedToken,
  onRefreshSession,
  onRequestWriteScopeChange,
  onRevokeToken,
  onSaveMemoryBrain,
  onSaveProviderConfig,
  onSignOut,
  onStartProviderOAuth,
  onToggleProvider,
  onTokenExpiresAtInputChange,
  onTokenNameInputChange,
  requestWriteScope,
  tokenExpiresAtInput,
  tokenNameInput,
  tokens
}: AccountViewProps) {
  const activeTokenCount = tokens.filter((token) => !token.revokedAt).length;
  const configuredProviderMap = useMemo(
    () => new Map(configuredProviders.map((provider) => [provider.providerId, provider])),
    [configuredProviders]
  );
  const [providerEditors, setProviderEditors] = useState<Record<string, ProviderEditorState>>(() =>
    buildProviderEditors(availableProviders, configuredProviders)
  );
  const [selectedMemoryBrainId, setSelectedMemoryBrainId] = useState('');
  const [pendingProviderId, setPendingProviderId] = useState<string | null>(null);
  const [memoryBrainPending, setMemoryBrainPending] = useState(false);

  useEffect(() => {
    setProviderEditors(buildProviderEditors(availableProviders, configuredProviders));
  }, [availableProviders, configuredProviders]);

  useEffect(() => {
    setSelectedMemoryBrainId(memoryBrainPreference?.configuredMemoryBrainId || '');
  }, [memoryBrainPreference]);

  function updateProviderEditor(providerId: string, patch: Partial<ProviderEditorState>) {
    setProviderEditors((current) => ({
      ...current,
      [providerId]: {
        ...(current[providerId] || buildProviderEditorState(
          availableProviders.find((provider) => provider.providerId === providerId) || null,
          configuredProviderMap.get(providerId) || null
        )),
        ...patch,
      },
    }));
  }

  async function runProviderAction(providerId: string, action: () => Promise<void>) {
    setPendingProviderId(providerId);
    try {
      await action();
    } finally {
      setPendingProviderId((current) => (current === providerId ? null : current));
    }
  }

  return (
    <section className="portal-layout">
      <article className="panel portal-hero">
        <p className="eyebrow">Account</p>
        <h2>Session controls, provider access, and MCP tokens now live in one place.</h2>
        <p className="summary-detail">
          Chat uses your own provider settings now, so this page is where model access, browser session posture, and MCP token issuance meet.
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
            <button type="button" className="button" onClick={() => void onRefreshSession()}>Refresh Session</button>
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
            <button type="button" className="button button-primary" onClick={() => void onCreateToken()}>Create Token</button>
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
                <button type="button" className="button button-primary" onClick={() => void onCopyCreatedToken()}>Copy Token</button>
              </div>
            </section>
          ) : null}
        </section>
      </section>

      <section className="panel">
        <div className="panel-header">
          <div>
            <h3>Memory Brain</h3>
            <p className="summary-detail">Select which managed-content brain stores agent memories under the reserved <code>memories/</code> path.</p>
          </div>
        </div>

        <label className="field">
          <span>Preferred Memory Brain</span>
          <select value={selectedMemoryBrainId} onChange={(event) => setSelectedMemoryBrainId(event.target.value)} disabled={memoryBrainPending || brains.length === 0}>
            <option value="">Auto-select the only active managed-content brain</option>
            {brains.map((brain) => (
              <option key={brain.brainId} value={brain.brainId}>{brain.name} ({brain.brainId})</option>
            ))}
          </select>
        </label>

        <dl className="facts-list compact-facts">
          <div className="fact-row"><dt>Configured</dt><dd>{memoryBrainPreference?.configuredMemoryBrainId || 'Auto'}</dd></div>
          <div className="fact-row"><dt>Effective</dt><dd>{memoryBrainPreference?.effectiveMemoryBrainId || 'Not resolved'}</dd></div>
          <div className="fact-row"><dt>Status</dt><dd>{memoryBrainPreference?.needsConfiguration ? 'Needs configuration' : 'Ready'}</dd></div>
        </dl>

        <p className="summary-detail">
          {memoryBrainPreference?.error
            ? memoryBrainPreference.error
            : memoryBrainPreference?.needsConfiguration
              ? 'Multiple active managed-content brains exist. Choose one explicitly for memory tools.'
              : 'Memory tools will use the effective brain shown above.'}
        </p>

        <div className="action-row">
          <button
            type="button"
            className="button button-primary"
            onClick={async () => {
              setMemoryBrainPending(true);
              try {
                await onSaveMemoryBrain(selectedMemoryBrainId);
              } finally {
                setMemoryBrainPending(false);
              }
            }}
            disabled={memoryBrainPending}
          >
            Save Memory Brain
          </button>
        </div>
      </section>

      <section className="panel">
        <div className="panel-header">
          <div>
            <h3>Provider Settings</h3>
            <p className="summary-detail">Connect Anthropic or OpenAI with OAuth or API keys, or point Ollama at a remote endpoint.</p>
          </div>
        </div>

        {availableProviders.length === 0 ? (
          <div className="empty-state">Provider catalog is not available yet.</div>
        ) : (
          <div className="provider-card-list">
            {availableProviders.map((provider) => {
              const configured = configuredProviderMap.get(provider.providerId) || null;
              const editor = providerEditors[provider.providerId] || buildProviderEditorState(provider, configured);
              const pending = pendingProviderId === provider.providerId;
              const supportsApiKey = provider.authTypes.includes('api_key');
              const supportsOAuth = provider.authTypes.includes('oauth') && isProviderOAuthConfigured(provider);
              const isOllama = provider.providerId === 'ollama';
              const isOAuthMode = editor.authType === 'oauth';
              const statusClass = configured
                ? (configured.isEnabled ? 'status-chip-active' : 'status-chip-expired')
                : 'status-chip-revoked';
              const statusLabel = configured
                ? (configured.isEnabled ? 'Enabled' : 'Disabled')
                : 'Not Configured';
              const hasOAuthConnection = configured?.authType === 'oauth' && configured.hasCredentials;
              const description = configured
                ? (hasOAuthConnection
                  ? 'OAuth token stored for this provider.'
                  : configured.hasCredentials
                    ? 'Stored credentials are available.'
                    : isOllama && configured.settings?.baseUrl
                      ? 'Remote endpoint is configured.'
                      : 'Configuration exists but still needs credentials or endpoint details.')
                : 'No saved configuration yet.';

              return (
                <article key={provider.providerId} className="provider-card">
                  <div className="token-record-header">
                    <div>
                      <h3>{provider.name}</h3>
                      <p className="summary-detail">{description}</p>
                    </div>
                    <span className={`status-chip ${statusClass}`}>{statusLabel}</span>
                  </div>

                  <div className="provider-card-meta">
                    <span><code>{provider.providerId}</code></span>
                    <span>Default {provider.defaultModel}</span>
                    {configured?.updatedAt ? <span>Updated {formatDateTime(configured.updatedAt || undefined)}</span> : null}
                    {configured?.tokenExpiresAt ? <span>Token Expires {formatDateTime(configured.tokenExpiresAt || undefined)}</span> : null}
                    {provider.configUrl ? (
                      <a href={provider.configUrl} target="_blank" rel="noreferrer">Get API Key</a>
                    ) : null}
                  </div>

                  {provider.authTypes.length > 1 ? (
                    <label className="field">
                      <span>Authentication</span>
                      <select
                        value={editor.authType}
                        onChange={(event) => updateProviderEditor(provider.providerId, { authType: event.target.value })}
                        disabled={pending}
                      >
                        {provider.authTypes.map((authType) => (
                          <option key={authType} value={authType}>{formatProviderAuthType(authType)}</option>
                        ))}
                      </select>
                    </label>
                  ) : (
                    <p className="summary-detail provider-auth-note">Authentication: {formatProviderAuthType(editor.authType)}</p>
                  )}

                  <div className="provider-form-grid">
                    <label className="field">
                      <span>Default Model</span>
                      <input
                        type="text"
                        value={editor.defaultModel}
                        onChange={(event) => updateProviderEditor(provider.providerId, { defaultModel: event.target.value })}
                        placeholder={provider.defaultModel}
                        disabled={pending}
                      />
                    </label>

                    {isOllama ? (
                      <label className="field">
                        <span>Base URL</span>
                        <input
                          type="url"
                          value={editor.baseUrl}
                          onChange={(event) => updateProviderEditor(provider.providerId, { baseUrl: event.target.value })}
                          placeholder="http://localhost:11434"
                          disabled={pending}
                        />
                      </label>
                    ) : null}
                  </div>

                  {supportsApiKey && !isOAuthMode ? (
                    <label className="field">
                      <span>API Key</span>
                      <input
                        type="password"
                        value={editor.apiKey}
                        onChange={(event) => updateProviderEditor(provider.providerId, { apiKey: event.target.value })}
                        placeholder={configured?.hasCredentials ? 'Leave blank to keep the stored key' : 'Paste API key'}
                        disabled={pending}
                      />
                    </label>
                  ) : null}

                  {supportsOAuth && isOAuthMode ? (
                    <p className="summary-detail provider-auth-note">
                      OAuth uses a browser redirect. Save any default model changes, then connect the provider for this account.
                    </p>
                  ) : null}

                  <div className="action-row">
                    <button
                      type="button"
                      className="button button-primary"
                      onClick={() => void runProviderAction(provider.providerId, async () => {
                        await onSaveProviderConfig(provider.providerId, editor);
                        updateProviderEditor(provider.providerId, { apiKey: '' });
                      })}
                      disabled={pending}
                    >
                      Save Settings
                    </button>
                    {supportsOAuth && isOAuthMode ? (
                      hasOAuthConnection ? (
                        <button
                          type="button"
                          className="button"
                          onClick={() => void runProviderAction(provider.providerId, () => onDisconnectProviderOAuth(provider.providerId))}
                          disabled={pending}
                        >
                          Disconnect OAuth
                        </button>
                      ) : (
                        <button
                          type="button"
                          className="button"
                          onClick={() => void runProviderAction(provider.providerId, async () => {
                            await onSaveProviderConfig(provider.providerId, editor);
                            await onStartProviderOAuth(provider.providerId);
                          })}
                          disabled={pending}
                        >
                          Connect OAuth
                        </button>
                      )
                    ) : null}
                    {configured ? (
                      <button
                        type="button"
                        className="button"
                        onClick={() => void runProviderAction(provider.providerId, () => onToggleProvider(provider.providerId))}
                        disabled={pending}
                      >
                        {configured.isEnabled ? 'Disable' : 'Enable'}
                      </button>
                    ) : null}
                    {configured ? (
                      <button
                        type="button"
                        className="button button-danger"
                        onClick={() => void runProviderAction(provider.providerId, () => onDeleteProvider(provider.providerId))}
                        disabled={pending}
                      >
                        Delete
                      </button>
                    ) : null}
                  </div>
                </article>
              );
            })}
          </div>
        )}
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

function saveStoredAuthSession(session: StoredAuthSession) {
  window.localStorage.setItem(storageKey, JSON.stringify(session));
}

function buildStoredAuthSession(session: PortalAuthResponse): StoredAuthSession {
  return {
    idToken: session.idToken,
    refreshToken: session.refreshToken,
    email: session.email,
    displayName: session.displayName || session.email.split('@', 1)[0] || '',
    expiresAt: session.expiresAt || buildExpiryTimestamp(session.expiresIn)
  };
}

function initializeFirebaseAuth(config: PortalConfig): FirebaseAuthInstance | null {
  if (!config.firebaseProjectId || !config.firebaseApiKey) {
    return null;
  }

  if (!window.firebase?.initializeApp || !window.firebase?.auth) {
    return null;
  }

  if (!window.firebase.apps.length) {
    window.firebase.initializeApp({
      apiKey: config.firebaseApiKey,
      authDomain: config.firebaseAuthDomain,
      projectId: config.firebaseProjectId,
    });
  }

  return window.firebase.auth();
}

function normalizeFirebaseClientError(error: unknown) {
  const candidate = error as { code?: string; message?: string };
  const code = candidate?.code || '';

  switch (code) {
    case 'auth/popup-closed-by-user':
      return 'Google sign-in was cancelled before it completed.';
    case 'auth/cancelled-popup-request':
      return 'Another Google sign-in attempt is already in progress.';
    case 'auth/operation-not-allowed':
      return 'Google sign-in is not enabled for this Firebase project.';
    case 'auth/unauthorized-domain':
      return 'This portal origin is not authorized in Firebase Authentication.';
    default:
      return candidate?.message || 'Google sign-in failed.';
  }
}

async function buildStoredAuthSessionFromFirebaseUser(user: FirebaseUser): Promise<StoredAuthSession> {
  const [idToken, idTokenResult] = await Promise.all([
    user.getIdToken(),
    user.getIdTokenResult(),
  ]);

  return buildStoredAuthSession({
    idToken,
    refreshToken: user.refreshToken || '',
    email: user.email || '',
    displayName: user.displayName || '',
    expiresAt: idTokenResult?.expirationTime || '',
    expiresIn: '3600'
  });
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

  saveStoredAuthSession(updatedSession);
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

function buildProviderEditors(
  availableProviders: AvailableProvider[],
  configuredProviders: ConfiguredProviderSummary[]
) {
  return availableProviders.reduce<Record<string, ProviderEditorState>>((accumulator, provider) => {
    const configured = configuredProviders.find((candidate) => candidate.providerId === provider.providerId) || null;
    accumulator[provider.providerId] = buildProviderEditorState(provider, configured);
    return accumulator;
  }, {});
}

function buildProviderEditorState(
  provider: AvailableProvider | null,
  configured: ConfiguredProviderSummary | null
): ProviderEditorState {
  const defaultAuthType = configured?.authType
    || (provider?.authTypes.includes('api_key') ? 'api_key' : provider?.authTypes[0] || 'api_key');

  return {
    authType: defaultAuthType,
    apiKey: '',
    defaultModel: configured?.settings?.defaultModel || provider?.defaultModel || '',
    baseUrl: configured?.settings?.baseUrl || '',
  };
}

function buildProviderConfigRequest(
  providerId: string,
  editor: ProviderEditorState,
  configuredProviders: ConfiguredProviderSummary[]
) {
  const configured = configuredProviders.find((candidate) => candidate.providerId === providerId) || null;
  const settings: Record<string, unknown> = {};
  const defaultModel = editor.defaultModel.trim();
  const baseUrl = editor.baseUrl.trim();

  if (defaultModel) {
    settings.defaultModel = defaultModel;
  }

  if (baseUrl) {
    settings.baseUrl = baseUrl;
  }

  const request: Record<string, unknown> = {
    authType: editor.authType,
    isEnabled: configured?.isEnabled ?? true,
  };

  if (Object.keys(settings).length > 0) {
    request.settings = settings;
  }

  if (editor.authType === 'api_key' && editor.apiKey.trim()) {
    request.apiKey = editor.apiKey.trim();
  }

  return request;
}

function isProviderReady(provider: ConfiguredProviderSummary) {
  if (!provider.isEnabled) {
    return false;
  }

  const authType = String(provider.authType || '').toLowerCase();
  if (provider.providerId === 'ollama' || authType === 'none') {
    return Boolean(provider.settings?.baseUrl?.trim());
  }

  return provider.hasCredentials;
}

function isProviderOAuthConfigured(provider: AvailableProvider) {
  return Boolean(provider.oauthConfigured ?? provider.OAuthConfigured);
}

function formatProviderAuthType(authType: string) {
  switch (String(authType || '').toLowerCase()) {
    case 'api_key':
      return 'API Key';
    case 'oauth':
      return 'OAuth';
    case 'none':
      return 'None';
    default:
      return authType || 'Unknown';
  }
}

function buildPortalOAuthReturnUrl() {
  return new URL('/app#account', window.location.origin).toString();
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
function normalizeMemoryDraftForEdit(draft: DocumentDraft) {
  const normalized = normalizeDocumentDraft(draft);
  return normalized.slug
    ? { ...normalized, slug: ensureMemoryPathPrefix(normalized.slug) }
    : normalized;
}

function normalizeMemoryDraftForSave(draft: DocumentDraft) {
  const normalized = normalizeDocumentDraft(draft);
  const fallback = normalized.slug || normalized.title || 'memory';
  return {
    ...normalized,
    slug: ensureMemoryPathPrefix(fallback),
  };
}

function ensureMemoryPathPrefix(value: string) {
  const normalized = normalizeDocumentPath(value);
  if (!normalized) {
    return '';
  }

  return normalized.startsWith('memories/') ? normalized : `memories/${normalized}`;
}
function confirmDiscardDocumentChanges(isDirty: boolean, message: string) {
  return !isDirty || window.confirm(message);
}

function downloadTextFile(fileName: string, content: string, contentType: string) {
  const blob = new Blob([content], { type: contentType });
  const url = window.URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  window.URL.revokeObjectURL(url);
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

