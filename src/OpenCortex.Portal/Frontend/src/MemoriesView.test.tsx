import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';

import { MemoriesView, type BrainSummary } from './MemoriesView';
import { buildEmptyDocumentDraft, type DocumentDetail } from './documentDraft';

afterEach(() => {
  cleanup();
});

const brain: BrainSummary = {
  brainId: 'brain-1',
  name: 'Daily Brain',
  mode: 'managed-content',
  status: 'active',
};

const memories: DocumentDetail[] = [
  {
    managedDocumentId: 'mem-1',
    title: 'OQL path prefix decision',
    slug: 'memories/decision/day-one-oql',
    canonicalPath: 'memories/decision/day-one-oql.md',
    status: 'published',
    updatedAt: '2026-03-19T12:00:00Z',
    content: '# OQL path prefix decision\n\nUse OQL path prefix on day one.',
    frontmatter: {
      category: 'decision',
    },
  },
];

describe('MemoriesView', () => {
  it('renders memory-specific labels on the shared document editor surface', () => {
    render(
      <MemoriesView
        activeBrain={brain}
        activeBrainId={brain.brainId}
        brains={[brain]}
        documentDraft={buildEmptyDocumentDraft()}
        documentError={null}
        documentFilter=""
        documentGroups={[
          {
            directoryPath: 'memories/decision',
            depth: 2,
            label: 'decision',
            documents: memories,
          },
        ]}
        documentIsDirty={false}
        documentLoading={false}
        documentSaveMessage="Ready."
        documentSaveState="info"
        documentVersions={[]}
        documents={memories}
        documentsError={null}
        documentsLoading={false}
        filteredDocuments={memories}
        isCreatingDocument={false}
        onChangeBrain={() => {}}
        onChangeDocumentFilter={() => {}}
        onCreateDocument={() => {}}
        onDeleteDocument={() => {}}
        onDraftChange={() => {}}
        onExportDocument={() => {}}
        onImportDocument={() => {}}
        onRefreshDocuments={() => {}}
        onRefreshVersions={() => {}}
        onRestoreVersion={() => {}}
        onRevertDocument={() => {}}
        onSaveDocument={() => {}}
        onSelectDocument={() => {}}
        onSelectVersion={() => {}}
        selectedDocument={memories[0]}
        selectedDocumentId="mem-1"
        selectedVersion={null}
        selectedVersionId={null}
        versionError={null}
        versionLoading={false}
        versionsError={null}
        versionsLoading={false}
      />
    );

    expect(screen.getByText('Memories')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'New Memory' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Save Memory' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Forget Memory' })).toBeTruthy();
    expect(screen.getByPlaceholderText('Filter by category, title, slug, or path')).toBeTruthy();
  });

  it('dispatches shared editor actions through the memory wrapper', () => {
    const onSelectDocument = vi.fn();
    const onRefreshDocuments = vi.fn();

    render(
      <MemoriesView
        activeBrain={brain}
        activeBrainId={brain.brainId}
        brains={[brain]}
        documentDraft={buildEmptyDocumentDraft()}
        documentError={null}
        documentFilter=""
        documentGroups={[
          {
            directoryPath: 'memories/decision',
            depth: 2,
            label: 'decision',
            documents: memories,
          },
        ]}
        documentIsDirty={false}
        documentLoading={false}
        documentSaveMessage="Ready."
        documentSaveState="info"
        documentVersions={[]}
        documents={memories}
        documentsError={null}
        documentsLoading={false}
        filteredDocuments={memories}
        isCreatingDocument={false}
        onChangeBrain={() => {}}
        onChangeDocumentFilter={() => {}}
        onCreateDocument={() => {}}
        onDeleteDocument={() => {}}
        onDraftChange={() => {}}
        onExportDocument={() => {}}
        onImportDocument={() => {}}
        onRefreshDocuments={onRefreshDocuments}
        onRefreshVersions={() => {}}
        onRestoreVersion={() => {}}
        onRevertDocument={() => {}}
        onSaveDocument={() => {}}
        onSelectDocument={onSelectDocument}
        onSelectVersion={() => {}}
        selectedDocument={memories[0]}
        selectedDocumentId="mem-1"
        selectedVersion={null}
        selectedVersionId={null}
        versionError={null}
        versionLoading={false}
        versionsError={null}
        versionsLoading={false}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Refresh Memories' }));
    fireEvent.click(screen.getByRole('button', { name: /day-one-oql/i }));

    expect(onRefreshDocuments).toHaveBeenCalled();
    expect(onSelectDocument).toHaveBeenCalledWith('mem-1');
  });
});
