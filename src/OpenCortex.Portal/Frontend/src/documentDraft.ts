export type DocumentSummary = {
  managedDocumentId: string;
  title?: string;
  slug?: string;
  canonicalPath?: string;
  status?: string;
  updatedAt?: string;
  wordCount?: number;
};

export type DocumentDetail = DocumentSummary & {
  content?: string;
  frontmatter?: Record<string, unknown> | null;
};

export type DocumentDraft = {
  title: string;
  slug: string;
  status: string;
  frontmatterText: string;
  content: string;
};

export function buildEmptyDocumentDraft(): DocumentDraft {
  return {
    title: '',
    slug: '',
    status: 'draft',
    frontmatterText: '',
    content: '',
  };
}

export function buildDraftFromDocument(document: DocumentDetail): DocumentDraft {
  return {
    title: document.title || '',
    slug: document.slug || '',
    status: document.status || 'draft',
    frontmatterText: serializeFrontmatter(document.frontmatter || {}),
    content: document.content || '',
  };
}

export function normalizeDocumentDraft(draft: DocumentDraft): DocumentDraft {
  return {
    title: String(draft.title || '').trim(),
    slug: String(draft.slug || '').trim(),
    status: String(draft.status || 'draft').trim() || 'draft',
    frontmatterText: String(draft.frontmatterText || '').replace(/\r\n/g, '\n').trim(),
    content: String(draft.content || '').replace(/\r\n/g, '\n'),
  };
}

export function hasUnsavedDocumentChanges(draft: DocumentDraft, isCreatingDocument: boolean, selectedDocument: DocumentDetail | null) {
  const currentDraft = normalizeDocumentDraft(draft);
  if (isCreatingDocument) {
    return JSON.stringify(currentDraft) !== JSON.stringify(normalizeDocumentDraft(buildEmptyDocumentDraft()));
  }

  if (!selectedDocument) {
    return false;
  }

  return JSON.stringify(currentDraft) !== JSON.stringify(normalizeDocumentDraft(buildDraftFromDocument(selectedDocument)));
}

export function buildDocumentPayload(draft: DocumentDraft) {
  if (!draft.title) {
    throw new Error('Title is required.');
  }

  return {
    title: draft.title,
    slug: draft.slug || null,
    status: draft.status || 'draft',
    content: draft.content || '',
    frontmatter: parseFrontmatterText(draft.frontmatterText),
  };
}

export function parseImportedMarkdown(markdown: string, fileName: string): DocumentDraft {
  const normalized = String(markdown || '').replace(/\r\n/g, '\n');
  let frontmatterText = '';
  let content = normalized;

  if (normalized.startsWith('---\n')) {
    const endIndex = normalized.indexOf('\n---\n', 4);
    const alternateEndIndex = normalized.indexOf('\n...\n', 4);
    const closingIndex = endIndex >= 0 ? endIndex : alternateEndIndex;

    if (closingIndex < 0) {
      throw new Error('Imported Markdown frontmatter is missing a closing --- or ... line.');
    }

    frontmatterText = normalized.slice(4, closingIndex).trim();
    content = normalized.slice(closingIndex + 5);
  }

  const parsedFrontmatter = parseFrontmatterText(frontmatterText);
  const title = deriveImportedDocumentTitle(content, parsedFrontmatter, fileName);
  const slug = deriveImportedDocumentSlug(parsedFrontmatter, title, fileName);

  return {
    title,
    slug,
    status: 'draft',
    frontmatterText: serializeFrontmatter(parsedFrontmatter),
    content: content.replace(/^\n+/, ''),
  };
}

function deriveImportedDocumentTitle(content: string, frontmatter: Record<string, string>, fileName: string) {
  const frontmatterTitle = String(frontmatter.title || '').trim();
  if (frontmatterTitle) {
    return frontmatterTitle;
  }

  const headingMatch = String(content || '').match(/^\s*#\s+(.+)$/m);
  if (headingMatch?.[1]) {
    return headingMatch[1].trim();
  }

  const fileStem = String(fileName || '').replace(/\.[^.]+$/, '').trim();
  if (fileStem) {
    return fileStem;
  }

  return 'Imported document';
}

function deriveImportedDocumentSlug(frontmatter: Record<string, string>, title: string, fileName: string) {
  const explicitSlug = String(frontmatter.slug || '').trim();
  if (explicitSlug) {
    return normalizeDocumentPath(explicitSlug);
  }

  const titleSlug = normalizeDocumentPath(title);
  if (titleSlug) {
    return titleSlug;
  }

  return normalizeDocumentPath(String(fileName || '').replace(/\.[^.]+$/, ''));
}

export function buildMarkdownExport(draft: DocumentDraft, selectedDocument: DocumentDetail | null) {
  const frontmatter = parseFrontmatterText(draft.frontmatterText);
  const parts: string[] = [];

  if (Object.keys(frontmatter).length > 0) {
    parts.push('---');
    parts.push(serializeFrontmatter(frontmatter));
    parts.push('---');
    parts.push('');
  }

  parts.push(String(draft.content || '').replace(/\r\n/g, '\n').replace(/\s+$/, ''));
  return parts.join('\n').replace(/\n{3,}/g, '\n\n').trimEnd() + '\n';
}

export function buildDocumentExportFileName(draft: DocumentDraft, selectedDocument: DocumentDetail | null) {
  const slug = normalizeDocumentPath(draft.slug || draft.title || selectedDocument?.slug || selectedDocument?.title || 'document');
  return `${slug || 'document'}.md`;
}

export function normalizeDocumentPath(value: string) {
  const normalized = String(value || '')
    .trim()
    .replace(/\\/g, '/')
    .replace(/\.md$/i, '');

  if (!normalized) {
    return '';
  }

  return normalized
    .split('/')
    .map((segment) => String(segment || '')
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, ''))
    .filter(Boolean)
    .join('/');
}

export function parseFrontmatterText(value: string) {
  const result: Record<string, string> = {};
  const lines = String(value || '').split(/\r?\n/);

  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index].trim();
    if (!line || line === '---' || line === '...' || line.startsWith('#')) {
      continue;
    }

    const separatorIndex = line.indexOf(':');
    if (separatorIndex <= 0) {
      throw new Error(`Frontmatter line ${index + 1} must use 'key: value'.`);
    }

    const key = line.slice(0, separatorIndex).trim();
    const lineValue = line.slice(separatorIndex + 1).trim();
    if (!key) {
      throw new Error(`Frontmatter line ${index + 1} is missing a key.`);
    }

    result[key] = lineValue;
  }

  return result;
}

export function serializeFrontmatter(frontmatter: Record<string, unknown>) {
  return Object.entries(frontmatter)
    .map(([key, value]) => `${key}: ${value}`)
    .join('\n');
}