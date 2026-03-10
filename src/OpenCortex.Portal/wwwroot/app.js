'use strict';

const storageKey = 'opencortex.portal.auth_session';
const sessionRefreshSkewMs = 5 * 60 * 1000;

const state = {
  config: null,
  authSession: null,
  context: null,
  billing: null,
  brains: [],
  activeBrainId: '',
  documents: [],
  filteredDocuments: [],
  selectedDocument: null,
  documentDraft: buildEmptyDocumentDraft(),
  isCreatingDocument: false,
  documentSaveState: 'idle',
  documentSaveMessage: 'Make a change to enable save.',
  documentVersions: [],
  selectedVersion: null,
  firebaseAuthInitialized: false,
  firebaseAuth: null,
  tokens: [],
};

const statusBanner = document.getElementById('statusBanner');
const authForm = document.getElementById('authForm');
const authEmailInput = document.getElementById('authEmailInput');
const authPasswordInput = document.getElementById('authPasswordInput');
const createAccountButton = document.getElementById('createAccountButton');
const refreshSessionButton = document.getElementById('refreshSessionButton');
const signOutButton = document.getElementById('signOutButton');
const googleAuthStatus = document.getElementById('googleAuthStatus');
const googleSignInButton = document.getElementById('googleSignInButton');
const sessionStatus = document.getElementById('sessionStatus');
const workspaceName = document.getElementById('workspaceName');
const workspaceDetail = document.getElementById('workspaceDetail');
const planName = document.getElementById('planName');
const planDetail = document.getElementById('planDetail');
const mcpAccess = document.getElementById('mcpAccess');
const mcpAccessDetail = document.getElementById('mcpAccessDetail');
const tokenCount = document.getElementById('tokenCount');
const tokenCountDetail = document.getElementById('tokenCountDetail');
const brainSelect = document.getElementById('brainSelect');
const documentFilterInput = document.getElementById('documentFilterInput');
const createDocumentButton = document.getElementById('createDocumentButton');
const refreshDocumentsButton = document.getElementById('refreshDocumentsButton');
const documentsEmptyState = document.getElementById('documentsEmptyState');
const documentsTableWrap = document.getElementById('documentsTableWrap');
const documentsTableBody = document.getElementById('documentsTableBody');
const documentDetailTitle = document.getElementById('documentDetailTitle');
const documentDetailMeta = document.getElementById('documentDetailMeta');
const documentSaveStatus = document.getElementById('documentSaveStatus');
const documentTitleInput = document.getElementById('documentTitleInput');
const documentSlugInput = document.getElementById('documentSlugInput');
const documentStatusSelect = document.getElementById('documentStatusSelect');
const documentFrontmatterInput = document.getElementById('documentFrontmatterInput');
const documentContentEditor = document.getElementById('documentContentEditor');
const documentPreviewSurface = document.getElementById('documentPreviewSurface');
const saveDocumentButton = document.getElementById('saveDocumentButton');
const revertDocumentButton = document.getElementById('revertDocumentButton');
const deleteDocumentButton = document.getElementById('deleteDocumentButton');
const refreshVersionsButton = document.getElementById('refreshVersionsButton');
const restoreVersionButton = document.getElementById('restoreVersionButton');
const versionsEmptyState = document.getElementById('versionsEmptyState');
const versionsTableWrap = document.getElementById('versionsTableWrap');
const versionsTableBody = document.getElementById('versionsTableBody');
const versionDetailMeta = document.getElementById('versionDetailMeta');
const versionPreviewSurface = document.getElementById('versionPreviewSurface');
const createTokenForm = document.getElementById('createTokenForm');
const tokenNameInput = document.getElementById('tokenNameInput');
const expiresAtInput = document.getElementById('expiresAtInput');
const scopeWriteInput = document.getElementById('scopeWriteInput');
const createdTokenPanel = document.getElementById('createdTokenPanel');
const createdTokenMeta = document.getElementById('createdTokenMeta');
const createdTokenValue = document.getElementById('createdTokenValue');
const dismissCreatedTokenButton = document.getElementById('dismissCreatedTokenButton');
const copyCreatedTokenButton = document.getElementById('copyCreatedTokenButton');
const workspaceFacts = document.getElementById('workspaceFacts');
const tokensEmptyState = document.getElementById('tokensEmptyState');
const tokensTableWrap = document.getElementById('tokensTableWrap');
const tokensTableBody = document.getElementById('tokensTableBody');

init();

async function init() {
  authForm.addEventListener('submit', onSignIn);
  createAccountButton.addEventListener('click', onCreateAccount);
  refreshSessionButton.addEventListener('click', () => refreshWorkspace());
  signOutButton.addEventListener('click', onSignOut);
  googleSignInButton.addEventListener('click', onGoogleSignIn);

  brainSelect.addEventListener('change', onBrainChange);
  documentFilterInput.addEventListener('input', onDocumentFilterChange);
  createDocumentButton.addEventListener('click', onCreateDocument);
  refreshDocumentsButton.addEventListener('click', () => refreshDocumentsForActiveBrain());
  saveDocumentButton.addEventListener('click', onSaveDocument);
  revertDocumentButton.addEventListener('click', onRevertDocument);
  deleteDocumentButton.addEventListener('click', onDeleteDocument);
  refreshVersionsButton.addEventListener('click', onRefreshVersions);
  restoreVersionButton.addEventListener('click', onRestoreVersion);
  documentTitleInput.addEventListener('input', onDocumentDraftChanged);
  documentSlugInput.addEventListener('input', onDocumentDraftChanged);
  documentStatusSelect.addEventListener('change', onDocumentDraftChanged);
  documentFrontmatterInput.addEventListener('input', onDocumentDraftChanged);
  documentContentEditor.addEventListener('input', onDocumentDraftChanged);

  createTokenForm.addEventListener('submit', onCreateToken);
  dismissCreatedTokenButton.addEventListener('click', dismissCreatedToken);
  copyCreatedTokenButton.addEventListener('click', onCopyCreatedToken);

  state.authSession = loadStoredAuthSession();
  if (state.authSession?.email) {
    authEmailInput.value = state.authSession.email;
  }

  await loadConfig();

  if (state.authSession) {
    await refreshWorkspace();
  } else {
    renderAll();
  }
}

