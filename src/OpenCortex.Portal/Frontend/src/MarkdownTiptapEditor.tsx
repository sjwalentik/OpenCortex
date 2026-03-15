import type { KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from 'react';
import { useEffect, useMemo, useRef, useState } from 'react';
import Link from '@tiptap/extension-link';
import Placeholder from '@tiptap/extension-placeholder';
import { EditorContent, type Editor as TiptapEditor, useEditor, useEditorState } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';

import { normalizeDocumentLinkPath, normalizeEditorLinkHref } from './documentLinks';
import type { DocumentSummary } from './documentDraft';
import { convertHtmlToMarkdown, convertMarkdownToEditorHtml, createMarkdownTurndownService, normalizeEditorMarkdown } from './documentMarkdown';

export type MarkdownTiptapEditorProps = {
  availableDocumentLinks?: DocumentSummary[];
  currentDocumentPath?: string;
  disabled: boolean;
  onChange: (value: string) => void;
  onEditorReady?: (editor: TiptapEditor | null) => void;
  onOpenDocumentLink?: (canonicalPath: string) => void;
  placeholder: string;
  value: string;
};

type EditorToolbarButtonProps = {
  active?: boolean;
  disabled?: boolean;
  label: string;
  onActivate: () => void;
};

type ToolbarState = {
  isParagraph: boolean;
  isHeading1: boolean;
  isHeading2: boolean;
  isHeading3: boolean;
  isBold: boolean;
  isItalic: boolean;
  isCode: boolean;
  isLink: boolean;
  isBulletList: boolean;
  isOrderedList: boolean;
  isBlockquote: boolean;
  isCodeBlock: boolean;
};

type SelectionRange = {
  from: number;
  to: number;
};

const defaultToolbarState: ToolbarState = {
  isParagraph: false,
  isHeading1: false,
  isHeading2: false,
  isHeading3: false,
  isBold: false,
  isItalic: false,
  isCode: false,
  isLink: false,
  isBulletList: false,
  isOrderedList: false,
  isBlockquote: false,
  isCodeBlock: false,
};

export function MarkdownTiptapEditor({
  availableDocumentLinks = [],
  currentDocumentPath,
  disabled,
  onChange,
  onEditorReady,
  onOpenDocumentLink,
  placeholder,
  value,
}: MarkdownTiptapEditorProps) {
  const turndownService = useMemo(() => createMarkdownTurndownService(), []);
  const lastSyncedMarkdownRef = useRef(normalizeEditorMarkdown(value));
  const linkSelectionRef = useRef<SelectionRange | null>(null);
  const linkInputRef = useRef<HTMLInputElement | null>(null);
  const [isLinkPickerOpen, setIsLinkPickerOpen] = useState(false);
  const [linkPickerValue, setLinkPickerValue] = useState('');
  const [linkPickerInitialHref, setLinkPickerInitialHref] = useState('');
  const normalizedCurrentDocumentPath = useMemo(
    () => normalizeDocumentLinkPath(currentDocumentPath || ''),
    [currentDocumentPath]
  );
  const normalizedAvailableDocumentLinks = useMemo(() => {
    const seenPaths = new Set<string>();

    return availableDocumentLinks
      .map((document) => {
        const canonicalPath = normalizeDocumentLinkPath(document.canonicalPath || document.slug || '');
        if (!canonicalPath || canonicalPath === normalizedCurrentDocumentPath || seenPaths.has(canonicalPath)) {
          return null;
        }

        seenPaths.add(canonicalPath);
        return {
          canonicalPath,
          managedDocumentId: document.managedDocumentId,
          slug: document.slug || '',
          title: document.title || '',
        };
      })
      .filter((document): document is {
        canonicalPath: string;
        managedDocumentId: string;
        slug: string;
        title: string;
      } => document !== null);
  }, [availableDocumentLinks, normalizedCurrentDocumentPath]);
  const filteredLinkSuggestions = useMemo(() => {
    const query = linkPickerValue.trim().toLowerCase();
    if (!query) {
      return normalizedAvailableDocumentLinks.slice(0, 8);
    }

    return normalizedAvailableDocumentLinks
      .filter((document) => {
        return [document.title, document.slug, document.canonicalPath]
          .some((value) => value.toLowerCase().includes(query));
      })
      .slice(0, 8);
  }, [linkPickerValue, normalizedAvailableDocumentLinks]);
  const editor = useEditor({
    immediatelyRender: true,
    extensions: [
      StarterKit.configure({
        link: false,
      }),
      Link.configure({
        autolink: true,
        linkOnPaste: true,
        openOnClick: false,
        isAllowedUri: (url, ctx) => ctx.defaultValidate(url) || normalizeDocumentLinkPath(url) !== null,
      }),
      Placeholder.configure({
        placeholder,
        emptyEditorClass: 'is-editor-empty',
      }),
    ],
    content: convertMarkdownToEditorHtml(value),
    editable: !disabled,
    editorProps: {
      attributes: {
        class: 'tiptap-editor',
      },
    },
    onUpdate: ({ editor: activeEditor }) => {
      const nextMarkdown = normalizeEditorMarkdown(convertHtmlToMarkdown(activeEditor.getHTML(), turndownService));
      lastSyncedMarkdownRef.current = nextMarkdown;
      onChange(nextMarkdown);
    },
  }, []);
  const toolbarState = useEditorState({
    editor,
    selector: ({ editor: currentEditor }) => {
      if (!currentEditor) {
        return defaultToolbarState;
      }

      return {
        isParagraph: currentEditor.isActive('paragraph'),
        isHeading1: currentEditor.isActive('heading', { level: 1 }),
        isHeading2: currentEditor.isActive('heading', { level: 2 }),
        isHeading3: currentEditor.isActive('heading', { level: 3 }),
        isBold: currentEditor.isActive('bold'),
        isItalic: currentEditor.isActive('italic'),
        isCode: currentEditor.isActive('code'),
        isLink: currentEditor.isActive('link'),
        isBulletList: currentEditor.isActive('bulletList'),
        isOrderedList: currentEditor.isActive('orderedList'),
        isBlockquote: currentEditor.isActive('blockquote'),
        isCodeBlock: currentEditor.isActive('codeBlock'),
      } satisfies ToolbarState;
    },
  }) ?? defaultToolbarState;

  useEffect(() => {
    onEditorReady?.(editor ?? null);

    return () => {
      onEditorReady?.(null);
    };
  }, [editor, onEditorReady]);

  useEffect(() => {
    if (!editor) {
      return;
    }

    editor.setEditable(!disabled, false);
  }, [disabled, editor]);

  useEffect(() => {
    if (!editor) {
      return;
    }

    const normalizedValue = normalizeEditorMarkdown(value);
    if (normalizedValue === lastSyncedMarkdownRef.current) {
      return;
    }

    editor.commands.setContent(convertMarkdownToEditorHtml(value), {
      emitUpdate: false,
    });
    lastSyncedMarkdownRef.current = normalizedValue;
  }, [editor, value]);

  useEffect(() => {
    if (!isLinkPickerOpen) {
      return;
    }

    linkInputRef.current?.focus();
    linkInputRef.current?.select();
  }, [isLinkPickerOpen]);

  useEffect(() => {
    if (!disabled) {
      return;
    }

    closeLinkPicker();
  }, [disabled]);

  function closeLinkPicker() {
    setIsLinkPickerOpen(false);
    setLinkPickerValue('');
    setLinkPickerInitialHref('');
    linkSelectionRef.current = null;
  }

  function handleLinkButtonActivate() {
    if (!editor) {
      return;
    }

    if (isLinkPickerOpen) {
      closeLinkPicker();
      return;
    }

    const currentHref = String(editor.getAttributes('link').href || '');
    const selection = editor.state.selection;
    linkSelectionRef.current = {
      from: selection.from,
      to: selection.to,
    };
    setLinkPickerInitialHref(currentHref);
    setLinkPickerValue(currentHref);
    setIsLinkPickerOpen(true);
  }

  function buildLinkCommandChain() {
    if (!editor) {
      return null;
    }

    const chain = editor.chain().focus();
    const selection = linkSelectionRef.current;
    if (selection) {
      chain.setTextSelection(selection);
    }

    if (linkPickerInitialHref) {
      chain.extendMarkRange('link');
    }

    return chain;
  }

  function applyLinkValue(rawValue: string) {
    const normalizedHref = normalizeEditorLinkHref(rawValue);
    const chain = buildLinkCommandChain();
    if (!chain) {
      return;
    }

    if (!normalizedHref) {
      if (linkPickerInitialHref) {
        chain.unsetLink().run();
      }
      closeLinkPicker();
      return;
    }

    chain.setLink({ href: normalizedHref }).run();
    closeLinkPicker();
  }

  function handleLinkInputKeyDown(event: ReactKeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter') {
      event.preventDefault();
      applyLinkValue(linkPickerValue);
      return;
    }

    if (event.key === 'Escape') {
      event.preventDefault();
      closeLinkPicker();
    }
  }

  function handlePickerButtonMouseDown(event: ReactMouseEvent<HTMLButtonElement>) {
    event.preventDefault();
  }

  function handleSuggestionSelect(canonicalPath: string) {
    setLinkPickerValue(canonicalPath);
    applyLinkValue(canonicalPath);
  }

  function handleEditorSurfaceClick(event: ReactMouseEvent<HTMLDivElement>) {
    const target = event.target;
    if (!(target instanceof HTMLElement)) {
      return;
    }

    const link = target.closest('a[href]');
    if (!(link instanceof HTMLAnchorElement)) {
      return;
    }

    const documentPath = normalizeDocumentLinkPath(link.getAttribute('href') || '');
    if (!documentPath || !(event.metaKey || event.ctrlKey)) {
      return;
    }

    event.preventDefault();
    onOpenDocumentLink?.(documentPath);
  }

  if (!editor) {
    return (
      <div className="document-editor-shell">
        <p className="document-save-status">Loading editor...</p>
      </div>
    );
  }

  return (
    <div className={disabled ? 'document-editor-shell is-disabled' : 'document-editor-shell'}>
      <div className="editor-toolbar" role="toolbar" aria-label="Markdown editor controls">
        <EditorToolbarButton
          label="H1"
          active={toolbarState.isHeading1}
          disabled={disabled}
          onActivate={() => editor.chain().focus().toggleHeading({ level: 1 }).run()}
        />
        <EditorToolbarButton
          label="H2"
          active={toolbarState.isHeading2}
          disabled={disabled}
          onActivate={() => editor.chain().focus().toggleHeading({ level: 2 }).run()}
        />
        <EditorToolbarButton
          label="H3"
          active={toolbarState.isHeading3}
          disabled={disabled}
          onActivate={() => editor.chain().focus().toggleHeading({ level: 3 }).run()}
        />
        <EditorToolbarButton
          label="Bold"
          active={toolbarState.isBold}
          disabled={disabled}
          onActivate={() => editor.chain().focus().toggleBold().run()}
        />
        <EditorToolbarButton
          label="Italic"
          active={toolbarState.isItalic}
          disabled={disabled}
          onActivate={() => editor.chain().focus().toggleItalic().run()}
        />
        <EditorToolbarButton
          label="Code"
          active={toolbarState.isCode}
          disabled={disabled}
          onActivate={() => editor.chain().focus().toggleCode().run()}
        />
        <EditorToolbarButton
          label="Link"
          active={toolbarState.isLink || isLinkPickerOpen}
          disabled={disabled}
          onActivate={handleLinkButtonActivate}
        />
        <EditorToolbarButton
          label="Bullets"
          active={toolbarState.isBulletList}
          disabled={disabled}
          onActivate={() => editor.chain().focus().toggleBulletList().run()}
        />
        <EditorToolbarButton
          label="Numbers"
          active={toolbarState.isOrderedList}
          disabled={disabled}
          onActivate={() => editor.chain().focus().toggleOrderedList().run()}
        />
        <EditorToolbarButton
          label="Quote"
          active={toolbarState.isBlockquote}
          disabled={disabled}
          onActivate={() => editor.chain().focus().toggleBlockquote().run()}
        />
        <EditorToolbarButton
          label="Code Block"
          active={toolbarState.isCodeBlock}
          disabled={disabled}
          onActivate={() => editor.chain().focus().toggleCodeBlock().run()}
        />
        <EditorToolbarButton
          label="Paragraph"
          active={toolbarState.isParagraph}
          disabled={disabled}
          onActivate={() => editor.chain().focus().setParagraph().run()}
        />
      </div>

      {isLinkPickerOpen ? (
        <div className="editor-link-picker" role="group" aria-label="Link picker">
          <div className="editor-link-picker-row">
            <input
              ref={linkInputRef}
              type="text"
              className="editor-link-input"
              aria-label="Link destination"
              placeholder="Search documents or paste a URL/path"
              value={linkPickerValue}
              onChange={(event) => setLinkPickerValue(event.target.value)}
              onKeyDown={handleLinkInputKeyDown}
            />
            <button
              type="button"
              className="button"
              onMouseDown={handlePickerButtonMouseDown}
              onClick={() => applyLinkValue(linkPickerValue)}
              disabled={!linkPickerValue.trim()}
            >
              Apply
            </button>
            <button
              type="button"
              className="button"
              onMouseDown={handlePickerButtonMouseDown}
              onClick={() => applyLinkValue('')}
              disabled={!linkPickerInitialHref}
            >
              Remove
            </button>
            <button
              type="button"
              className="button"
              onMouseDown={handlePickerButtonMouseDown}
              onClick={closeLinkPicker}
            >
              Close
            </button>
          </div>

          {filteredLinkSuggestions.length > 0 ? (
            <div className="editor-link-suggestion-list" role="list" aria-label="Document link suggestions">
              {filteredLinkSuggestions.map((document) => (
                <button
                  key={document.managedDocumentId}
                  type="button"
                  className="editor-link-suggestion"
                  onMouseDown={handlePickerButtonMouseDown}
                  onClick={() => handleSuggestionSelect(document.canonicalPath)}
                >
                  <span className="editor-link-suggestion-title">{document.title || document.canonicalPath}</span>
                  <span className="editor-link-suggestion-meta">{document.canonicalPath}</span>
                </button>
              ))}
            </div>
          ) : null}

          <p className="summary-detail editor-link-picker-note">
            Type a URL, or search the current document rail and pick a managed document path.
          </p>
        </div>
      ) : null}

      <div className="document-editor-surface" onClickCapture={handleEditorSurfaceClick}>
        <EditorContent editor={editor} />
      </div>
      <p className="summary-detail editor-shell-note">
        Rich editing stays inside Tiptap, but document save and export still persist Markdown. Use the Link picker for managed documents, then Ctrl+click to open them.
      </p>
    </div>
  );
}

function EditorToolbarButton({
  active = false,
  disabled = false,
  label,
  onActivate,
}: EditorToolbarButtonProps) {
  function handleMouseDown(event: ReactMouseEvent<HTMLButtonElement>) {
    event.preventDefault();
    if (!disabled) {
      onActivate();
    }
  }

  function handleClick(event: ReactMouseEvent<HTMLButtonElement>) {
    event.preventDefault();
    if (!disabled && event.detail === 0) {
      onActivate();
    }
  }

  return (
    <button
      type="button"
      aria-pressed={active}
      className={active ? 'button editor-toolbar-button active' : 'button editor-toolbar-button'}
      disabled={disabled}
      onMouseDown={handleMouseDown}
      onClick={handleClick}
    >
      {label}
    </button>
  );
}
