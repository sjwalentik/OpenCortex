import { useMemo, useRef, type MouseEvent as ReactMouseEvent } from 'react';
import type { Editor as TiptapEditor } from '@tiptap/react';

import { MarkdownTiptapEditor } from './MarkdownTiptapEditor';
import { isExternalLinkHref, normalizeDocumentLinkPath } from './documentLinks';
import type { DocumentDetail, DocumentDraft, DocumentSummary } from './documentDraft';

export type BrainSummary = {
  brainId: string;
  name: string;
  mode: string;
  status: string;
};

export type DocumentSaveState = 'idle' | 'saving' | 'error' | 'warn' | 'info';

export type DocumentVersionSummary = {
  managedDocumentVersionId: string;
  createdAt?: string;
  snapshotKind?: string;
  status?: string;
  wordCount?: number;
  snapshotBy?: string;
};

export type DocumentVersionDetail = DocumentVersionSummary & {
  content?: string;
};

export type DocumentGroup = {
  directoryPath: string;
  depth: number;
  label: string;
  documents: DocumentSummary[];
};

export type DocumentsViewProps = {
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
  onDocumentEditorReady?: (editor: TiptapEditor | null) => void;
  onDraftChange: <K extends keyof DocumentDraft>(field: K, value: DocumentDraft[K]) => void;
  onExportDocument: () => void;
  onImportDocument: (file: File | null) => void;
  onOpenDocumentLink?: (canonicalPath: string) => void;
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

export function DocumentsView({
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
  onDocumentEditorReady,
  onDraftChange,
  onExportDocument,
  onImportDocument,
  onOpenDocumentLink,
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
  versionsLoading,
}: DocumentsViewProps) {
  const importInputRef = useRef<HTMLInputElement | null>(null);
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

  function handleRenderedMarkdownClick(event: ReactMouseEvent<HTMLElement>) {
    const target = event.target;
    if (!(target instanceof HTMLElement)) {
      return;
    }

    const link = target.closest('a[data-document-path]');
    if (!(link instanceof HTMLAnchorElement)) {
      return;
    }

    const canonicalPath = normalizeDocumentLinkPath(link.dataset.documentPath || link.getAttribute('href') || '');
    if (!canonicalPath) {
      return;
    }

    event.preventDefault();
    onOpenDocumentLink?.(canonicalPath);
  }

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

            <div className="field">
              <span>Markdown Content</span>
              <MarkdownTiptapEditor
                value={documentDraft.content}
                placeholder="Write Markdown content here."
                disabled={!activeBrainId}
                onEditorReady={onDocumentEditorReady}
                onOpenDocumentLink={onOpenDocumentLink}
                onChange={(value) => onDraftChange('content', value)}
              />
            </div>

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
                            <div className="markdown-preview version-preview" onClick={handleRenderedMarkdownClick} dangerouslySetInnerHTML={showInlinePreview ? renderedVersionMarkup : { __html: '<p>Loading selected version...</p>' }} />
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

function renderMarkdown(markdown: string) {
  const source = String(markdown || '').replace(/\r\n/g, '\n').trim();
  if (!source) {
    return '<p>Nothing to preview yet.</p>';
  }

  const lines = source.split('\n');
  const blocks: string[] = [];
  let paragraph: string[] = [];
  let listItems: string[] = [];
  let orderedListItems: string[] = [];
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

  const flushOrderedList = () => {
    if (orderedListItems.length === 0) {
      return;
    }

    blocks.push(`<ol>${orderedListItems.map((item) => `<li>${renderInlineMarkdown(item)}</li>`).join('')}</ol>`);
    orderedListItems = [];
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
      flushOrderedList();
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
      flushOrderedList();
      continue;
    }

    const headingMatch = trimmed.match(/^(#{1,3})\s+(.*)$/);
    if (headingMatch) {
      flushParagraph();
      flushList();
      flushOrderedList();
      const level = headingMatch[1].length;
      blocks.push(`<h${level}>${renderInlineMarkdown(headingMatch[2])}</h${level}>`);
      continue;
    }

    if (trimmed.startsWith('- ') || trimmed.startsWith('* ')) {
      flushParagraph();
      flushOrderedList();
      listItems.push(trimmed.slice(2).trim());
      continue;
    }

    const orderedListMatch = trimmed.match(/^\d+\.\s+(.*)$/);
    if (orderedListMatch) {
      flushParagraph();
      flushList();
      orderedListItems.push(orderedListMatch[1].trim());
      continue;
    }

    if (trimmed.startsWith('> ')) {
      flushParagraph();
      flushList();
      flushOrderedList();
      blocks.push(`<blockquote><p>${renderInlineMarkdown(trimmed.slice(2).trim())}</p></blockquote>`);
      continue;
    }

    paragraph.push(trimmed);
  }

  flushParagraph();
  flushList();
  flushOrderedList();
  flushCodeFence();
  return blocks.join('');
}

function renderInlineMarkdown(value: string) {
  return escapeHtml(value)
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/\*([^*]+)\*/g, '<em>$1</em>')
    .replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (_match, label, href) => renderMarkdownLink(label, href));
}

function renderMarkdownLink(label: string, href: string) {
  const documentPath = normalizeDocumentLinkPath(href);
  if (documentPath) {
    const escapedPath = escapeHtml(documentPath);
    return `<a href="#${escapedPath}" data-document-path="${escapedPath}">${label}</a>`;
  }

  if (isExternalLinkHref(href)) {
    return `<a href="${href}" target="_blank" rel="noreferrer">${label}</a>`;
  }

  return label;
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
    minute: '2-digit',
  });
}