async function loadConfig() {
  try {
    state.config = await fetchJson('/portal-config');

    renderGoogleAuthSurface();

    if (!state.config.hostedAuthConfigured) {
      showBanner('warn', 'Portal hosted auth is not configured yet. Set Firebase project and API key values first.');
      return;
    }

    if (!state.config.apiBaseUrlConfigured) {
      showBanner('warn', 'Portal API base URL is not configured yet. Set Portal:ApiBaseUrl before using tenant settings.');
    }
  } catch (error) {
    showBanner('error', `Failed to load portal config: ${error.message}`);
  }
}

async function onSignIn(event) {
  event.preventDefault();
  await authenticate('/portal-auth/login');
}

async function onCreateAccount() {
  await authenticate('/portal-auth/register');
}

async function authenticate(url) {
  const email = authEmailInput.value.trim();
  const password = authPasswordInput.value;

  if (!email || !password) {
    showBanner('warn', 'Email and password are required.');
    return;
  }

  try {
    const session = await postJson(url, { email, password });
    state.authSession = buildStoredAuthSession(session);
    saveStoredAuthSession(state.authSession);
    authPasswordInput.value = '';
    sessionStatus.textContent = `Signed in as ${state.authSession.email}. Loading workspace...`;
    await refreshWorkspace();
  } catch (error) {
    showBanner('error', error.message);
    sessionStatus.textContent = 'Authentication failed.';
  }
}

async function refreshWorkspace() {
  hideBanner();

  try {
    const session = await ensureValidSession();
    sessionStatus.textContent = `Refreshing workspace for ${session.email}...`;

    const [context, billing, brainResponse, tokenResponse] = await Promise.all([
      portalFetch('/portal-api/tenant/me', session.idToken),
      portalFetch('/portal-api/tenant/billing/plan', session.idToken),
      portalFetch('/portal-api/tenant/brains', session.idToken),
      portalFetch('/portal-api/tenant/tokens', session.idToken),
    ]);

    state.context = context;
    state.billing = billing;
    state.brains = (brainResponse.brains || [])
      .filter(brain => String(brain.mode || '').toLowerCase() === 'managed-content'
        && String(brain.status || '').toLowerCase() !== 'retired');
    state.tokens = tokenResponse.tokens || [];
    state.activeBrainId = selectActiveBrainId();

    await loadDocumentsForActiveBrain(session.idToken, { preserveSelection: true });
    renderAll();
    sessionStatus.textContent = `Connected to ${context.customerName} as ${context.displayName}. Session expires ${formatRelativeExpiry(session.expiresAt)}.`;
    showBanner('info', 'Workspace data refreshed.');
  } catch (error) {
    clearWorkspaceState();
    renderAll();
    sessionStatus.textContent = 'No active authenticated browser session.';
    showBanner('error', error.message);
  }
}

function clearWorkspaceState() {
  state.context = null;
  state.billing = null;
  state.brains = [];
  state.activeBrainId = '';
  state.documents = [];
  state.filteredDocuments = [];
  state.selectedDocument = null;
  state.documentDraft = buildEmptyDocumentDraft();
  state.isCreatingDocument = false;
  state.documentSaveState = 'idle';
  state.documentSaveMessage = 'Make a change to enable save.';
  state.documentVersions = [];
  state.selectedVersion = null;
  state.tokens = [];
}

function renderGoogleAuthSurface(attempt = 0) {
  if (!state.config?.hostedAuthConfigured) {
    googleAuthStatus.textContent = 'Configure Firebase project and API key values before enabling browser auth.';
    googleSignInButton.disabled = true;
    return;
  }

  if (!initializeFirebaseAuth()) {
    googleAuthStatus.textContent = 'Waiting for the Firebase browser auth SDK to load.';
    googleSignInButton.disabled = true;
    if (attempt < 20) {
      window.setTimeout(() => renderGoogleAuthSurface(attempt + 1), 250);
    }

    return;
  }

  googleAuthStatus.textContent = 'Google sign-in uses the Firebase Authentication provider settings for this project.';
  googleSignInButton.disabled = false;
}

function initializeFirebaseAuth() {
  if (!state.config?.firebaseProjectId || !state.config?.firebaseApiKey) {
    return false;
  }

  if (state.firebaseAuthInitialized && state.firebaseAuth) {
    return true;
  }

  if (!window.firebase?.initializeApp || !window.firebase?.auth) {
    return false;
  }

  if (!window.firebase.apps.length) {
    window.firebase.initializeApp({
      apiKey: state.config.firebaseApiKey,
      authDomain: state.config.firebaseAuthDomain,
      projectId: state.config.firebaseProjectId,
    });
  }

  state.firebaseAuth = window.firebase.auth();
  state.firebaseAuthInitialized = true;
  return true;
}

async function onGoogleSignIn() {
  if (!state.config?.hostedAuthConfigured) {
    showBanner('warn', 'Configure Firebase project and API key values before using Google sign-in.');
    return;
  }

  if (!initializeFirebaseAuth() || !state.firebaseAuth) {
    showBanner('warn', 'Firebase browser auth is not ready yet. Try again in a moment.');
    return;
  }

  try {
    googleSignInButton.disabled = true;
    sessionStatus.textContent = 'Starting Google sign-in...';

    const provider = new window.firebase.auth.GoogleAuthProvider();
    provider.setCustomParameters({ prompt: 'select_account' });

    const userCredential = await state.firebaseAuth.signInWithPopup(provider);
    const user = userCredential?.user;
    if (!user) {
      throw new Error('Google authentication did not return a Firebase user.');
    }

    state.authSession = await buildStoredAuthSessionFromFirebaseUser(user);
    saveStoredAuthSession(state.authSession);
    authEmailInput.value = state.authSession.email || authEmailInput.value;
    authPasswordInput.value = '';
    sessionStatus.textContent = `Signed in with Google as ${state.authSession.email}. Loading workspace...`;
    await refreshWorkspace();
  } catch (error) {
    showBanner('error', normalizeFirebaseClientError(error));
    sessionStatus.textContent = 'Google authentication failed.';
  } finally {
    googleSignInButton.disabled = false;
  }
}

function selectActiveBrainId() {
  if (state.activeBrainId && state.brains.some(brain => brain.brainId === state.activeBrainId)) {
    return state.activeBrainId;
  }

  const preferred = state.context?.brainId || '';
  if (preferred && state.brains.some(brain => brain.brainId === preferred)) {
    return preferred;
  }

  return state.brains[0]?.brainId || '';
}

