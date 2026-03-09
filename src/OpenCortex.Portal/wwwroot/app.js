'use strict';

const storageKey = 'opencortex.portal.auth_session';
const sessionRefreshSkewMs = 5 * 60 * 1000;

const state = {
  config: null,
  authSession: null,
  context: null,
  billing: null,
  tokens: [],
};

const statusBanner = document.getElementById('statusBanner');
const authForm = document.getElementById('authForm');
const authEmailInput = document.getElementById('authEmailInput');
const authPasswordInput = document.getElementById('authPasswordInput');
const createAccountButton = document.getElementById('createAccountButton');
const refreshSessionButton = document.getElementById('refreshSessionButton');
const signOutButton = document.getElementById('signOutButton');
const sessionStatus = document.getElementById('sessionStatus');
const workspaceName = document.getElementById('workspaceName');
const workspaceDetail = document.getElementById('workspaceDetail');
const planName = document.getElementById('planName');
const planDetail = document.getElementById('planDetail');
const mcpAccess = document.getElementById('mcpAccess');
const mcpAccessDetail = document.getElementById('mcpAccessDetail');
const tokenCount = document.getElementById('tokenCount');
const tokenCountDetail = document.getElementById('tokenCountDetail');
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
    renderSummary();
    renderFacts();
    renderTokens();
  }
}

async function loadConfig() {
  try {
    state.config = await fetchJson('/portal-config');

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

    const [context, billing, tokenResponse] = await Promise.all([
      portalFetch('/portal-api/tenant/me', session.idToken),
      portalFetch('/portal-api/tenant/billing/plan', session.idToken),
      portalFetch('/portal-api/tenant/tokens', session.idToken),
    ]);

    state.context = context;
    state.billing = billing;
    state.tokens = tokenResponse.tokens || [];

    renderSummary();
    renderFacts();
    renderTokens();
    sessionStatus.textContent = `Connected to ${context.customerName} as ${context.displayName}. Session expires ${formatRelativeExpiry(session.expiresAt)}.`;
    showBanner('info', 'Workspace data refreshed.');
  } catch (error) {
    state.context = null;
    state.billing = null;
    state.tokens = [];
    renderSummary();
    renderFacts();
    renderTokens();
    sessionStatus.textContent = 'No active authenticated browser session.';
    showBanner('error', error.message);
  }
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
}

function onSignOut() {
  localStorage.removeItem(storageKey);
  state.authSession = null;
  state.context = null;
  state.billing = null;
  state.tokens = [];
  authPasswordInput.value = '';
  dismissCreatedToken();
  renderSummary();
  renderFacts();
  renderTokens();
  sessionStatus.textContent = 'Signed out of the browser session.';
  showBanner('info', 'Portal session cleared.');
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
    'Authorization': `Bearer ${idToken}`,
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
    displayName: session.displayName || session.email?.split('@', 1)[0] || '',
    expiresAt: buildExpiryTimestamp(session.expiresIn),
  };
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

function showBanner(type, message) {
  statusBanner.className = `status-banner status-${type}`;
  statusBanner.textContent = message;
  statusBanner.classList.remove('hidden');
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
