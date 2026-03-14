import { describe, expect, it } from 'vitest';

import { convertHtmlToMarkdown, convertMarkdownToEditorHtml, normalizeEditorMarkdown } from './documentMarkdown';

describe('documentMarkdown', () => {
  it('returns an empty paragraph shell for empty markdown', () => {
    expect(convertMarkdownToEditorHtml('')).toBe('<p></p>');
  });

  it('round-trips common document structures through the editor bridge', () => {
    const markdown = [
      '# Portal Note',
      '',
      'Paragraph with **bold** and *italic* and `inline code`.',
      '',
      '- alpha',
      '- beta',
      '',
      '1. first',
      '2. second',
      '',
      '> quoted line',
      '',
      '```',
      'const answer = 42;',
      '```',
    ].join('\n');

    const html = convertMarkdownToEditorHtml(markdown);
    const roundTrip = convertHtmlToMarkdown(html);

    expect(roundTrip).toBe(normalizeEditorMarkdown(markdown));
  });

  it('round-trips links and trims trailing editor whitespace', () => {
    const markdown = 'See [OpenCortex](https://example.com/docs).   \n';

    const html = convertMarkdownToEditorHtml(markdown);
    const roundTrip = convertHtmlToMarkdown(html);

    expect(roundTrip).toBe('See [OpenCortex](https://example.com/docs).');
  });
});