async function ensureValidSession() {
  const session = loadStoredAuthSession();
  if (!session) {
    throw new Error('Sign in before using the portal.');
  }

  if (isSessionFresh(session)) {
    state.authSession = session;
    return session;
  }

  try {
    const refreshed = await postJson('/portal-auth/refresh', { refreshToken: session.refreshToken });
    const updatedSession = {
      ...session,
      idToken: refreshed.idToken,
      refreshToken: refreshed.refreshToken || session.refreshToken,
      expiresAt: buildExpiryTimestamp(refreshed.expiresIn),
    };

    saveStoredAuthSession(updatedSession);
    state.authSession = updatedSession;
    return updatedSession;
  } catch (error) {
    localStorage.removeItem(storageKey);
    state.authSession = null;
    throw error;
  }
}

async function onSignOut() {
  if (!confirmDiscardDocumentChanges('Sign out and discard unsaved document changes?')) {
    return;
  }

  localStorage.removeItem(storageKey);
  state.authSession = null;
  clearWorkspaceState();

  try {
    if (state.firebaseAuth) {
      await state.firebaseAuth.signOut();
    }
  } catch {
    // Local browser session is already cleared; ignore Firebase sign-out errors.
  }

  authPasswordInput.value = '';
  dismissCreatedToken();
  renderAll();
  sessionStatus.textContent = 'Signed out of the browser session.';
  showBanner('info', 'Portal session cleared.');
}

async function onBrainChange() {
  const nextBrainId = brainSelect.value;
  if (nextBrainId === state.activeBrainId) {
    return;
  }

  if (!confirmDiscardDocumentChanges('Switch brains and discard unsaved document changes?')) {
    brainSelect.value = state.activeBrainId;
    return;
  }

  state.activeBrainId = nextBrainId;
  await refreshDocumentsForActiveBrain({ preserveSelection: false, skipDirtyCheck: true });
}

function onDocumentFilterChange() {
  applyDocumentFilter();
  renderDocuments();
}

function onDocumentDraftChanged() {
  state.documentDraft = readDocumentDraftFromInputs();
  state.documentSaveState = 'idle';
  state.documentSaveMessage = 'Unsaved changes.';
  documentPreviewSurface.innerHTML = renderMarkdown(state.documentDraft.content);
  renderDocumentActionState();
  renderDocumentMeta();
}

async function onCreateDocument() {
  if (!confirmDiscardDocumentChanges('Create a new document and discard unsaved changes?')) {
    return;
  }

  state.selectedDocument = null;
  state.documentDraft = buildEmptyDocumentDraft();
  state.isCreatingDocument = true;
  state.documentSaveState = 'idle';
  state.documentSaveMessage = 'Start typing, then create the document.';
  state.documentVersions = [];
  state.selectedVersion = null;
  renderDocuments();
  renderDocumentEditor();
  showBanner('info', 'New document draft ready.');
}

async function refreshDocumentsForActiveBrain(options = {}) {
  if (!options.skipDirtyCheck && !confirmDiscardDocumentChanges('Refresh documents and discard unsaved changes?')) {
    return;
  }

  try {
    const session = await ensureValidSession();
    await loadDocumentsForActiveBrain(session.idToken, options);
    renderDocuments();
    renderDocumentEditor();
  } catch (error) {
    showBanner('error', error.message);
  }
}

async function loadDocumentsForActiveBrain(idToken, options = {}) {
  if (!state.activeBrainId) {
    state.documents = [];
    state.filteredDocuments = [];
    state.selectedDocument = null;
    state.documentDraft = buildEmptyDocumentDraft();
    state.isCreatingDocument = false;
    state.documentVersions = [];
    state.selectedVersion = null;
    return;
  }

  const response = await portalFetch(
    `/portal-api/tenant/brains/${encodeURIComponent(state.activeBrainId)}/documents?limit=200`,
    idToken);

  state.documents = response.documents || [];
  applyDocumentFilter();

  const selectedId = options.preferredDocumentId
    || (options.preserveSelection ? state.selectedDocument?.managedDocumentId : null);
  const nextDocumentId = selectedId && state.documents.some(document => document.managedDocumentId === selectedId)
    ? selectedId
    : state.documents[0]?.managedDocumentId;

  if (nextDocumentId) {
    await loadDocumentDetail(state.activeBrainId, nextDocumentId, idToken);
  } else {
    state.selectedDocument = null;
    state.documentDraft = buildEmptyDocumentDraft();
    state.isCreatingDocument = false;
    state.documentVersions = [];
    state.selectedVersion = null;
  }
}

async function loadDocumentDetail(brainId, managedDocumentId, idToken) {
  const document = await portalFetch(
    `/portal-api/tenant/brains/${encodeURIComponent(brainId)}/documents/${encodeURIComponent(managedDocumentId)}`,
    idToken);

  state.selectedDocument = document;
  state.documentDraft = buildDraftFromDocument(document);
  state.isCreatingDocument = false;
  await loadDocumentVersions(brainId, managedDocumentId, idToken, { preserveSelection: true });
}

async function loadDocumentVersions(brainId, managedDocumentId, idToken, options = {}) {
  const response = await portalFetch(
    `/portal-api/tenant/brains/${encodeURIComponent(brainId)}/documents/${encodeURIComponent(managedDocumentId)}/versions?limit=25`,
    idToken);

  state.documentVersions = response.versions || [];
  const selectedVersionId = options.preserveSelection ? state.selectedVersion?.managedDocumentVersionId : null;
  const nextVersionId = selectedVersionId && state.documentVersions.some(version => version.managedDocumentVersionId === selectedVersionId)
    ? selectedVersionId
    : state.documentVersions[0]?.managedDocumentVersionId;

  if (!nextVersionId) {
    state.selectedVersion = null;
    return;
  }

  await loadVersionDetail(brainId, managedDocumentId, nextVersionId, idToken);
}

async function loadVersionDetail(brainId, managedDocumentId, managedDocumentVersionId, idToken) {
  state.selectedVersion = await portalFetch(
    `/portal-api/tenant/brains/${encodeURIComponent(brainId)}/documents/${encodeURIComponent(managedDocumentId)}/versions/${encodeURIComponent(managedDocumentVersionId)}`,
    idToken);
}

async function onSelectDocument(managedDocumentId) {
  if (!managedDocumentId || !state.activeBrainId) {
    return;
  }

  if (!confirmDiscardDocumentChanges('Open another document and discard unsaved changes?')) {
    return;
  }

  try {
    const session = await ensureValidSession();
    await loadDocumentDetail(state.activeBrainId, managedDocumentId, session.idToken);
    renderDocuments();
    renderDocumentEditor();
  } catch (error) {
    showBanner('error', error.message);
  }
}

