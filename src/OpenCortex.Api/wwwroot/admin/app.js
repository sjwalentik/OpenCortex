const state = {
  health: null,
  brains: [],
  runs: [],
  errors: {
    health: null,
    brains: null,
    runs: null,
  },
  selectedRunId: null,
};

const serviceStatusEl = document.getElementById('serviceStatus');
const serviceDetailEl = document.getElementById('serviceDetail');
const brainCountEl = document.getElementById('brainCount');
const brainDetailEl = document.getElementById('brainDetail');
const runCountEl = document.getElementById('runCount');
const runDetailEl = document.getElementById('runDetail');
const runHealthEl = document.getElementById('runHealth');
const runHealthDetailEl = document.getElementById('runHealthDetail');
const brainsEl = document.getElementById('brains');
const runsEl = document.getElementById('runs');
const errorsEl = document.getElementById('errors');
const selectedRunLabelEl = document.getElementById('selectedRunLabel');
const queryBrainEl = document.getElementById('queryBrain');
const queryFormEl = document.getElementById('queryForm');
const queryResultEl = document.getElementById('queryResult');

document.getElementById('refreshAll').addEventListener('click', () => refreshAll());
document.getElementById('createBrainForm').addEventListener('submit', onCreateBrainSubmit);
queryFormEl.addEventListener('submit', onQuerySubmit);

refreshAll();

async function refreshAll() {
  await Promise.all([loadHealth(), loadBrains(), loadRuns()]);
}

async function loadHealth() {
  try {
    state.health = await fetchJson('/health');
    state.errors.health = null;
  } catch (error) {
    state.health = null;
    state.errors.health = error;
  }

  renderSummary();
}

async function loadBrains() {
  try {
    state.brains = await fetchJson('/admin/brains/health');
    state.errors.brains = null;
  } catch (error) {
    state.brains = [];
    state.errors.brains = error;
  }

  renderBrains();
  renderBrainOptions();
  renderSummary();
}

async function loadRuns() {
  try {
    state.runs = await fetchJson('/indexing/runs?limit=25');
    state.errors.runs = null;
  } catch (error) {
    state.runs = [];
    state.errors.runs = error;
  }

  renderRuns();
  renderSummary();

  if (!state.errors.runs && state.selectedRunId) {
    await loadErrors(state.selectedRunId);
  }
}

function renderSummary() {
  if (state.errors.health) {
    serviceStatusEl.textContent = 'Unavailable';
    serviceDetailEl.textContent = 'Health endpoint could not be loaded.';
  }

  if (state.health) {
    const validationErrors = state.health.validationErrors || [];
    serviceStatusEl.textContent = validationErrors.length === 0 ? 'Ready' : 'Config Issues';
    serviceDetailEl.textContent = validationErrors.length === 0
      ? 'API health check is clean.'
      : `${validationErrors.length} validation issue(s) need operator attention.`;
  }

  brainCountEl.textContent = String(state.brains.length);
  if (state.errors.brains) {
    brainDetailEl.textContent = 'Brain health data is currently unavailable.';
  }

  brainDetailEl.textContent = state.brains.length === 0
    ? (state.errors.brains ? 'Brain health data is currently unavailable.' : 'No configured brains were returned.')
    : `${countByMode('filesystem')} filesystem brain(s) loaded.`;

  runCountEl.textContent = String(state.runs.length);
  if (state.runs.length === 0) {
    runDetailEl.textContent = state.errors.runs
      ? 'Run history is currently unavailable.'
      : 'No recent index runs recorded.';
    runHealthEl.textContent = state.errors.runs ? 'Unavailable' : 'No Data';
    runHealthDetailEl.textContent = state.errors.runs
      ? 'The runs endpoint could not be loaded.'
      : 'Run health appears after the first index job.';
    return;
  }

  const latestRun = state.runs[0];
  const failedRuns = state.runs.filter(run => run.status === 'failed').length;
  const runningRuns = state.runs.filter(run => run.status === 'running').length;
  const completedRuns = state.runs.filter(run => run.status === 'completed').length;

  runDetailEl.textContent = `Latest: ${latestRun.brainId} at ${formatTimestamp(latestRun.startedAt)}.`;
  runHealthEl.textContent = failedRuns > 0 ? 'Attention' : runningRuns > 0 ? 'Active' : 'Stable';
  runHealthDetailEl.textContent = `${completedRuns} completed, ${runningRuns} running, ${failedRuns} failed.`;
}

