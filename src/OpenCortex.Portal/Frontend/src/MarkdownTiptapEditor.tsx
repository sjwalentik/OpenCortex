import type { MouseEvent as ReactMouseEvent } from 'react';
import { useEffect, useMemo, useRef } from 'react';
import Placeholder from '@tiptap/extension-placeholder';
import { EditorContent, type Editor as TiptapEditor, useEditor, useEditorState } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';

import { convertHtmlToMarkdown, convertMarkdownToEditorHtml, createMarkdownTurndownService, normalizeEditorMarkdown } from './documentMarkdown';

export type MarkdownTiptapEditorProps = {
  disabled: boolean;
  onChange: (value: string) => void;
  onEditorReady?: (editor: TiptapEditor | null) => void;
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
  isBulletList: boolean;
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
  isBulletList: false,
  isBlockquote: false,
  isCodeBlock: false,
};

export function MarkdownTiptapEditor({
  disabled,
  onChange,
  onEditorReady,
  placeholder,
  value,
}: MarkdownTiptapEditorProps) {
  const turndownService = useMemo(() => createMarkdownTurndownService(), []);
  const lastSyncedMarkdownRef = useRef(normalizeEditorMarkdown(value));
  const editor = useEditor({
    immediatelyRender: true,
    extensions: [
      StarterKit,
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
        isBulletList: currentEditor.isActive('bulletList'),
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
          label="Bullets"
          active={toolbarState.isBulletList}
          disabled={disabled}
          onActivate={() => editor.chain().focus().toggleBulletList().run()}
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

      <EditorContent editor={editor} className="document-editor-surface" />
      <p className="summary-detail editor-shell-note">
        Rich editing stays inside Tiptap, but document save and export still persist Markdown.
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