async function onSelectVersion(managedDocumentVersionId) {
  if (!managedDocumentVersionId || !state.selectedDocument?.managedDocumentId) {
    return;
  }

  try {
    const session = await ensureValidSession();
    await loadVersionDetail(
      state.activeBrainId,
      state.selectedDocument.managedDocumentId,
      managedDocumentVersionId,
      session.idToken);
    renderVersionHistory();
    renderVersionPreview();
    renderDocumentActionState();
  } catch (error) {
    showBanner('error', error.message);
  }
}

async function onSaveDocument() {
  if (!state.authSession) {
    setDocumentSaveFeedback('warn', 'Sign in before saving documents.');
    renderDocumentActionState();
    showBanner('warn', 'Sign in before saving documents.');
    return;
  }

  if (!state.activeBrainId) {
    setDocumentSaveFeedback('warn', 'Select a managed-content brain before saving.');
    renderDocumentActionState();
    showBanner('warn', 'Select a managed-content brain before saving.');
    return;
  }

  try {
    setDocumentSaveFeedback('saving', state.isCreatingDocument ? 'Creating document...' : 'Saving document...');
    renderDocumentActionState();
    const session = await ensureValidSession();
    state.documentDraft = readDocumentDraftFromInputs();
    const payload = buildDocumentPayload(state.documentDraft);

    if (state.isCreatingDocument) {
      const created = await portalFetch(
        `/portal-api/tenant/brains/${encodeURIComponent(state.activeBrainId)}/documents`,
        session.idToken,
        {
          method: 'POST',
          body: JSON.stringify(payload),
        });

      await loadDocumentsForActiveBrain(session.idToken, {
        preserveSelection: false,
        preferredDocumentId: created.managedDocumentId,
      });
      setDocumentSaveFeedback('info', `Created document '${created.title}'.`);
      renderAll();
      showBanner('info', `Created document '${created.title}'.`);
      return;
    }

    if (!state.selectedDocument?.managedDocumentId) {
      setDocumentSaveFeedback('warn', 'Select a document or create a new one before saving.');
      renderDocumentActionState();
      showBanner('warn', 'Select a document or create a new one before saving.');
      return;
    }

    const updated = await portalFetch(
      `/portal-api/tenant/brains/${encodeURIComponent(state.activeBrainId)}/documents/${encodeURIComponent(state.selectedDocument.managedDocumentId)}`,
      session.idToken,
      {
        method: 'PUT',
        body: JSON.stringify(payload),
      });

    await loadDocumentsForActiveBrain(session.idToken, {
      preserveSelection: false,
      preferredDocumentId: updated.managedDocumentId,
    });
    setDocumentSaveFeedback('info', `Saved document '${updated.title}'.`);
    renderAll();
    showBanner('info', `Saved document '${updated.title}'.`);
  } catch (error) {
    setDocumentSaveFeedback('error', error.message);
    renderDocumentActionState();
    showBanner('error', error.message);
  }
}

async function onDeleteDocument() {
  if (!state.selectedDocument?.managedDocumentId || state.isCreatingDocument) {
    showBanner('warn', 'Select an existing document before deleting.');
    return;
  }

  if (!window.confirm(`Delete document '${state.selectedDocument.title || state.selectedDocument.managedDocumentId}'?`)) {
    return;
  }

  try {
    const session = await ensureValidSession();
    await portalFetch(
      `/portal-api/tenant/brains/${encodeURIComponent(state.activeBrainId)}/documents/${encodeURIComponent(state.selectedDocument.managedDocumentId)}`,
      session.idToken,
      {
        method: 'DELETE',
      });

    await loadDocumentsForActiveBrain(session.idToken, { preserveSelection: false });
    renderAll();
    showBanner('info', 'Document deleted.');
  } catch (error) {
    showBanner('error', error.message);
  }
}

function onRevertDocument() {
  if (state.isCreatingDocument) {
    state.documentDraft = buildEmptyDocumentDraft();
    state.documentSaveState = 'idle';
    state.documentSaveMessage = 'Start typing, then create the document.';
  } else if (state.selectedDocument) {
    state.documentDraft = buildDraftFromDocument(state.selectedDocument);
    state.documentSaveState = 'idle';
    state.documentSaveMessage = 'Changes reverted to the last saved version.';
  } else {
    state.documentDraft = buildEmptyDocumentDraft();
    state.documentSaveState = 'idle';
    state.documentSaveMessage = 'Make a change to enable save.';
  }

  renderDocumentEditor();
  showBanner('info', state.isCreatingDocument ? 'New document draft cleared.' : 'Document changes reverted.');
}

async function onRefreshVersions() {
  if (!state.selectedDocument?.managedDocumentId || state.isCreatingDocument) {
    showBanner('warn', 'Select a saved document before loading version history.');
    return;
  }

  try {
    const session = await ensureValidSession();
    await loadDocumentVersions(
      state.activeBrainId,
      state.selectedDocument.managedDocumentId,
      session.idToken,
      { preserveSelection: true });
    renderVersionHistory();
    renderVersionPreview();
  } catch (error) {
    showBanner('error', error.message);
  }
}

async function onRestoreVersion() {
  if (!state.selectedDocument?.managedDocumentId || !state.selectedVersion?.managedDocumentVersionId) {
    showBanner('warn', 'Select a document version before restoring.');
    return;
  }

  const restoreTimestamp = formatDateTime(state.selectedVersion.createdAt);
  if (!window.confirm(`Restore the selected version from ${restoreTimestamp}? Current draft changes will be replaced.`)) {
    return;
  }

  try {
    const session = await ensureValidSession();
    const restored = await portalFetch(
      `/portal-api/tenant/brains/${encodeURIComponent(state.activeBrainId)}/documents/${encodeURIComponent(state.selectedDocument.managedDocumentId)}/versions/${encodeURIComponent(state.selectedVersion.managedDocumentVersionId)}/restore`,
      session.idToken,
      {
        method: 'POST',
      });

    await loadDocumentDetail(state.activeBrainId, restored.managedDocumentId, session.idToken);
    renderAll();
    showBanner('info', `Restored version from ${restoreTimestamp}.`);
  } catch (error) {
    showBanner('error', error.message);
  }
}

