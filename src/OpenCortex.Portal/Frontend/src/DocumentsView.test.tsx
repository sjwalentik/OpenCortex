import { act, useMemo, useState } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { Editor as TiptapEditor } from '@tiptap/react';

import { DocumentsView, type BrainSummary, type DocumentGroup } from './DocumentsView';
import { buildDraftFromDocument, buildEmptyDocumentDraft, hasUnsavedDocumentChanges, type DocumentDetail, type DocumentDraft } from './documentDraft';
import { convertMarkdownToEditorHtml } from './documentMarkdown';

afterEach(() => {
  cleanup();
});

if (!HTMLElement.prototype.scrollIntoView) {
  HTMLElement.prototype.scrollIntoView = () => {};
}

const brain: BrainSummary = {
  brainId: 'brain-1',
  name: 'Daily Brain',
  mode: 'managed-content',
  status: 'active',
};

function buildGroups(documents: DocumentDetail[]): DocumentGroup[] {
  return [
    {
      directoryPath: '',
      depth: 0,
      label: 'Root',
      documents,
    },
  ];
}

function DocumentsViewHarness({ onEditorReady }: { onEditorReady?: (editor: TiptapEditor | null) => void }) {
  const [documents, setDocuments] = useState<DocumentDetail[]>([
    {
      managedDocumentId: 'doc-existing',
      title: 'Existing Doc',
      slug: 'daily/existing-doc',
      canonicalPath: 'daily/existing-doc.md',
      status: 'published',
      updatedAt: '2026-03-13T12:00:00Z',
      content: '# Existing Doc\n\nOriginal body',
      frontmatter: { type: 'daily' },
    },
  ]);
  const [selectedDocumentId, setSelectedDocumentId] = useState<string | null>('doc-existing');
  const [selectedDocument, setSelectedDocument] = useState<DocumentDetail | null>(documents[0]);
  const [documentDraft, setDocumentDraft] = useState<DocumentDraft>(buildDraftFromDocument(documents[0]));
  const [isCreatingDocument, setIsCreatingDocument] = useState(false);
  const [documentSaveMessage, setDocumentSaveMessage] = useState('Ready.');
  const [documentFilter, setDocumentFilter] = useState('');

  const filteredDocuments = useMemo(() => documents.filter((document) => {
    const haystack = `${document.title || ''} ${document.slug || ''} ${document.canonicalPath || ''}`.toLowerCase();
    return haystack.includes(documentFilter.toLowerCase());
  }), [documentFilter, documents]);

  const documentGroups = useMemo(() => buildGroups(filteredDocuments), [filteredDocuments]);
  const documentIsDirty = hasUnsavedDocumentChanges(documentDraft, isCreatingDocument, selectedDocument);

  function handleDraftChange<K extends keyof DocumentDraft>(field: K, value: DocumentDraft[K]) {
    setDocumentDraft((current) => ({
      ...current,
      [field]: value,
    }));
    setDocumentSaveMessage('Unsaved changes.');
  }

  function handleCreateDocument() {
    setIsCreatingDocument(true);
    setSelectedDocumentId(null);
    setSelectedDocument(null);
    setDocumentDraft(buildEmptyDocumentDraft());
    setDocumentSaveMessage('Creating a new document.');
  }

  function handleSaveDocument() {
    if (isCreatingDocument) {
      const created: DocumentDetail = {
        managedDocumentId: `doc-${documentDraft.slug || documentDraft.title.toLowerCase().replace(/\s+/g, '-')}`,
        title: documentDraft.title,
        slug: documentDraft.slug,
        canonicalPath: `${documentDraft.slug}.md`,
        status: documentDraft.status,
        updatedAt: '2026-03-13T13:00:00Z',
        content: documentDraft.content,
        frontmatter: documentDraft.frontmatterText
          ? Object.fromEntries(documentDraft.frontmatterText.split('\n').map((line) => {
              const [key, ...rest] = line.split(':');
              return [key.trim(), rest.join(':').trim()];
            }))
          : {},
      };

      setDocuments((current) => [...current, created]);
      setSelectedDocumentId(created.managedDocumentId);
      setSelectedDocument(created);
      setDocumentDraft(buildDraftFromDocument(created));
      setIsCreatingDocument(false);
      setDocumentSaveMessage(`Saved document '${created.title}'.`);
      return;
    }

    if (!selectedDocument) {
      return;
    }

    const updated: DocumentDetail = {
      ...selectedDocument,
      title: documentDraft.title,
      slug: documentDraft.slug,
      canonicalPath: `${documentDraft.slug}.md`,
      status: documentDraft.status,
      content: documentDraft.content,
    };

    setDocuments((current) => current.map((document) => document.managedDocumentId === updated.managedDocumentId ? updated : document));
    setSelectedDocument(updated);
    setDocumentDraft(buildDraftFromDocument(updated));
    setDocumentSaveMessage(`Saved document '${updated.title}'.`);
  }

  function handleSelectDocument(documentId: string) {
    const next = documents.find((document) => document.managedDocumentId === documentId) || null;
    setSelectedDocumentId(documentId);
    setSelectedDocument(next);
    setDocumentDraft(next ? buildDraftFromDocument(next) : buildEmptyDocumentDraft());
    setIsCreatingDocument(false);
    setDocumentSaveMessage(next ? `Loaded '${next.title}'.` : 'Ready.');
  }

  function handleRevertDocument() {
    setDocumentDraft(selectedDocument ? buildDraftFromDocument(selectedDocument) : buildEmptyDocumentDraft());
    setDocumentSaveMessage('Changes reverted.');
  }

  return (
    <DocumentsView
      activeBrain={brain}
      activeBrainId={brain.brainId}
      brains={[brain]}
      documentDraft={documentDraft}
      documentError={null}
      documentFilter={documentFilter}
      documentGroups={documentGroups}
      documentIsDirty={documentIsDirty}
      documentLoading={false}
      documentSaveMessage={documentSaveMessage}
      documentSaveState="info"
      documentVersions={[]}
      documents={documents}
      documentsError={null}
      documentsLoading={false}
      filteredDocuments={filteredDocuments}
      isCreatingDocument={isCreatingDocument}
      onChangeBrain={() => {}}
      onChangeDocumentFilter={setDocumentFilter}
      onCreateDocument={handleCreateDocument}
      onDeleteDocument={() => {}}
      onDocumentEditorReady={onEditorReady}
      onDraftChange={handleDraftChange}
      onExportDocument={() => {}}
      onImportDocument={() => {}}
      onRefreshDocuments={() => {}}
      onRefreshVersions={() => {}}
      onRestoreVersion={() => {}}
      onRevertDocument={handleRevertDocument}
      onSaveDocument={handleSaveDocument}
      onSelectDocument={handleSelectDocument}
      onSelectVersion={() => {}}
      selectedDocument={selectedDocument}
      selectedDocumentId={selectedDocumentId}
      selectedVersion={null}
      selectedVersionId={null}
      versionError={null}
      versionLoading={false}
      versionsError={null}
      versionsLoading={false}
    />
  );
}

