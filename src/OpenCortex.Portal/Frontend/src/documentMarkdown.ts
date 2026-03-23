import { marked } from 'marked';
import TurndownService from 'turndown';

export function convertMarkdownToEditorHtml(markdown: string) {
  const source = String(markdown || '').replace(/\r\n/g, '\n');
  if (!source.trim()) {
    return '<p></p>';
  }

  const rendered = marked.parse(source, {
    async: false,
    gfm: true,
  });

  return typeof rendered === 'string' && rendered.trim() ? rendered : '<p></p>';
}

export function convertHtmlToMarkdown(html: string, turndownService = createMarkdownTurndownService()) {
  return normalizeEditorMarkdown(turndownService.turndown(String(html || '')));
}

export function createMarkdownTurndownService() {
  const turndownService = new TurndownService({
    bulletListMarker: '-',
    codeBlockStyle: 'fenced',
    emDelimiter: '*',
    headingStyle: 'atx',
    strongDelimiter: '**',
  });

  turndownService.addRule('listItemParagraphCompat', {
    filter: 'li',
    replacement(content, node) {
      const normalizedContent = String(content || '')
        .replace(/^\n+|\n+$/g, '')
        .replace(/\n{3,}/g, '\n\n');

      const parent = node.parentNode as HTMLOListElement | HTMLUListElement | null;
      if (parent?.nodeName === 'OL') {
        const siblings = Array.from(parent.children);
        const index = siblings.indexOf(node as Element);
        const start = Number(parent.getAttribute('start') || '1');
        return `${start + Math.max(index, 0)}. ${normalizedContent}\n`;
      }

      return `- ${normalizedContent}\n`;
    },
  });

  return turndownService;
}

export function normalizeEditorMarkdown(value: string) {
  return String(value || '')
    .replace(/\r\n/g, '\n')
    .replace(/\u00a0/g, ' ')
    .replace(/^([*-])\s+/gm, '$1 ')
    .replace(/^(\d+\.)\s+/gm, '$1 ')
    .replace(/[ \t]+\n/g, '\n')
    .replace(/\n{3,}/g, '\n\n')
    .replace(/\s+$/g, '');
}