function applyDocumentFilter() {
  const filter = documentFilterInput.value.trim().toLowerCase();
  if (!filter) {
    state.filteredDocuments = [...state.documents];
    return;
  }

  state.filteredDocuments = state.documents.filter(document =>
    String(document.title || '').toLowerCase().includes(filter)
    || String(document.slug || '').toLowerCase().includes(filter)
    || String(document.canonicalPath || '').toLowerCase().includes(filter));
}

async function onCreateToken(event) {
  event.preventDefault();

  let session;
  try {
    session = await ensureValidSession();
  } catch (error) {
    showBanner('warn', error.message);
    return;
  }

  const name = tokenNameInput.value.trim();
  if (!name) {
    showBanner('warn', 'Token name is required.');
    return;
  }

  const scopes = ['mcp:read'];
  if (scopeWriteInput.checked) {
    scopes.push('mcp:write');
  }

  try {
    const created = await portalFetch('/portal-api/tenant/tokens', session.idToken, {
      method: 'POST',
      body: JSON.stringify({
        name,
        scopes,
        expiresAt: parseExpiresAt(expiresAtInput.value),
      }),
    });

    createdTokenMeta.textContent = `${created.name} | ${created.tokenPrefix} | ${created.scopes.join(', ')}`;
    createdTokenValue.value = created.token || '';
    createdTokenPanel.classList.remove('hidden');

    tokenNameInput.value = '';
    expiresAtInput.value = '';
    scopeWriteInput.checked = false;

    await refreshWorkspace();
    showBanner('info', 'Token created. Save the raw token value before dismissing the panel.');
  } catch (error) {
    showBanner('error', error.message);
  }
}

async function onRevokeToken(apiTokenId) {
  let session;
  try {
    session = await ensureValidSession();
  } catch (error) {
    showBanner('warn', error.message);
    return;
  }

  if (!window.confirm(`Revoke token ${apiTokenId}?`)) {
    return;
  }

  try {
    await portalFetch(`/portal-api/tenant/tokens/${encodeURIComponent(apiTokenId)}`, session.idToken, {
      method: 'DELETE',
    });

    await refreshWorkspace();
    showBanner('info', `Token ${apiTokenId} revoked.`);
  } catch (error) {
    showBanner('error', error.message);
  }
}

function renderAll() {
  renderSummary();
  renderFacts();
  renderBrainOptions();
  renderDocuments();
  renderDocumentEditor();
  renderVersionHistory();
  renderVersionPreview();
  renderTokens();
}

function renderSummary() {
  if (!state.authSession) {
    workspaceName.textContent = 'Signed Out';
    workspaceDetail.textContent = 'Sign in to load workspace context.';
    planName.textContent = 'Unknown';
    planDetail.textContent = 'Billing state appears after sign-in.';
    mcpAccess.textContent = 'Unknown';
    mcpAccessDetail.textContent = 'Scope and write posture appear after sign-in.';
    tokenCount.textContent = '0';
    tokenCountDetail.textContent = 'No token records loaded.';
    return;
  }

  if (!state.context || !state.billing) {
    workspaceName.textContent = state.authSession.email;
    workspaceDetail.textContent = `Authenticated browser session expires ${formatRelativeExpiry(state.authSession.expiresAt)}.`;
    planName.textContent = 'Loading';
    planDetail.textContent = 'Fetching billing state...';
    mcpAccess.textContent = 'Loading';
    mcpAccessDetail.textContent = 'Fetching workspace policy...';
    tokenCount.textContent = String(state.tokens.length);
    tokenCountDetail.textContent = 'Fetching token records...';
    return;
  }

  workspaceName.textContent = state.context.customerName;
  workspaceDetail.textContent = `${state.context.customerSlug} | default brain ${state.context.brainName}`;
  planName.textContent = state.billing.planId || state.context.planId;
  planDetail.textContent = `${state.billing.subscriptionStatus} | ${formatDocumentQuota(state.billing)}`;

  const canWrite = Boolean(state.billing.mcpWrite);
  mcpAccess.textContent = canWrite ? 'Read + Write' : 'Read Only';
  mcpAccessDetail.textContent = canWrite
    ? 'This workspace can mint mcp:write tokens.'
    : 'Requested mcp:write tokens will be rejected until the plan allows it.';

  tokenCount.textContent = String(state.tokens.length);
  tokenCountDetail.textContent = state.tokens.length === 0
    ? 'No tokens issued yet.'
    : `${state.tokens.filter(token => !token.revokedAt).length} active token(s) in this workspace.`;
}

function renderFacts() {
  const facts = !state.context || !state.billing
    ? [
        ['User', state.authSession?.email || 'Not loaded'],
        ['Email', state.authSession?.email || 'Not loaded'],
        ['Role', 'Not loaded'],
        ['Customer', 'Not loaded'],
        ['Brain', 'Not loaded'],
        ['Documents', 'Not loaded'],
        ['MCP Queries', 'Not loaded'],
      ]
    : [
        ['User', state.context.displayName],
        ['Email', state.context.email],
        ['Role', state.context.role],
        ['Customer', `${state.context.customerName} (${state.context.customerId})`],
        ['Brain', `${state.context.brainName} (${state.context.brainId})`],
        ['Documents', `${state.billing.activeDocuments} active of ${formatLimit(state.billing.maxDocuments)}`],
        ['MCP Queries', `${state.billing.mcpQueriesUsed} used of ${formatLimit(state.billing.mcpQueriesPerMonth)}`],
      ];

  workspaceFacts.innerHTML = facts.map(([label, value]) => `
    <div class="fact-row">
      <dt>${escapeHtml(label)}</dt>
      <dd>${escapeHtml(value)}</dd>
    </div>`).join('');
}

function renderBrainOptions() {
  if (!state.authSession) {
    brainSelect.innerHTML = '<option value="">Sign in to load brains</option>';
    brainSelect.disabled = true;
    return;
  }

  if (state.brains.length === 0) {
    brainSelect.innerHTML = '<option value="">No managed-content brains found</option>';
    brainSelect.disabled = true;
    return;
  }

  brainSelect.innerHTML = state.brains.map(brain =>
    `<option value="${escapeAttr(brain.brainId)}">${escapeHtml(brain.name)} (${escapeHtml(brain.brainId)})</option>`).join('');
  brainSelect.disabled = false;
  brainSelect.value = state.activeBrainId;
}

