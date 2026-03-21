import { DocumentsView, type DocumentsViewProps } from './DocumentsView';

export type BrainSummary = DocumentsViewProps['brains'][number];
export type MemoriesViewProps = Omit<DocumentsViewProps, 'copy'>;

const memoryCopy = {
  eyebrow: 'Memories',
  heroTitle: 'Agent memory is just managed-content authoring under the reserved memories namespace.',
  heroSummary: 'Memory records stay as editable Markdown documents under memories/, with the same save path, import/export flow, and version history as any other managed document.',
  filterPlaceholder: 'Filter by category, title, slug, or path',
  createButtonLabel: 'New Memory',
  importButtonLabel: 'Import Memory Markdown',
  refreshButtonLabel: 'Refresh Memories',
  listTitle: 'Memory List',
  railAriaLabel: 'Managed memory list',
  noItemsMessage: 'No memories exist in this brain yet.',
  noMatchesMessage: 'No memories match the current filter.',
  detailFallbackTitle: 'Memory Editor',
  saveButtonLabel: 'Save Memory',
  exportButtonLabel: 'Export Memory Markdown',
  deleteButtonLabel: 'Forget Memory',
  loadingRailMessage: 'Refreshing memory rail...',
  loadingDetailMessage: 'Loading selected memory...',
  createMetaClean: 'Ready to create a new memory document under memories/.',
  createMetaDirty: 'Unsaved memory draft for the active managed-content brain.',
  emptySelectionMessage: 'Select a memory to inspect or edit its content.',
} as const;

export function MemoriesView(props: MemoriesViewProps) {
  return (
    <DocumentsView
      {...props}
      availableDocumentLinks={props.availableDocumentLinks ?? props.documents}
      copy={memoryCopy}
    />
  );
}
