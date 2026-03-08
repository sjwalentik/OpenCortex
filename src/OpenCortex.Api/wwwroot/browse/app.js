'use strict';

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

let allDocuments = [];

// ---------------------------------------------------------------------------
// DOM refs
// ---------------------------------------------------------------------------

const brainSelect       = document.getElementById('brainSelect');
const pathPrefixInput   = document.getElementById('pathPrefixInput');
const limitInput        = document.getElementById('limitInput');
const loadButton        = document.getElementById('loadDocuments');
const statusBanner      = document.getElementById('statusBanner');
const documentsSection  = document.getElementById('documentsSection');
const documentCount     = document.getElementById('documentCount');
const documentTableBody = document.getElementById('documentTableBody');
const filterInput       = document.getElementById('filterInput');
const filteredNote      = document.getElementById('filteredNote');
const emptyState        = document.getElementById('emptyState');
const emptyMessage      = document.getElementById('emptyMessage');

// ---------------------------------------------------------------------------
// Startup
// ---------------------------------------------------------------------------

async function init() {
  await loadBrains();
  loadButton.addEventListener('click', onLoadDocuments);
  filterInput.addEventListener('input', onFilter);
  brainSelect.addEventListener('change', () => {
    loadButton.disabled = !brainSelect.value;
  });
}

// ---------------------------------------------------------------------------
// Load brains into selector
// ---------------------------------------------------------------------------

async function loadBrains() {
  try {
    const res = await fetch('/admin/brains/health');
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const data = await res.json();

    const active = data.filter(b => b.isConfigured && b.status !== 'retired');

    brainSelect.innerHTML = active.length === 0
      ? '<option value="">No active brains found</option>'
      : '<option value="">Select a brain...</option>' +
        active.map(b =>
          `<option value="${escapeAttr(b.brainId)}">${escapeHtml(b.name)} (${escapeHtml(b.brainId)})</option>`
        ).join('');

    loadButton.disabled = true;
  } catch (err) {
    showBanner('error', `Failed to load brains: ${err.message}`);
    brainSelect.innerHTML = '<option value="">Error loading brains</option>';
  }
}

// ---------------------------------------------------------------------------
// Load documents
// ---------------------------------------------------------------------------

async function onLoadDocuments() {
  const brainId = brainSelect.value;
  if (!brainId) return;

  const pathPrefix = pathPrefixInput.value.trim() || null;
  const limit      = parseInt(limitInput.value, 10) || 200;

  const params = new URLSearchParams();
  if (pathPrefix) params.set('pathPrefix', pathPrefix);
  params.set('limit', String(limit));

  hideBanner();
  showLoading();

  try {
    const url = `/browse/brains/${encodeURIComponent(brainId)}/documents?${params.toString()}`;
    const res = await fetch(url);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const data = await res.json();

    allDocuments = data.documents || [];
    renderDocuments(allDocuments);

    if (allDocuments.length === 0) {
      showEmptyState('No documents found for this brain with the current filters.');
    } else {
      showDocumentsSection();
      documentCount.textContent = `${allDocuments.length} document${allDocuments.length === 1 ? '' : 's'}`;
    }
  } catch (err) {
    showBanner('error', `Failed to load documents: ${err.message}`);
    showEmptyState('Could not load documents. Check the API connection.');
  }
}

// ---------------------------------------------------------------------------
// Render
// ---------------------------------------------------------------------------

function renderDocuments(docs) {
  documentTableBody.innerHTML = docs.map(doc => {
    const updated  = doc.sourceUpdatedAt ? formatDate(doc.sourceUpdatedAt) : '—';
    const indexed  = doc.indexedAt       ? formatDate(doc.indexedAt)       : '—';
    const docType  = doc.documentType    ? `<span class="type-chip">${escapeHtml(doc.documentType)}</span>` : '—';

    return `<tr class="doc-row">
      <td class="col-title">
        <span class="doc-title">${escapeHtml(doc.title || '(untitled)')}</span>
      </td>
      <td class="col-path">
        <span class="doc-path" title="${escapeAttr(doc.canonicalPath)}">${escapeHtml(doc.canonicalPath)}</span>
      </td>
      <td class="col-type">${docType}</td>
      <td class="col-updated">${updated}</td>
      <td class="col-indexed">${indexed}</td>
    </tr>`;
  }).join('');
}

// ---------------------------------------------------------------------------
// Client-side filter
// ---------------------------------------------------------------------------

function onFilter() {
  const q = filterInput.value.trim().toLowerCase();
  if (!q) {
    renderDocuments(allDocuments);
    filteredNote.classList.add('hidden');
    documentCount.textContent = `${allDocuments.length} document${allDocuments.length === 1 ? '' : 's'}`;
    return;
  }

  const filtered = allDocuments.filter(doc =>
    (doc.title || '').toLowerCase().includes(q) ||
    (doc.canonicalPath || '').toLowerCase().includes(q)
  );

  renderDocuments(filtered);
  documentCount.textContent = `${filtered.length} of ${allDocuments.length} documents`;

  if (filtered.length === 0) {
    filteredNote.textContent = 'No documents match this filter.';
    filteredNote.classList.remove('hidden');
  } else {
    filteredNote.classList.add('hidden');
  }
}

// ---------------------------------------------------------------------------
// UI helpers
// ---------------------------------------------------------------------------

function showLoading() {
  documentsSection.classList.add('hidden');
  emptyState.classList.add('hidden');
  emptyMessage.textContent = 'Loading…';
  emptyState.classList.remove('hidden');
}

function showDocumentsSection() {
  emptyState.classList.add('hidden');
  documentsSection.classList.remove('hidden');
  filterInput.value = '';
  filteredNote.classList.add('hidden');
}

function showEmptyState(message) {
  documentsSection.classList.add('hidden');
  emptyMessage.textContent = message;
  emptyState.classList.remove('hidden');
}

function showBanner(type, message) {
  statusBanner.className = `status-banner status-${type}`;
  statusBanner.textContent = message;
  statusBanner.classList.remove('hidden');
}

function hideBanner() {
  statusBanner.classList.add('hidden');
}

// ---------------------------------------------------------------------------
// Utilities
// ---------------------------------------------------------------------------

function escapeHtml(str) {
  return String(str ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function escapeAttr(str) {
  return String(str ?? '').replace(/"/g, '&quot;');
}

function formatDate(isoString) {
  if (!isoString) return '—';
  try {
    return new Date(isoString).toLocaleDateString(undefined, {
      year: 'numeric', month: 'short', day: 'numeric'
    });
  } catch {
    return isoString;
  }
}

// ---------------------------------------------------------------------------
// Boot
// ---------------------------------------------------------------------------

init();