function renderDocuments() {
  if (!state.authSession) {
    documentsEmptyState.textContent = 'Sign in to load documents.';
    documentsEmptyState.classList.remove('hidden');
    documentsTableWrap.classList.add('hidden');
    documentsTableBody.innerHTML = '';
    return;
  }

  if (!state.activeBrainId) {
    documentsEmptyState.textContent = 'No managed-content brain is available for this workspace yet.';
    documentsEmptyState.classList.remove('hidden');
    documentsTableWrap.classList.add('hidden');
    documentsTableBody.innerHTML = '';
    return;
  }

  if (state.filteredDocuments.length === 0) {
    documentsEmptyState.textContent = state.documents.length === 0
      ? 'No documents exist in this brain yet.'
      : 'No documents match the current filter.';
    documentsEmptyState.classList.remove('hidden');
    documentsTableWrap.classList.add('hidden');
    documentsTableBody.innerHTML = '';
    return;
  }

  documentsEmptyState.classList.add('hidden');
  documentsTableWrap.classList.remove('hidden');
  documentsTableBody.innerHTML = state.filteredDocuments.map(document => {
    const isSelected = !state.isCreatingDocument
      && document.managedDocumentId === state.selectedDocument?.managedDocumentId;
    return `
      <tr class="${isSelected ? 'selected-row' : ''}" data-document-id="${escapeAttr(document.managedDocumentId)}">
        <td>${escapeHtml(document.title || '(untitled)')}</td>
        <td>${escapeHtml(document.status || 'draft')}</td>
        <td>${escapeHtml(document.slug || '')}</td>
        <td>${escapeHtml(String(document.wordCount || 0))}</td>
        <td>${escapeHtml(formatDateTime(document.updatedAt))}</td>
      </tr>`;
  }).join('');

  document.querySelectorAll('#documentsTableBody tr[data-document-id]').forEach(row => {
    row.addEventListener('click', () => onSelectDocument(row.dataset.documentId));
  });
}

function renderDocumentEditor() {
  if (!state.authSession) {
    documentDetailTitle.textContent = 'Document Editor';
    documentDetailMeta.textContent = 'Sign in to create or edit documents.';
    documentTitleInput.value = '';
    documentSlugInput.value = '';
    documentStatusSelect.value = 'draft';
    documentFrontmatterInput.value = '';
    documentContentEditor.value = '';
    documentPreviewSurface.innerHTML = '<p>Sign in to preview documents.</p>';
    setDocumentEditorDisabled(true);
    renderDocumentActionState();
    return;
  }

  if (!state.activeBrainId) {
    documentDetailTitle.textContent = 'Document Editor';
    documentDetailMeta.textContent = 'Select a managed-content brain to start authoring.';
    documentTitleInput.value = '';
    documentSlugInput.value = '';
    documentStatusSelect.value = 'draft';
    documentFrontmatterInput.value = '';
    documentContentEditor.value = '';
    documentPreviewSurface.innerHTML = '<p>Select a managed-content brain to preview documents.</p>';
    setDocumentEditorDisabled(true);
    renderDocumentActionState();
    return;
  }

  const draft = state.documentDraft || buildEmptyDocumentDraft();
  documentTitleInput.value = draft.title;
  documentSlugInput.value = draft.slug;
  documentStatusSelect.value = draft.status || 'draft';
  documentFrontmatterInput.value = draft.frontmatterText;
  documentContentEditor.value = draft.content;
  documentPreviewSurface.innerHTML = renderMarkdown(draft.content);
  setDocumentEditorDisabled(false);
  renderDocumentMeta();
  renderDocumentActionState();
}

function renderDocumentMeta() {
  if (!state.authSession) {
    return;
  }

  if (state.isCreatingDocument) {
    documentDetailTitle.textContent = 'New Document';
    documentDetailMeta.textContent = hasUnsavedDocumentChanges()
      ? 'Unsaved draft for the active managed-content brain.'
      : 'Ready to create a new managed-content document.';
    return;
  }

  if (!state.selectedDocument) {
    documentDetailTitle.textContent = 'Document Editor';
    documentDetailMeta.textContent = 'Select a document to inspect or edit its content.';
    return;
  }

  documentDetailTitle.textContent = state.selectedDocument.title || '(untitled)';
  documentDetailMeta.textContent = `${state.selectedDocument.status} | ${state.selectedDocument.slug} | ${state.selectedDocument.canonicalPath} | updated ${formatDateTime(state.selectedDocument.updatedAt)}`;
}

function renderDocumentActionState() {
  const hasSession = Boolean(state.authSession);
  const hasBrain = Boolean(state.activeBrainId);
  const hasSelection = Boolean(state.selectedDocument?.managedDocumentId) && !state.isCreatingDocument;
  const dirty = hasUnsavedDocumentChanges();
  const saving = state.documentSaveState === 'saving';

  createDocumentButton.disabled = !hasSession || !hasBrain;
  saveDocumentButton.disabled = !hasSession || !hasBrain || !dirty || saving;
  revertDocumentButton.disabled = !hasSession || !hasBrain || !dirty || saving;
  deleteDocumentButton.disabled = !hasSelection || saving;
  refreshVersionsButton.disabled = !hasSelection || saving;
  restoreVersionButton.disabled = !hasSelection || !state.selectedVersion?.managedDocumentVersionId || saving;
  saveDocumentButton.textContent = saving
    ? (state.isCreatingDocument ? 'Creating...' : 'Saving...')
    : (state.isCreatingDocument ? 'Create Document' : 'Save Document');

  renderDocumentSaveStatus(dirty);
}

function setDocumentEditorDisabled(disabled) {
  documentTitleInput.disabled = disabled;
  documentSlugInput.disabled = disabled;
  documentStatusSelect.disabled = disabled;
  documentFrontmatterInput.disabled = disabled;
  documentContentEditor.disabled = disabled;
}

