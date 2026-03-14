import { describe, expect, it } from 'vitest';

import {
  buildDocumentExportFileName,
  buildDocumentPayload,
  buildDraftFromDocument,
  buildEmptyDocumentDraft,
  buildMarkdownExport,
  hasUnsavedDocumentChanges,
  normalizeDocumentDraft,
  parseImportedMarkdown,
} from './documentDraft';

describe('documentDraft flow helpers', () => {
  it('treats a fresh create flow as clean until the draft changes', () => {
    const draft = buildEmptyDocumentDraft();

    expect(hasUnsavedDocumentChanges(draft, true, null)).toBe(false);

    const editedDraft = {
      ...draft,
      title: 'March 13 2026',
      content: '# March 13 2026\n\nStart the richer editor pass.',
    };

    expect(hasUnsavedDocumentChanges(editedDraft, true, null)).toBe(true);
  });

  it('supports reload-style revert back to the saved document state', () => {
    const selectedDocument = {
      managedDocumentId: 'doc-1',
      title: 'Daily Log',
      slug: 'daily/daily-log',
      status: 'published',
      content: '# Daily Log\n\nSaved body',
      frontmatter: {
        type: 'daily',
        owner: 'steph',
      },
    };

    const savedDraft = buildDraftFromDocument(selectedDocument);
    const editedDraft = {
      ...savedDraft,
      content: '# Daily Log\n\nChanged body',
    };

    expect(hasUnsavedDocumentChanges(editedDraft, false, selectedDocument)).toBe(true);
    expect(hasUnsavedDocumentChanges(buildDraftFromDocument(selectedDocument), false, selectedDocument)).toBe(false);
    expect(normalizeDocumentDraft(savedDraft)).toEqual({
      title: 'Daily Log',
      slug: 'daily/daily-log',
      status: 'published',
      frontmatterText: 'type: daily\nowner: steph',
      content: '# Daily Log\n\nSaved body',
    });
  });

  it('imports markdown, builds a payload, and exports normalized markdown again', () => {
    const imported = parseImportedMarkdown([
      '---',
      'title: Adventure Plan',
      'slug: daily/3-13-2026',
      'category: planning',
      '---',
      '',
      '# Adventure Plan',
      '',
      '- inspect editor state',
      '- wire save path',
    ].join('\n'), 'adventure-plan.md');

    expect(imported).toEqual({
      title: 'Adventure Plan',
      slug: 'daily/3-13-2026',
      status: 'draft',
      frontmatterText: 'title: Adventure Plan\nslug: daily/3-13-2026\ncategory: planning',
      content: '# Adventure Plan\n\n- inspect editor state\n- wire save path',
    });

    expect(buildDocumentPayload(imported)).toEqual({
      title: 'Adventure Plan',
      slug: 'daily/3-13-2026',
      status: 'draft',
      content: '# Adventure Plan\n\n- inspect editor state\n- wire save path',
      frontmatter: {
        title: 'Adventure Plan',
        slug: 'daily/3-13-2026',
        category: 'planning',
      },
    });

    expect(buildMarkdownExport(imported, null)).toBe([
      '---',
      'title: Adventure Plan',
      'slug: daily/3-13-2026',
      'category: planning',
      '---',
      '',
      '# Adventure Plan',
      '',
      '- inspect editor state',
      '- wire save path',
      '',
    ].join('\n'));

    expect(buildDocumentExportFileName(imported, null)).toBe('daily/3-13-2026.md');
  });
});