import { useCallback, useEffect, useMemo, useState } from 'react';

import {
  buildDocumentExportFileName,
  buildDocumentPayload,
  buildDraftFromDocument,
  buildEmptyDocumentDraft,
  buildMarkdownExport,
  hasUnsavedDocumentChanges,
  normalizeDocumentDraft,
  parseImportedMarkdown,
  type DocumentDetail,
  type DocumentDraft,
  type DocumentSummary,
} from './documentDraft';

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

type DocumentListResponse = {
  documents?: DocumentSummary[];
};

type DocumentVersionListResponse = {
  versions?: DocumentVersionSummary[];
};

type ValidSession = {
  idToken: string;
};

type ManagedDocumentWorkspaceOptions = {
  activeBrainId: string;
  enabled: boolean;
  hasSession: boolean;
  singularLabel: string;
  deleteActionLabel: string;
  deletePastTense: string;
  getValidSession: () => Promise<ValidSession>;
  portalFetch: (url: string, idToken: string, init?: RequestInit) => Promise<unknown>;
  downloadTextFile: (fileName: string, content: string, contentType: string) => void;
  formatDateTime: (value?: string | null) => string;
  listQuery?: {
    pathPrefix?: string;
    excludePathPrefix?: string;
  };
  normalizeDraftForEdit?: (draft: DocumentDraft) => DocumentDraft;
  normalizeDraftForSave?: (draft: DocumentDraft) => DocumentDraft;
};

type SelectDocumentOptions = {
  loadingMessage?: string;
  skipConfirm?: boolean;
};

const defaultEmptyDraft = buildEmptyDocumentDraft();
const defaultIdleMessage = 'Make a change to enable save.';