function renderVersionHistory() {
  const hasSelection = Boolean(state.selectedDocument?.managedDocumentId) && !state.isCreatingDocument;
  if (!hasSelection) {
    versionsEmptyState.textContent = state.isCreatingDocument
      ? 'Save the new document to start accumulating versions.'
      : 'Select a document to load version history.';
    versionsEmptyState.classList.remove('hidden');
    versionsTableWrap.classList.add('hidden');
    versionsTableBody.innerHTML = '';
    return;
  }

  if (state.documentVersions.length === 0) {
    versionsEmptyState.textContent = 'No saved versions exist for this document yet.';
    versionsEmptyState.classList.remove('hidden');
    versionsTableWrap.classList.add('hidden');
    versionsTableBody.innerHTML = '';
    return;
  }

  versionsEmptyState.classList.add('hidden');
  versionsTableWrap.classList.remove('hidden');
  versionsTableBody.innerHTML = state.documentVersions.map(version => {
    const isSelected = version.managedDocumentVersionId === state.selectedVersion?.managedDocumentVersionId;
    return `
      <tr class="${isSelected ? 'selected-row' : ''}" data-version-id="${escapeAttr(version.managedDocumentVersionId)}">
        <td>${escapeHtml(formatDateTime(version.createdAt))}</td>
        <td>${escapeHtml(version.snapshotKind)}</td>
        <td>${escapeHtml(version.status)}</td>
        <td>${escapeHtml(String(version.wordCount || 0))}</td>
        <td>${escapeHtml(version.snapshotBy)}</td>
      </tr>`;
  }).join('');

  document.querySelectorAll('#versionsTableBody tr[data-version-id]').forEach(row => {
    row.addEventListener('click', () => onSelectVersion(row.dataset.versionId));
  });
}

function renderVersionPreview() {
  if (!state.selectedVersion) {
    versionDetailMeta.textContent = 'Select a saved version to inspect it.';
    versionPreviewSurface.innerHTML = '<p>Select a saved version to preview it here.</p>';
    return;
  }

  versionDetailMeta.textContent = `${state.selectedVersion.snapshotKind} | ${state.selectedVersion.status} | ${formatDateTime(state.selectedVersion.createdAt)} | ${state.selectedVersion.snapshotBy}`;
  versionPreviewSurface.innerHTML = renderMarkdown(state.selectedVersion.content || '');
}

function renderDocumentSaveStatus(dirty) {
  documentSaveStatus.className = 'document-save-status';

  if (!state.authSession) {
    documentSaveStatus.textContent = 'Sign in to create or edit documents.';
    return;
  }

  if (!state.activeBrainId) {
    documentSaveStatus.textContent = 'Select a managed-content brain to start authoring.';
    return;
  }

  if (state.documentSaveState === 'saving') {
    documentSaveStatus.classList.add('status-info');
    documentSaveStatus.textContent = state.documentSaveMessage;
    return;
  }

  if (state.documentSaveState === 'error') {
    documentSaveStatus.classList.add('status-error');
    documentSaveStatus.textContent = state.documentSaveMessage;
    return;
  }

  if (state.documentSaveState === 'warn') {
    documentSaveStatus.classList.add('status-warn');
    documentSaveStatus.textContent = state.documentSaveMessage;
    return;
  }

  if (dirty) {
    documentSaveStatus.textContent = state.documentSaveMessage || 'Unsaved changes.';
    return;
  }

  documentSaveStatus.classList.add('status-info');
  documentSaveStatus.textContent = state.documentSaveMessage || 'All changes saved.';
}

function setDocumentSaveFeedback(stateName, message) {
  state.documentSaveState = stateName;
  state.documentSaveMessage = message;
}

function buildEmptyDocumentDraft() {
  return {
    title: '',
    slug: '',
    status: 'draft',
    frontmatterText: '',
    content: '',
  };
}

function buildDraftFromDocument(document) {
  return {
    title: document.title || '',
    slug: document.slug || '',
    status: document.status || 'draft',
    frontmatterText: serializeFrontmatter(document.frontmatter || {}),
    content: document.content || '',
  };
}

function readDocumentDraftFromInputs() {
  return {
    title: documentTitleInput.value.trim(),
    slug: documentSlugInput.value.trim(),
    status: documentStatusSelect.value || 'draft',
    frontmatterText: documentFrontmatterInput.value,
    content: documentContentEditor.value,
  };
}

