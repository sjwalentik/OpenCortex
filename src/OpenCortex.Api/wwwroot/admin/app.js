const state = {
  health: null,
  brains: [],
  runs: [],
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
queryFormEl.addEventListener('submit', onQuerySubmit);

refreshAll();

async function refreshAll() {
  await Promise.all([loadHealth(), loadBrains(), loadRuns()]);
}

async function loadHealth() {
  state.health = await fetchJson('/health');
  renderSummary();
}

async function loadBrains() {
  state.brains = await fetchJson('/brains');
  renderBrains();
  renderBrainOptions();
  renderSummary();
}

async function loadRuns() {
  state.runs = await fetchJson('/indexing/runs?limit=25');
  renderRuns();
  renderSummary();

  if (state.selectedRunId) {
    await loadErrors(state.selectedRunId);
  }
}

function renderSummary() {
  if (state.health) {
    const validationErrors = state.health.validationErrors || [];
    serviceStatusEl.textContent = validationErrors.length === 0 ? 'Ready' : 'Config Issues';
    serviceDetailEl.textContent = validationErrors.length === 0
      ? 'API health check is clean.'
      : `${validationErrors.length} validation issue(s) need operator attention.`;
  }

  brainCountEl.textContent = String(state.brains.length);
  brainDetailEl.textContent = state.brains.length === 0
    ? 'No configured brains were returned.'
    : `${countByMode('filesystem')} filesystem brain(s) loaded.`;

  runCountEl.textContent = String(state.runs.length);
  if (state.runs.length === 0) {
    runDetailEl.textContent = 'No recent index runs recorded.';
    runHealthEl.textContent = 'No Data';
    runHealthDetailEl.textContent = 'Run health appears after the first index job.';
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
    brainsEl.innerHTML = '<div class="empty-state">No brains found.</div>';
    return;
  }

  const template = document.getElementById('brainTemplate');

  for (const brain of state.brains) {
    const node = template.content.firstElementChild.cloneNode(true);
    node.querySelector('h3').textContent = brain.name;
    node.querySelector('.mode-chip').textContent = brain.mode;
    node.querySelector('.brain-meta').textContent = `${brain.slug} · ${brain.status} · ${brain.sourceRootCount} source root(s)`;
    node.querySelector('.run-index').addEventListener('click', () => runIndex(brain.brainId));
    node.querySelector('.preview-index').addEventListener('click', () => previewIndex(brain.brainId));
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
    runsEl.innerHTML = '<div class="empty-state">No index runs found.</div>';
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