describe('DocumentsView', () => {
  it('supports create, save, and reload across documents', async () => {
    let editorInstance: TiptapEditor | null = null;
    const { container } = render(
      <DocumentsViewHarness onEditorReady={(editor) => {
        if (editor) {
          editorInstance = editor;
        }
      }} />
    );

    await waitFor(() => {
      expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Existing Doc');
    });

    fireEvent.click(screen.getByRole('button', { name: 'New Document' }));
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Second Doc' } });
    fireEvent.change(screen.getByLabelText('Filename / Path'), { target: { value: 'daily/second-doc' } });

    act(() => {
      editorInstance?.commands.setContent(convertMarkdownToEditorHtml('# Second Doc\n\nCreated body'));
    });

    fireEvent.click(screen.getByRole('button', { name: 'Save Document' }));

    await waitFor(() => {
      expect(screen.getByText("Saved document 'Second Doc'.")).toBeTruthy();
      expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Second Doc');
    });

    const listButtons = Array.from(container.querySelectorAll('.document-list-item')) as HTMLButtonElement[];
    const existingButton = listButtons.find((button) => button.textContent?.includes('existing-doc'));
    const secondButton = listButtons.find((button) => button.textContent?.includes('second-doc'));

    expect(existingButton).toBeTruthy();
    expect(secondButton).toBeTruthy();

    fireEvent.click(existingButton!);
    await waitFor(() => {
      expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Existing Doc');
    });

    fireEvent.click(secondButton!);
    await waitFor(() => {
      expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Second Doc');
      const editor = container.querySelector('.tiptap-editor');
      expect(editor?.textContent).toContain('Created body');
    });

    expect(screen.queryByText('Rendered Document')).toBeNull();
    expect(container.querySelector('.document-editor-shell')?.closest('label')).toBeNull();
  });
});