function buildDocumentPayload(draft) {
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

function parseFrontmatterText(value) {
  const result = {};
  const lines = String(value || '').split(/\r?\n/);

  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index].trim();
    if (!line) {
      continue;
    }

    if (line === '---' || line === '...') {
      continue;
    }

    if (line.startsWith('#')) {
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

function serializeFrontmatter(frontmatter) {
  return Object.entries(frontmatter)
    .map(([key, value]) => `${key}: ${value}`)
    .join('\n');
}

function hasUnsavedDocumentChanges() {
  const currentDraft = normalizeDocumentDraft(readDocumentDraftFromInputsSafe());
  if (state.isCreatingDocument) {
    return JSON.stringify(currentDraft) !== JSON.stringify(normalizeDocumentDraft(buildEmptyDocumentDraft()));
  }

  if (!state.selectedDocument) {
    return false;
  }

  return JSON.stringify(currentDraft) !== JSON.stringify(normalizeDocumentDraft(buildDraftFromDocument(state.selectedDocument)));
}

function readDocumentDraftFromInputsSafe() {
  if (!documentTitleInput) {
    return state.documentDraft || buildEmptyDocumentDraft();
  }

  return {
    title: documentTitleInput.value || '',
    slug: documentSlugInput.value || '',
    status: documentStatusSelect.value || 'draft',
    frontmatterText: documentFrontmatterInput.value || '',
    content: documentContentEditor.value || '',
  };
}

function normalizeDocumentDraft(draft) {
  return {
    title: String(draft.title || '').trim(),
    slug: String(draft.slug || '').trim(),
    status: String(draft.status || 'draft').trim() || 'draft',
    frontmatterText: String(draft.frontmatterText || '').replace(/\r\n/g, '\n').trim(),
    content: String(draft.content || '').replace(/\r\n/g, '\n'),
  };
}

function confirmDiscardDocumentChanges(message) {
  if (!hasUnsavedDocumentChanges()) {
    return true;
  }

  return window.confirm(message);
}

function renderTokens() {
  if (state.tokens.length === 0) {
    tokensEmptyState.textContent = state.authSession
      ? 'No tokens have been issued for this workspace yet.'
      : 'Sign in to load tokens.';
    tokensEmptyState.classList.remove('hidden');
    tokensTableWrap.classList.add('hidden');
    tokensTableBody.innerHTML = '';
    return;
  }

  tokensEmptyState.classList.add('hidden');
  tokensTableWrap.classList.remove('hidden');
  tokensTableBody.innerHTML = state.tokens.map(token => {
    const status = getTokenStatus(token);
    const revokeButton = token.revokedAt
      ? ''
      : `<button type="button" class="button button-danger revoke-token-button" data-token-id="${escapeAttr(token.apiTokenId)}">Revoke</button>`;

    return `
      <tr>
        <td>
          <div class="token-name">${escapeHtml(token.name)}</div>
          <div class="summary-detail">${escapeHtml(token.apiTokenId)}</div>
        </td>
        <td><span class="token-prefix">${escapeHtml(token.tokenPrefix)}</span></td>
        <td>${token.scopes.map(scope => `<span class="scope-chip">${escapeHtml(scope)}</span>`).join('')}</td>
        <td>${escapeHtml(formatDateTime(token.createdAt))}</td>
        <td>${escapeHtml(formatDateTime(token.lastUsedAt))}</td>
        <td>${escapeHtml(formatDateTime(token.expiresAt))}</td>
        <td><span class="status-chip ${status.className}">${escapeHtml(status.label)}</span></td>
        <td>${revokeButton}</td>
      </tr>`;
  }).join('');

  document.querySelectorAll('.revoke-token-button').forEach(button => {
    button.addEventListener('click', () => onRevokeToken(button.dataset.tokenId));
  });
}

function getTokenStatus(token) {
  if (token.revokedAt) {
    return { label: 'Revoked', className: 'status-chip-revoked' };
  }

  if (token.expiresAt && new Date(token.expiresAt) <= new Date()) {
    return { label: 'Expired', className: 'status-chip-expired' };
  }

  return { label: 'Active', className: 'status-chip-active' };
}

function dismissCreatedToken() {
  createdTokenValue.value = '';
  createdTokenMeta.textContent = '';
  createdTokenPanel.classList.add('hidden');
}

async function onCopyCreatedToken() {
  if (!createdTokenValue.value) {
    return;
  }

  try {
    await navigator.clipboard.writeText(createdTokenValue.value);
    showBanner('info', 'Raw token copied to clipboard.');
  } catch {
    showBanner('warn', 'Clipboard copy failed. Copy the token manually before dismissing it.');
  }
}

async function portalFetch(url, idToken, options = {}) {
  const headers = {
    Authorization: `Bearer ${idToken}`,
  };

  if (options.body) {
    headers['Content-Type'] = 'application/json';
  }

  const response = await fetch(url, {
    method: options.method || 'GET',
    headers,
    body: options.body,
  });

  const contentType = response.headers.get('content-type') || '';
  const payload = contentType.includes('application/json')
    ? await response.json()
    : await response.text();

  if (!response.ok) {
    throw new Error(extractErrorMessage(payload, response.status));
  }

  return payload;
}

async function postJson(url, body) {
  const response = await fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(body),
  });

  const contentType = response.headers.get('content-type') || '';
  const payload = contentType.includes('application/json')
    ? await response.json()
    : await response.text();

  if (!response.ok) {
    throw new Error(extractErrorMessage(payload, response.status));
  }

  return payload;
}

async function fetchJson(url) {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`);
  }

  return response.json();
}

function extractErrorMessage(payload, status) {
  if (typeof payload === 'string' && payload.trim()) {
    return payload;
  }

  if (payload?.detail) {
    return `${payload.title || 'Request failed'}: ${payload.detail}`;
  }

  if (payload?.message) {
    return payload.message;
  }

  if (payload?.title) {
    return payload.title;
  }

  return `Request failed with HTTP ${status}.`;
}

function normalizeFirebaseClientError(error) {
  const code = error?.code || '';

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
      return error?.message || 'Google sign-in failed.';
  }
}

function loadStoredAuthSession() {
  const raw = localStorage.getItem(storageKey);
  if (!raw) {
    return null;
  }

  try {
    return JSON.parse(raw);
  } catch {
    localStorage.removeItem(storageKey);
    return null;
  }
}

function saveStoredAuthSession(session) {
  localStorage.setItem(storageKey, JSON.stringify(session));
}

function buildStoredAuthSession(session) {
  return {
    idToken: session.idToken,
    refreshToken: session.refreshToken,
    email: session.email,
    displayName: session.displayName || (session.email || '').split('@', 1)[0] || '',
    expiresAt: session.expiresAt || buildExpiryTimestamp(session.expiresIn),
  };
}

async function buildStoredAuthSessionFromFirebaseUser(user) {
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
  });
}

function buildExpiryTimestamp(expiresInSeconds) {
  const seconds = Number.parseInt(expiresInSeconds, 10);
  const safeSeconds = Number.isFinite(seconds) ? seconds : 3600;
  return new Date(Date.now() + safeSeconds * 1000).toISOString();
}

function isSessionFresh(session) {
  if (!session?.expiresAt) {
    return false;
  }

  const expiresAt = new Date(session.expiresAt).valueOf();
  if (Number.isNaN(expiresAt)) {
    return false;
  }

  return expiresAt - Date.now() > sessionRefreshSkewMs;
}

function parseExpiresAt(value) {
  if (!value) {
    return null;
  }

  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? null : date.toISOString();
}

function formatDateTime(value) {
  if (!value) {
    return 'Never';
  }

  return new Date(value).toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  });
}

function formatRelativeExpiry(value) {
  if (!value) {
    return 'soon';
  }

  return formatDateTime(value);
}

function formatDocumentQuota(billing) {
  return `${billing.activeDocuments} active of ${formatLimit(billing.maxDocuments)} documents`;
}

function formatLimit(value) {
  return value < 0 ? 'unlimited' : String(value);
}

function renderMarkdown(markdown) {
  const source = String(markdown || '').replace(/\r\n/g, '\n').trim();
  if (!source) {
    return '<p>Nothing to preview yet.</p>';
  }

  const lines = source.split('\n');
  const blocks = [];
  let paragraph = [];
  let listItems = [];
  let codeFence = null;

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

    blocks.push(`<ul>${listItems.map(item => `<li>${renderInlineMarkdown(item)}</li>`).join('')}</ul>`);
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

function renderInlineMarkdown(value) {
  return escapeHtml(value)
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/\*([^*]+)\*/g, '<em>$1</em>')
    .replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g, '<a href="$2" target="_blank" rel="noreferrer">$1</a>');
}

function showBanner(type, message) {
  statusBanner.className = `status-banner status-${type}`;
  statusBanner.textContent = message;
  statusBanner.classList.remove('hidden');
  statusBanner.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

function hideBanner() {
  statusBanner.classList.add('hidden');
}

function escapeHtml(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function escapeAttr(value) {
  return String(value ?? '').replace(/"/g, '&quot;');
}