function renderBrains() {
  brainsEl.innerHTML = '';

  if (state.brains.length === 0) {
    brainsEl.innerHTML = state.errors.brains
      ? '<div class="empty-state">Unable to load brain health right now.</div>'
      : '<div class="empty-state">No brains found.</div>';
    return;
  }

  const template = document.getElementById('brainTemplate');

  for (const brain of state.brains) {
    const node = template.content.firstElementChild.cloneNode(true);
    node.querySelector('h3').textContent = brain.name;
    node.querySelector('.mode-chip').textContent = brain.mode;
    node.querySelector('.brain-meta').textContent = `${brain.slug} · ${brain.status} · ${brain.sourceRootCount} source root(s)`;
    const healthChipEl = node.querySelector('.health-chip');
    const healthState = getBrainHealthState(brain);
    healthChipEl.textContent = healthState.label;
    healthChipEl.classList.add(healthState.className);
    node.querySelector('.brain-health-detail').textContent = describeBrainHealth(brain);
    const runIndexBtn = node.querySelector('.run-index');
    const previewIndexBtn = node.querySelector('.preview-index');
    const addSourceRootBtn = node.querySelector('.add-source-root');
    const retireBrainBtn = node.querySelector('.retire-brain');

    if (!brain.isConfigured) {
      runIndexBtn.disabled = true;
      runIndexBtn.title = 'This brain is no longer in the current config and cannot be indexed.';
      previewIndexBtn.disabled = true;
      previewIndexBtn.title = 'This brain is no longer in the current config.';
    } else {
      runIndexBtn.addEventListener('click', () => runIndex(brain.brainId));
      previewIndexBtn.addEventListener('click', () => previewIndex(brain.brainId));
    }

    addSourceRootBtn.addEventListener('click', () => toggleSourceRootForm(node, brain.brainId));
    retireBrainBtn.addEventListener('click', () => retireBrain(brain.brainId, brain.name));
    brainsEl.appendChild(node);
  }
}

function renderBrainOptions() {
  const currentValue = queryBrainEl.value;
  queryBrainEl.innerHTML = '';

  for (const brain of state.brains) {
    const option = document.createElement('option');
    option.value = brain.brainId;
    option.textContent = `${brain.name} (${brain.brainId})`;
    queryBrainEl.appendChild(option);
  }

  if (currentValue && state.brains.some(brain => brain.brainId === currentValue)) {
    queryBrainEl.value = currentValue;
  }
}

function renderRuns() {
  runsEl.innerHTML = '';

  if (state.runs.length === 0) {
    runsEl.innerHTML = state.errors.runs
      ? '<div class="empty-state">Unable to load index runs right now.</div>'
      : '<div class="empty-state">No index runs found.</div>';
    return;
  }

  const template = document.getElementById('runTemplate');

  for (const run of state.runs) {
    const node = template.content.firstElementChild.cloneNode(true);
    node.classList.toggle('active', run.indexRunId === state.selectedRunId);
    node.querySelector('.run-brain').textContent = run.brainId;
    const statusEl = node.querySelector('.run-status');
    statusEl.textContent = run.status;
    statusEl.classList.add(run.status);
    node.querySelector('.run-meta').textContent = `${run.triggerType} · seen ${run.documentsSeen} · indexed ${run.documentsIndexed} · failed ${run.documentsFailed}`;
    node.querySelector('.run-id').textContent = run.indexRunId;
    node.addEventListener('click', () => selectRun(run.indexRunId));
    runsEl.appendChild(node);
  }
}

