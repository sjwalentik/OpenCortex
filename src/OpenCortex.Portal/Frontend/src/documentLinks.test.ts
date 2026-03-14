import { describe, expect, it } from 'vitest';

import { isExternalLinkHref, normalizeDocumentLinkPath, normalizeEditorLinkHref } from './documentLinks';

describe('documentLinks', () => {
  it('normalizes internal canonical paths for document links', () => {
    expect(normalizeDocumentLinkPath('daily/notes')).toBe('daily/notes.md');
    expect(normalizeDocumentLinkPath('/daily/notes.md')).toBe('daily/notes.md');
    expect(normalizeDocumentLinkPath('ocdoc:projects/opencortex/roadmap')).toBe('projects/opencortex/roadmap.md');
  });

  it('rejects external urls as internal document paths', () => {
    expect(normalizeDocumentLinkPath('https://example.com')).toBeNull();
    expect(normalizeDocumentLinkPath('mailto:test@example.com')).toBeNull();
  });

  it('normalizes editor link input for document paths and external urls', () => {
    expect(normalizeEditorLinkHref('daily/notes')).toBe('daily/notes.md');
    expect(normalizeEditorLinkHref('example.com/docs')).toBe('https://example.com/docs');
    expect(normalizeEditorLinkHref('https://example.com/docs')).toBe('https://example.com/docs');
  });

  it('detects external link schemes', () => {
    expect(isExternalLinkHref('https://example.com')).toBe(true);
    expect(isExternalLinkHref('mailto:test@example.com')).toBe(true);
    expect(isExternalLinkHref('daily/notes.md')).toBe(false);
  });
});

