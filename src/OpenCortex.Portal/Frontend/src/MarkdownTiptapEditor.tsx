import type { MouseEvent as ReactMouseEvent } from 'react';
import { useEffect, useMemo, useRef } from 'react';
import Link from '@tiptap/extension-link';
import Placeholder from '@tiptap/extension-placeholder';
import { EditorContent, type Editor as TiptapEditor, useEditor, useEditorState } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';

import { normalizeDocumentLinkPath, normalizeEditorLinkHref } from './documentLinks';
import { convertHtmlToMarkdown, convertMarkdownToEditorHtml, createMarkdownTurndownService, normalizeEditorMarkdown } from './documentMarkdown';

export type MarkdownTiptapEditorProps = {
  disabled: boolean;
  onChange: (value: string) => void;
  onEditorReady?: (editor: TiptapEditor | null) => void;
  onOpenDocumentLink?: (canonicalPath: string) => void;
  placeholder: string;
  requestLinkUrl?: (currentHref: string) => string | null;
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
  disabled,
  onChange,
  onEditorReady,
  onOpenDocumentLink,
  placeholder,
  requestLinkUrl,
  value,
}: MarkdownTiptapEditorProps) {
  const turndownService = useMemo(() => createMarkdownTurndownService(), []);
  const lastSyncedMarkdownRef = useRef(normalizeEditorMarkdown(value));
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

  function handleLinkActivate() {
    if (!editor) {
      return;
    }

    const currentHref = String(editor.getAttributes('link').href || '');
    const requestedHref = requestLinkUrl
      ? requestLinkUrl(currentHref)
      : window.prompt('Enter a URL or document path like daily/notes.md. Leave blank to remove the current link.', currentHref);

    if (requestedHref === null) {
      return;
    }

    const normalizedHref = normalizeEditorLinkHref(requestedHref);

    if (!normalizedHref) {
      editor.chain().focus().extendMarkRange('link').unsetLink().run();
      return;
    }

    if (editor.isActive('link')) {
      editor.chain().focus().extendMarkRange('link').setLink({ href: normalizedHref }).run();
      return;
    }

    editor.chain().focus().setLink({ href: normalizedHref }).run();
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
          active={toolbarState.isLink}
          disabled={disabled}
          onActivate={handleLinkActivate}
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

      <div className="document-editor-surface" onClickCapture={handleEditorSurfaceClick}>
        <EditorContent editor={editor} />
      </div>
      <p className="summary-detail editor-shell-note">
        Rich editing stays inside Tiptap, but document save and export still persist Markdown. Use canonical paths like daily/notes.md for internal links, then Ctrl+click to open them.
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

