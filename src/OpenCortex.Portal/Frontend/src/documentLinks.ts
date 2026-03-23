export function normalizeDocumentLinkPath(value: string) {
  const rawValue = String(value || '').trim();
  let trimmed = rawValue;
  if (!trimmed) {
    return null;
  }

  if (trimmed.toLowerCase().startsWith('ocdoc:')) {
    trimmed = trimmed.slice('ocdoc:'.length);
  }

  if (/^[a-z][a-z\d+.-]*:/i.test(trimmed)) {
    return null;
  }

  trimmed = trimmed
    .replace(/\\/g, '/')
    .replace(/^\/+/, '')
    .replace(/^\.\//, '')
    .split(/[?#]/, 1)[0]
    .trim();

  if (!trimmed || trimmed.startsWith('../')) {
    return null;
  }

  const segments = trimmed.split('/').filter(Boolean);
  const firstSegment = segments[0] || '';
  const lastSegment = segments.at(-1) || '';
  const isExplicitDocumentPath =
    rawValue.toLowerCase().startsWith('ocdoc:') ||
    /^[\\/]/.test(rawValue) ||
    rawValue.startsWith('./') ||
    trimmed.endsWith('.md');
  const looksLikeDomainPath =
    segments.length > 1 &&
    firstSegment.includes('.') &&
    !trimmed.endsWith('.md');

  if (!(trimmed.includes('/') || trimmed.endsWith('.md'))) {
    return null;
  }

  if (!isExplicitDocumentPath && looksLikeDomainPath) {
    return null;
  }

  if (!/\.[a-z\d]+$/i.test(lastSegment)) {
    trimmed = `${trimmed}.md`;
  }

  return trimmed;
}

export function isExternalLinkHref(value: string) {
  const trimmed = String(value || '').trim();
  return /^[a-z][a-z\d+.-]*:/i.test(trimmed) && !trimmed.toLowerCase().startsWith('ocdoc:');
}

export function normalizeEditorLinkHref(value: string) {
  const trimmed = String(value || '').trim();
  if (!trimmed) {
    return '';
  }

  const documentPath = normalizeDocumentLinkPath(trimmed);
  if (documentPath) {
    return documentPath;
  }

  if (isExternalLinkHref(trimmed)) {
    return trimmed;
  }

  return `https://${trimmed}`;
}