async function selectRun(indexRunId) {
  state.selectedRunId = indexRunId;
  renderRuns();
  await loadErrors(indexRunId);
}

async function loadErrors(indexRunId) {
  selectedRunLabelEl.textContent = indexRunId;
  const errors = await fetchJson(`/indexing/runs/${encodeURIComponent(indexRunId)}/errors`);
  renderErrors(errors);
}

function renderErrors(errors) {
  errorsEl.innerHTML = '';

  if (!errors.length) {
    errorsEl.innerHTML = '<div class="empty-state">No errors recorded for this run.</div>';
    return;
  }

  for (const error of errors) {
    const card = document.createElement('article');
    card.className = 'error-card';
    card.innerHTML = `
      <strong>${escapeHtml(error.errorCode)}</strong>
      <p>${escapeHtml(error.errorMessage)}</p>
      <code>${escapeHtml(error.indexRunErrorId)}</code>
    `;
    errorsEl.appendChild(card);
  }
}

async function runIndex(brainId) {
  await fetchJson(`/indexing/run/${encodeURIComponent(brainId)}`, { method: 'POST' });
  await loadRuns();
}

async function previewIndex(brainId) {
  const preview = await fetchJson(`/indexing/preview/${encodeURIComponent(brainId)}`);
  queryResultEl.innerHTML = `
    <article class="result-card">
      <strong>Preview for ${escapeHtml(preview.brainId)}</strong>
      <p>${preview.documentCount} document(s), ${preview.chunkCount} chunk(s), ${preview.linkEdgeCount} link edge(s)</p>
    </article>
  `;
}

async function onQuerySubmit(event) {
  event.preventDefault();

  const brainId = queryBrainEl.value;
  const search = document.getElementById('querySearch').value.trim();
  const rank = document.getElementById('queryRank').value;
  const where = document.getElementById('queryWhere').value.trim();
  const limit = document.getElementById('queryLimit').value || '5';

  const lines = [`FROM brain("${brainId}")`];
  if (search) lines.push(`SEARCH "${search}"`);
  if (where) lines.push(`WHERE ${where}`);
  lines.push(`RANK ${rank}`);
  lines.push(`LIMIT ${limit}`);

  const result = await fetchJson('/query', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ oql: lines.join('\n') }),
  });

  renderQueryResult(result);
}

function renderQueryResult(payload) {
  queryResultEl.innerHTML = '';

  if (!payload.results.length) {
    queryResultEl.innerHTML = '<div class="empty-state">No results returned.</div>';
    return;
  }

  for (const result of payload.results) {
    const card = document.createElement('article');
    card.className = 'result-card';
    card.innerHTML = `
      <div class="run-topline">
        <strong>${escapeHtml(result.title)}</strong>
        <span class="chip">${escapeHtml(result.reason)}</span>
      </div>
      <p>${escapeHtml(result.canonicalPath)}</p>
      <p>${escapeHtml(result.snippet || '')}</p>
      <code>score ${Number(result.score).toFixed(3)}</code>
    `;
    queryResultEl.appendChild(card);
  }
}

async function fetchJson(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `Request failed: ${response.status}`);
  }
  return response.json();
}

function countByMode(mode) {
  return state.brains.filter(brain => brain.mode === mode).length;
}

function getBrainHealthState(brain) {
  if (!brain.isConfigured) {
    return { label: 'Retired', className: 'stable' };
  }

  if (brain.latestRunStatus === 'failed') {
    return { label: 'Needs Attention', className: 'attention' };
  }

  if (brain.isLatestRunActive || brain.latestRunStatus === 'running') {
    return { label: 'Indexing', className: 'active' };
  }

  if (brain.latestRunStatus === 'completed') {
    return { label: 'Healthy', className: 'healthy' };
  }

  return { label: 'Not Indexed', className: 'stable' };
}

