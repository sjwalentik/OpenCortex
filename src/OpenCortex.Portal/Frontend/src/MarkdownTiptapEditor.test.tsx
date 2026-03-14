import { act } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { Editor as TiptapEditor } from '@tiptap/react';

import { MarkdownTiptapEditor } from './MarkdownTiptapEditor';
import { convertMarkdownToEditorHtml } from './documentMarkdown';

afterEach(() => {
  cleanup();
});

if (!HTMLElement.prototype.scrollIntoView) {
  HTMLElement.prototype.scrollIntoView = () => {};
}

const emptyDomRect = {
  x: 0,
  y: 0,
  top: 0,
  right: 0,
  bottom: 0,
  left: 0,
  width: 0,
  height: 0,
  toJSON: () => ({}),
};

const emptyDomRectList = {
  length: 0,
  item: () => null,
  [Symbol.iterator]: function* () {},
};

HTMLElement.prototype.getBoundingClientRect = () => emptyDomRect;
HTMLElement.prototype.getClientRects = () => emptyDomRectList as DOMRectList;
Range.prototype.getBoundingClientRect = () => emptyDomRect;
Range.prototype.getClientRects = () => emptyDomRectList as DOMRectList;

describe('MarkdownTiptapEditor', () => {
  it('renders the editor toolbar and initial markdown content', async () => {
    const { container } = render(
      <MarkdownTiptapEditor
        disabled={false}
        onChange={() => {}}
        placeholder="Write Markdown content here."
        value={'# Daily Note\n\nInitial body'}
      />
    );

    expect(screen.getByRole('toolbar', { name: 'Markdown editor controls' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Bold' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Link' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Numbers' })).toBeTruthy();

    await waitFor(() => {
      const editor = container.querySelector('.tiptap-editor');
      expect(editor?.textContent).toContain('Daily Note');
      expect(editor?.textContent).toContain('Initial body');
    });
  });

  it('emits normalized markdown when the editor content changes', async () => {
    const handleChange = vi.fn();
    let editorInstance: TiptapEditor | null = null;

    render(
      <MarkdownTiptapEditor
        disabled={false}
        onChange={handleChange}
        onEditorReady={(editor) => {
          if (editor) {
            editorInstance = editor;
          }
        }}
        placeholder="Write Markdown content here."
        value={'# Daily Note\n\nInitial body'}
      />
    );

    await waitFor(() => expect(editorInstance).toBeTruthy());

    act(() => {
      editorInstance?.commands.setContent(convertMarkdownToEditorHtml('# Updated Title\n\n- alpha\n- beta'));
    });

    await waitFor(() => {
      expect(handleChange).toHaveBeenCalled();
      expect(handleChange.mock.lastCall?.[0]).toBe('# Updated Title\n\n- alpha\n- beta');
    });
  });

  it('reflects the selected block format without mutating content', async () => {
    let editorInstance: TiptapEditor | null = null;

    render(
      <MarkdownTiptapEditor
        disabled={false}
        onChange={() => {}}
        onEditorReady={(editor) => {
          if (editor) {
            editorInstance = editor;
          }
        }}
        placeholder="Write Markdown content here."
        value={'# Heading\n\nBody paragraph'}
      />
    );

    await waitFor(() => expect(editorInstance).toBeTruthy());
    const originalHtml = editorInstance?.getHTML();

    act(() => {
      editorInstance?.commands.focus('start');
    });

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'H1' }).getAttribute('aria-pressed')).toBe('true');
      expect(screen.getByRole('button', { name: 'Paragraph' }).getAttribute('aria-pressed')).toBe('false');
    });

    act(() => {
      editorInstance?.commands.focus('end');
    });

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Paragraph' }).getAttribute('aria-pressed')).toBe('true');
      expect(screen.getByRole('button', { name: 'H1' }).getAttribute('aria-pressed')).toBe('false');
    });

    expect(editorInstance?.getHTML()).toBe(originalHtml);
  });

  it('applies inline marks from the toolbar while preserving the current selection', async () => {
    let editorInstance: TiptapEditor | null = null;

    render(
      <MarkdownTiptapEditor
        disabled={false}
        onChange={() => {}}
        onEditorReady={(editor) => {
          if (editor) {
            editorInstance = editor;
          }
        }}
        placeholder="Write Markdown content here."
        value={'alpha beta'}
      />
    );

    await waitFor(() => expect(editorInstance).toBeTruthy());

    act(() => {
      editorInstance?.commands.focus('start');
      editorInstance?.commands.selectAll();
    });

    fireEvent.mouseDown(screen.getByRole('button', { name: 'Bold' }));
    await waitFor(() => {
      expect(editorInstance?.getHTML()).toContain('<strong>alpha beta</strong>');
      expect(screen.getByRole('button', { name: 'Bold' }).getAttribute('aria-pressed')).toBe('true');
    });

    act(() => {
      editorInstance?.commands.selectAll();
    });

    fireEvent.mouseDown(screen.getByRole('button', { name: 'Italic' }));
    await waitFor(() => {
      expect(editorInstance?.getHTML()).toContain('<strong>');
      expect(editorInstance?.getHTML()).toContain('<em>');
      expect(editorInstance?.getHTML()).toContain('alpha beta');
      expect(screen.getByRole('button', { name: 'Italic' }).getAttribute('aria-pressed')).toBe('true');
    });

    act(() => {
      editorInstance?.commands.selectAll();
    });

    fireEvent.mouseDown(screen.getByRole('button', { name: 'Code' }));
    await waitFor(() => {
      expect(editorInstance?.getHTML()).toContain('<code>');
      expect(editorInstance?.getHTML()).toContain('alpha beta');
      expect(screen.getByRole('button', { name: 'Code' }).getAttribute('aria-pressed')).toBe('true');
    });
  });

  it('applies and removes links through the toolbar prompt handler', async () => {
    const handleChange = vi.fn();
    const requestLinkUrl = vi.fn()
      .mockReturnValueOnce('example.com/docs')
      .mockReturnValueOnce('');
    let editorInstance: TiptapEditor | null = null;

    render(
      <MarkdownTiptapEditor
        disabled={false}
        onChange={handleChange}
        onEditorReady={(editor) => {
          if (editor) {
            editorInstance = editor;
          }
        }}
        placeholder="Write Markdown content here."
        requestLinkUrl={requestLinkUrl}
        value={'alpha beta'}
      />
    );

    await waitFor(() => expect(editorInstance).toBeTruthy());

    act(() => {
      editorInstance?.commands.focus('start');
      editorInstance?.commands.selectAll();
    });

    fireEvent.mouseDown(screen.getByRole('button', { name: 'Link' }));

    await waitFor(() => {
      expect(requestLinkUrl).toHaveBeenCalledWith('');
      expect(editorInstance?.getHTML()).toContain('href="https://example.com/docs"');
      expect(handleChange.mock.lastCall?.[0]).toContain('[alpha beta](https://example.com/docs)');
    });

    act(() => {
      editorInstance?.commands.focus('start');
    });

    fireEvent.mouseDown(screen.getByRole('button', { name: 'Link' }));

    await waitFor(() => {
      expect(requestLinkUrl).toHaveBeenLastCalledWith('https://example.com/docs');
      expect(editorInstance?.getHTML()).not.toContain('href="https://example.com/docs"');
      expect(handleChange.mock.lastCall?.[0]).toBe('alpha beta');
    });
  });
  it('applies internal document links and opens them with ctrl-click', async () => {
    const handleChange = vi.fn();
    const handleOpenDocumentLink = vi.fn();
    const requestLinkUrl = vi.fn().mockReturnValueOnce('daily/linked-note');
    let editorInstance: TiptapEditor | null = null;

    const { container } = render(
      <MarkdownTiptapEditor
        disabled={false}
        onChange={handleChange}
        onEditorReady={(editor) => {
          if (editor) {
            editorInstance = editor;
          }
        }}
        onOpenDocumentLink={handleOpenDocumentLink}
        placeholder="Write Markdown content here."
        requestLinkUrl={requestLinkUrl}
        value={'alpha beta'}
      />
    );

    await waitFor(() => expect(editorInstance).toBeTruthy());

    act(() => {
      editorInstance?.commands.focus('start');
      editorInstance?.commands.selectAll();
    });

    fireEvent.mouseDown(screen.getByRole('button', { name: 'Link' }));

    await waitFor(() => {
      expect(editorInstance?.getHTML()).toContain('href="daily/linked-note.md"');
      expect(handleChange.mock.lastCall?.[0]).toContain('[alpha beta](daily/linked-note.md)');
    });

    const internalLink = container.querySelector('a[href="daily/linked-note.md"]');
    expect(internalLink).toBeTruthy();

    fireEvent.click(internalLink!, { ctrlKey: true });
    expect(handleOpenDocumentLink).toHaveBeenCalledWith('daily/linked-note.md');
  });

  it('supports ordered lists from the toolbar and preserves markdown numbering', async () => {
    const handleChange = vi.fn();
    let editorInstance: TiptapEditor | null = null;

    render(
      <MarkdownTiptapEditor
        disabled={false}
        onChange={handleChange}
        onEditorReady={(editor) => {
          if (editor) {
            editorInstance = editor;
          }
        }}
        placeholder="Write Markdown content here."
        value={'alpha beta'}
      />
    );

    await waitFor(() => expect(editorInstance).toBeTruthy());

    act(() => {
      editorInstance?.commands.focus('start');
      editorInstance?.commands.selectAll();
    });

    fireEvent.mouseDown(screen.getByRole('button', { name: 'Numbers' }));

    await waitFor(() => {
      expect(editorInstance?.getHTML()).toContain('<ol>');
      expect(handleChange).toHaveBeenCalled();
      expect(handleChange.mock.lastCall?.[0]).toContain('1. alpha beta');
    });
  });

  it('reloads external value changes and disables the toolbar when requested', async () => {
    let editorInstance: TiptapEditor | null = null;
    const { container, rerender } = render(
      <MarkdownTiptapEditor
        disabled={false}
        onChange={() => {}}
        onEditorReady={(editor) => {
          if (editor) {
            editorInstance = editor;
          }
        }}
        placeholder="Write Markdown content here."
        value={'# First Document\n\nAlpha'}
      />
    );

    await waitFor(() => expect(editorInstance).toBeTruthy());

    rerender(
      <MarkdownTiptapEditor
        disabled={true}
        onChange={() => {}}
        onEditorReady={(editor) => {
          if (editor) {
            editorInstance = editor;
          }
        }}
        placeholder="Write Markdown content here."
        value={'# Reloaded Document\n\nBeta'}
      />
    );

    await waitFor(() => {
      const editor = container.querySelector('.tiptap-editor');
      expect(editor?.textContent).toContain('Reloaded Document');
      expect(editor?.textContent).toContain('Beta');
    });

    expect(container.querySelector('.document-editor-shell')?.className).toContain('is-disabled');
    const buttons = Array.from(container.querySelectorAll('button')) as HTMLButtonElement[];
    const boldButton = buttons.find((button) => button.textContent === 'Bold');
    expect(boldButton?.disabled).toBe(true);
    expect(editorInstance?.isEditable).toBe(false);
  });
});