export function useManagedDocumentWorkspace({
  activeBrainId,
  enabled,
  hasSession,
  singularLabel,
  deleteActionLabel,
  deletePastTense,
  getValidSession,
  portalFetch,
  downloadTextFile,
  formatDateTime,
  listQuery,
  normalizeDraftForEdit,
  normalizeDraftForSave,
}: ManagedDocumentWorkspaceOptions) {
  const pathPrefix = listQuery?.pathPrefix || '';
  const excludePathPrefix = listQuery?.excludePathPrefix || '';
  const normalizeForEdit = normalizeDraftForEdit ?? normalizeDocumentDraft;
  const normalizeForSave = normalizeDraftForSave ?? normalizeDocumentDraft;
  const article = startsWithVowelSound(singularLabel) ? 'an' : 'a';
  const createStartMessage = `Start typing, then create the ${singularLabel}.`;

  const [documents, setDocuments] = useState<DocumentSummary[]>([]);
  const [documentsLoading, setDocumentsLoading] = useState(false);
  const [documentsError, setDocumentsError] = useState<string | null>(null);
  const [selectedDocumentId, setSelectedDocumentId] = useState<string | null>(null);
  const [selectedDocument, setSelectedDocument] = useState<DocumentDetail | null>(null);
  const [documentLoading, setDocumentLoading] = useState(false);
  const [documentError, setDocumentError] = useState<string | null>(null);
  const [refreshNonce, setRefreshNonce] = useState(0);
  const [detailNonce, setDetailNonce] = useState(0);
  const [isCreatingDocument, setIsCreatingDocument] = useState(false);
  const [documentDraft, setDocumentDraft] = useState<DocumentDraft>(defaultEmptyDraft);
  const [documentSaveState, setDocumentSaveState] = useState<DocumentSaveState>('idle');
  const [documentSaveMessage, setDocumentSaveMessage] = useState(defaultIdleMessage);
  const [documentVersions, setDocumentVersions] = useState<DocumentVersionSummary[]>([]);
  const [versionsLoading, setVersionsLoading] = useState(false);
  const [versionsError, setVersionsError] = useState<string | null>(null);
  const [versionRefreshNonce, setVersionRefreshNonce] = useState(0);
  const [selectedVersionId, setSelectedVersionId] = useState<string | null>(null);
  const [selectedVersion, setSelectedVersion] = useState<DocumentVersionDetail | null>(null);
  const [versionLoading, setVersionLoading] = useState(false);
  const [versionError, setVersionError] = useState<string | null>(null);

  const documentIsDirty = useMemo(
    () => hasUnsavedDocumentChanges(documentDraft, isCreatingDocument, selectedDocument),
    [documentDraft, isCreatingDocument, selectedDocument]
  );

  const resetWorkspace = useCallback(() => {
    setDocuments([]);
    setDocumentsError(null);
    setDocumentsLoading(false);
    setSelectedDocumentId(null);
    setSelectedDocument(null);
    setDocumentError(null);
    setDocumentLoading(false);
    setIsCreatingDocument(false);
    setDocumentDraft(defaultEmptyDraft);
    setDocumentSaveState('idle');
    setDocumentSaveMessage(defaultIdleMessage);
    setDocumentVersions([]);
    setVersionsLoading(false);
    setVersionsError(null);
    setSelectedVersionId(null);
    setSelectedVersion(null);
    setVersionLoading(false);
    setVersionError(null);
  }, []);

  useEffect(() => {
    if (!hasSession || !activeBrainId) {
      resetWorkspace();
    }
  }, [activeBrainId, hasSession, resetWorkspace]);

  useEffect(() => {
    if (!activeBrainId) {
      return;
    }

    setDocuments([]);
    setDocumentsError(null);
    setDocumentsLoading(false);
    setSelectedDocumentId(null);
    setSelectedDocument(null);
    setDocumentError(null);
    setDocumentLoading(false);
    setIsCreatingDocument(false);
    setDocumentDraft(defaultEmptyDraft);
    setDocumentSaveState('idle');
    setDocumentSaveMessage(defaultIdleMessage);
    setDocumentVersions([]);
    setVersionsLoading(false);
    setVersionsError(null);
    setSelectedVersionId(null);
    setSelectedVersion(null);
    setVersionLoading(false);
    setVersionError(null);
  }, [activeBrainId]);

  useEffect(() => {
    if (!hasSession || !activeBrainId || !enabled) {
      return;
    }

    let cancelled = false;

    async function loadDocuments() {
      setDocumentsLoading(true);
      setDocumentsError(null);

      try {
        const session = await getValidSession();
        if (cancelled) {
          return;
        }

        const response = (await portalFetch(
          buildDocumentListUrl(activeBrainId, pathPrefix, excludePathPrefix),
          session.idToken,
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
        setDocumentsError(error instanceof Error ? error.message : `Failed to load ${singularLabel}s.`);
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
  }, [activeBrainId, enabled, excludePathPrefix, getValidSession, hasSession, pathPrefix, portalFetch, refreshNonce, singularLabel]);

  useEffect(() => {
    if (!hasSession || !activeBrainId || !selectedDocumentId || !enabled) {
      if (!hasSession || !activeBrainId) {
        setSelectedDocument(null);
        setDocumentError(null);
        setDocumentLoading(false);
      }

      return;
    }

    let cancelled = false;

    async function loadDocument() {
      setDocumentLoading(true);
      setDocumentError(null);

      try {
        const session = await getValidSession();
        if (cancelled) {
          return;
        }

        const document = (await portalFetch(
          `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents/${encodeURIComponent(selectedDocumentId)}`,
          session.idToken,
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
        setDocumentError(error instanceof Error ? error.message : `Failed to load the selected ${singularLabel}.`);
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
  }, [activeBrainId, detailNonce, enabled, getValidSession, hasSession, portalFetch, selectedDocumentId, singularLabel]);

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
      setDocumentDraft(defaultEmptyDraft);
      setDocumentSaveState('idle');
      setDocumentSaveMessage(defaultIdleMessage);
      return;
    }

    setDocumentDraft(normalizeForEdit(buildDraftFromDocument(selectedDocument)));
    setDocumentSaveState('info');
    setDocumentSaveMessage('All changes saved.');
  }, [isCreatingDocument, normalizeForEdit, selectedDocument]);

  useEffect(() => {
    if (!hasSession || !activeBrainId || !selectedDocumentId || isCreatingDocument || !enabled) {
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
        const session = await getValidSession();
        if (cancelled) {
          return;
        }

        const response = (await portalFetch(
          `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents/${encodeURIComponent(selectedDocumentId)}/versions?limit=25`,
          session.idToken,
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
        setVersionsError(error instanceof Error ? error.message : `Failed to load ${singularLabel} versions.`);
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
  }, [activeBrainId, enabled, getValidSession, hasSession, isCreatingDocument, portalFetch, selectedDocumentId, singularLabel, versionRefreshNonce]);

  useEffect(() => {
    if (!hasSession || !activeBrainId || !selectedDocumentId || !selectedVersionId || isCreatingDocument || !enabled) {
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
        const session = await getValidSession();
        if (cancelled) {
          return;
        }

        const version = (await portalFetch(
          `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents/${encodeURIComponent(selectedDocumentId)}/versions/${encodeURIComponent(selectedVersionId)}`,
          session.idToken,
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
  }, [activeBrainId, enabled, getValidSession, hasSession, isCreatingDocument, portalFetch, selectedDocumentId, selectedVersionId]);

  function handleDraftChange<K extends keyof DocumentDraft>(field: K, value: DocumentDraft[K]) {
    setDocumentDraft((current) => normalizeForEdit({
      ...current,
      [field]: value,
    }));
    setDocumentSaveState('idle');
    setDocumentSaveMessage('Unsaved changes.');
  }

  function handleSelectDocument(nextDocumentId: string, options?: SelectDocumentOptions) {
    if (!nextDocumentId) {
      return false;
    }

    if (!options?.skipConfirm && !confirmDiscardDocumentChanges(documentIsDirty, `Open another ${singularLabel} and discard unsaved changes?`)) {
      return false;
    }

    setIsCreatingDocument(false);
    setSelectedDocument(null);
    setSelectedDocumentId(nextDocumentId);
    setSelectedVersionId(null);
    setSelectedVersion(null);
    setDocumentSaveState('info');
    setDocumentSaveMessage(options?.loadingMessage || `Loading ${singularLabel}...`);
    return true;
  }

  function handleCreateDocument() {
    if (!confirmDiscardDocumentChanges(documentIsDirty, `Create a new ${singularLabel} and discard unsaved changes?`)) {
      return;
    }

    setIsCreatingDocument(true);
    setSelectedDocumentId(null);
    setSelectedDocument(null);
    setDocumentDraft(defaultEmptyDraft);
    setDocumentSaveState('idle');
    setDocumentSaveMessage(createStartMessage);
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
      setDocumentSaveMessage(isCreatingDocument ? `Creating ${singularLabel}...` : `Saving ${singularLabel}...`);
      const session = await getValidSession();
      const draft = normalizeForSave(documentDraft);
      const payload = buildDocumentPayload(draft);

      if (isCreatingDocument) {
        const created = await portalFetch(
          `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents`,
          session.idToken,
          {
            method: 'POST',
            body: JSON.stringify(payload),
          },
        ) as DocumentDetail;

        setIsCreatingDocument(false);
        setSelectedDocumentId(created.managedDocumentId);
        setDocumentSaveState('info');
        setDocumentSaveMessage(`Created ${singularLabel} '${created.title || created.slug || created.managedDocumentId}'.`);
        setRefreshNonce((value) => value + 1);
        setDetailNonce((value) => value + 1);
        setVersionRefreshNonce((value) => value + 1);
        return;
      }

      if (!selectedDocument?.managedDocumentId) {
        setDocumentSaveState('warn');
        setDocumentSaveMessage(`Select ${article} ${singularLabel} or create a new one before saving.`);
        return;
      }

      const updated = await portalFetch(
        `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents/${encodeURIComponent(selectedDocument.managedDocumentId)}`,
        session.idToken,
        {
          method: 'PUT',
          body: JSON.stringify(payload),
        },
      ) as DocumentDetail;

      setDocumentSaveState('info');
      setDocumentSaveMessage(`Saved ${singularLabel} '${updated.title || updated.slug || updated.managedDocumentId}'.`);
      setRefreshNonce((value) => value + 1);
      setDetailNonce((value) => value + 1);
      setVersionRefreshNonce((value) => value + 1);
    } catch (error) {
      setDocumentSaveState('error');
      setDocumentSaveMessage(error instanceof Error ? error.message : `Failed to save the ${singularLabel}.`);
    }
  }

  function handleExportDocument() {
    try {
      const draft = normalizeForSave(documentDraft);
      if (!draft.title && !draft.content.trim()) {
        setDocumentSaveState('warn');
        setDocumentSaveMessage(`Create or select ${article} ${singularLabel} before exporting Markdown.`);
        return;
      }

      const markdown = buildMarkdownExport(draft, selectedDocument);
      const fileName = buildDocumentExportFileName(draft, selectedDocument);
      downloadTextFile(fileName, markdown, 'text/markdown;charset=utf-8');
      setDocumentSaveState('info');
      setDocumentSaveMessage(`Exported '${fileName}'.`);
    } catch (error) {
      setDocumentSaveState('error');
      setDocumentSaveMessage(error instanceof Error ? error.message : `Failed to export the ${singularLabel}.`);
    }
  }

  async function handleDeleteDocument() {
    if (!selectedDocument?.managedDocumentId || isCreatingDocument) {
      setDocumentSaveState('warn');
      setDocumentSaveMessage(`Select an existing ${singularLabel} before deleting.`);
      return;
    }

    const label = selectedDocument.title || selectedDocument.canonicalPath || selectedDocument.managedDocumentId;
    if (!window.confirm(`${deleteActionLabel} ${singularLabel} '${label}'?`)) {
      return;
    }

    try {
      const session = await getValidSession();
      await portalFetch(
        `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents/${encodeURIComponent(selectedDocument.managedDocumentId)}`,
        session.idToken,
        {
          method: 'DELETE',
        },
      );

      setSelectedDocumentId(null);
      setSelectedDocument(null);
      setDocumentDraft(defaultEmptyDraft);
      setDocumentSaveState('info');
      setDocumentSaveMessage(`${capitalizeLabel(singularLabel)} ${deletePastTense}.`);
      setRefreshNonce((value) => value + 1);
      setDocumentVersions([]);
      setSelectedVersionId(null);
      setSelectedVersion(null);
    } catch (error) {
      setDocumentSaveState('error');
      setDocumentSaveMessage(error instanceof Error ? error.message : `Failed to ${deleteActionLabel.toLowerCase()} the selected ${singularLabel}.`);
    }
  }

  function handleRevertDocument() {
    if (isCreatingDocument) {
      setDocumentDraft(defaultEmptyDraft);
      setDocumentSaveState('idle');
      setDocumentSaveMessage(createStartMessage);
      return;
    }

    if (selectedDocument) {
      setDocumentDraft(normalizeForEdit(buildDraftFromDocument(selectedDocument)));
      setDocumentSaveState('info');
      setDocumentSaveMessage('Changes reverted to the last saved version.');
      return;
    }

    setDocumentDraft(defaultEmptyDraft);
    setDocumentSaveState('idle');
    setDocumentSaveMessage(defaultIdleMessage);
  }

  async function handleImportDocument(file: File | null) {
    if (!file) {
      return;
    }

    if (!confirmDiscardDocumentChanges(documentIsDirty, `Import Markdown and discard unsaved ${singularLabel} changes?`)) {
      return;
    }

    try {
      const imported = normalizeForEdit(parseImportedMarkdown(await file.text(), file.name));
      setIsCreatingDocument(true);
      setSelectedDocumentId(null);
      setSelectedDocument(null);
      setDocumentDraft(imported);
      setDocumentSaveState('info');
      setDocumentSaveMessage(`Imported ${singularLabel} draft ready.`);
      setDocumentVersions([]);
      setSelectedVersionId(null);
      setSelectedVersion(null);
    } catch (error) {
      setDocumentSaveState('error');
      setDocumentSaveMessage(error instanceof Error ? error.message : 'Failed to import Markdown.');
    }
  }

  function handleRefreshDocuments() {
    setRefreshNonce((value) => value + 1);
  }

  function handleRefreshVersions() {
    if (!selectedDocument?.managedDocumentId || isCreatingDocument) {
      setVersionsError(`Select a saved ${singularLabel} before loading version history.`);
      return;
    }

    setVersionRefreshNonce((value) => value + 1);
  }

  function handleSelectVersion(nextVersionId: string) {
    setSelectedVersionId((current) => current === nextVersionId ? null : nextVersionId);
  }

  async function handleRestoreVersion() {
    if (!selectedDocument?.managedDocumentId || !selectedVersion?.managedDocumentVersionId) {
      setVersionError(`Select a ${singularLabel} version before restoring.`);
      return;
    }

    const restoreTimestamp = formatDateTime(selectedVersion.createdAt);
    if (!window.confirm(`Restore the selected ${singularLabel} version from ${restoreTimestamp}? Current draft changes will be replaced.`)) {
      return;
    }

    try {
      const session = await getValidSession();
      await portalFetch(
        `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents/${encodeURIComponent(selectedDocument.managedDocumentId)}/versions/${encodeURIComponent(selectedVersion.managedDocumentVersionId)}/restore`,
        session.idToken,
        {
          method: 'POST',
        },
      );

      setDocumentSaveState('info');
      setDocumentSaveMessage(`Restored version from ${restoreTimestamp}.`);
      setDetailNonce((value) => value + 1);
      setVersionRefreshNonce((value) => value + 1);
    } catch (error) {
      setVersionError(error instanceof Error ? error.message : 'Failed to restore the selected version.');
    }
  }

  function showDocumentStatus(nextState: DocumentSaveState, message: string) {
    setDocumentSaveState(nextState);
    setDocumentSaveMessage(message);
  }

  return {
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
    handleSelectDocument,
    handleSelectVersion,
    showDocumentStatus,
  };
}

function buildDocumentListUrl(activeBrainId: string, pathPrefix: string, excludePathPrefix: string) {
  const query = new URLSearchParams({ limit: '200' });

  if (pathPrefix) {
    query.set('pathPrefix', pathPrefix);
  }

  if (excludePathPrefix) {
    query.set('excludePathPrefix', excludePathPrefix);
  }

  return `/portal-api/tenant/brains/${encodeURIComponent(activeBrainId)}/documents?${query.toString()}`;
}

function confirmDiscardDocumentChanges(isDirty: boolean, message: string) {
  return !isDirty || window.confirm(message);
}

function startsWithVowelSound(value: string) {
  return /^[aeiou]/i.test(value);
}

function capitalizeLabel(value: string) {
  return value.charAt(0).toUpperCase() + value.slice(1);
}