function describeBrainHealth(brain) {
  if (!brain.isConfigured) {
    return 'This brain is no longer present in the current config. Its history is preserved but it cannot be reindexed.';
  }

  if (brain.latestRunStatus === 'never-run') {
    return 'No index run has completed yet for this brain.';
  }

  const parts = [];
  parts.push(`Last run ${brain.latestRunStatus} at ${formatTimestamp(brain.latestRunStartedAt)}.`);

  if (typeof brain.latestDocumentsIndexed === 'number' && typeof brain.latestDocumentsSeen === 'number') {
    parts.push(`Indexed ${brain.latestDocumentsIndexed} of ${brain.latestDocumentsSeen} document(s).`);
  }

  if (brain.latestDocumentsFailed) {
    parts.push(`${brain.latestDocumentsFailed} document(s) failed.`);
  }

  if (brain.latestErrorSummary) {
    parts.push(brain.latestErrorSummary);
  }

  if (!brain.isLatestRunActive && brain.runningRunCount > 0) {
    parts.push(`${brain.runningRunCount} older run(s) still show as running in history.`);
  }

  return parts.join(' ');
}

async function onCreateBrainSubmit(event) {
  event.preventDefault();

  const brainId = document.getElementById('newBrainId').value.trim();
  const name = document.getElementById('newBrainName').value.trim();
  const slug = document.getElementById('newBrainSlug').value.trim() || brainId;
  const mode = document.getElementById('newBrainMode').value;
  const resultEl = document.getElementById('createBrainResult');

  try {
    const brain = await fetchJson('/admin/brains', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ brainId, name, slug, mode }),
    });

    resultEl.innerHTML = `
      <article class="result-card">
        <strong>${escapeHtml(brain.name)}</strong>
        <p>Brain <code>${escapeHtml(brain.brainId)}</code> created with status <em>${escapeHtml(brain.status)}</em>.</p>
      </article>
    `;
    event.target.reset();
    await loadBrains();
  } catch (err) {
    resultEl.innerHTML = `<div class="empty-state">${escapeHtml(String(err))}</div>`;
  }
}

function toggleSourceRootForm(brainCardNode, brainId) {
  const existing = brainCardNode.querySelector('.source-root-form');
  if (existing) {
    existing.remove();
    return;
  }

  const form = document.createElement('form');
  form.className = 'source-root-form';
  form.innerHTML = `
    <label><span>Root ID</span><input name="sourceRootId" type="text" placeholder="my-docs" required></label>
    <label><span>Path</span><input name="path" type="text" placeholder="C:\\docs or //server/share" required></label>
    <label><span>Path Type</span>
      <select name="pathType">
        <option value="local">local</option>
        <option value="unc">unc</option>
        <option value="nas">nas</option>
      </select>
    </label>
    <div style="display:flex;gap:8px;align-items:end">
      <button type="submit" class="button button-primary">Add</button>
      <button type="button" class="button cancel-source-root">Cancel</button>
    </div>
  `;

  form.querySelector('.cancel-source-root').addEventListener('click', () => form.remove());

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    const data = Object.fromEntries(new FormData(form));
    try {
      await fetchJson(`/admin/brains/${encodeURIComponent(brainId)}/source-roots`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          sourceRootId: data.sourceRootId,
          path: data.path,
          pathType: data.pathType,
          isWritable: false,
          includePatterns: ['**/*.md'],
          excludePatterns: [],
          watchMode: 'scheduled',
        }),
      });
      form.remove();
      await loadBrains();
    } catch (err) {
      alert(`Failed to add source root: ${err}`);
    }
  });

  // Append below the brain card content
  brainCardNode.appendChild(form);
}

async function retireBrain(brainId, brainName) {
  if (!confirm(`Retire brain "${brainName}" (${brainId})? It will remain visible in history but cannot be reindexed.`)) {
    return;
  }

  try {
    await fetchJson(`/admin/brains/${encodeURIComponent(brainId)}`, { method: 'DELETE' });
    await loadBrains();
  } catch (err) {
    alert(`Failed to retire brain: ${err}`);
  }
}

function formatTimestamp(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return 'unknown time';
  }

  return date.toLocaleString();
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}
