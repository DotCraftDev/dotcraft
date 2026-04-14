import {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
  type ForwardedRef
} from 'react'
import { COMMAND_REF_CLASS, FILE_REF_CLASS } from './richInputConstants'
import { serializeEditor, truncateEditorDomToSerializedLength } from './richInputSerialization'

const MAX_ROWS = 8
const MAX_TEXT_LEN = 100_000
const PLACEHOLDER = 'Ask DotCraft anything…'

export interface RichInputAreaHandle {
  getText: () => string
  clear: () => void
  focus: () => void
  insertFileTag: (relativePath: string) => void
  insertCommandTag: (commandName: string) => void
  /** Replace editor content with plain text (used for composer prefill). */
  setPlainText: (text: string) => void
}

interface RichInputAreaProps {
  disabled?: boolean
  placeholder?: string
  suppressSubmit?: boolean
  onToggleModeShortcut?: () => void
  onSubmit: () => void
  /** Omitted on welcome screen (no @ popover). */
  onAtQuery?: (query: string | null) => void
  onSlashQuery?: (query: string | null) => void
  onContentChange?: () => void
  onPasteImage?: (file: File) => void
  onPasteTextOversized?: () => void
}

/** Same linearization as textBeforeCaretForTriggers (tags = one boundary char). */
function linearizeForTriggers(root: HTMLElement): string {
  let out = ''
  const walk = (node: Node): void => {
    if (node.nodeType === Node.TEXT_NODE) {
      out += node.textContent ?? ''
      return
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return
    const el = node as HTMLElement
    if (el.tagName === 'STYLE' || el.tagName === 'SCRIPT') {
      return
    }
    if (el.classList.contains(FILE_REF_CLASS) || el.classList.contains(COMMAND_REF_CLASS)) {
      out += ' '
      return
    }
    if (el.tagName === 'BR') {
      out += '\n'
      return
    }
    for (const c of Array.from(el.childNodes)) {
      walk(c)
    }
  }
  for (const c of Array.from(root.childNodes)) {
    walk(c)
  }
  return out
}

function textBeforeCaretForTriggers(root: HTMLElement): string {
  const sel = window.getSelection()
  if (!sel || sel.rangeCount === 0) return ''
  const range = sel.getRangeAt(0)
  if (!root.contains(range.startContainer)) return ''
  const endRange = range.cloneRange()
  endRange.selectNodeContents(root)
  endRange.setEnd(range.startContainer, range.startOffset)
  const frag = endRange.cloneContents()
  const div = document.createElement('div')
  div.appendChild(frag)
  return linearizeForTriggers(div)
}

function walkToLinearOffset(
  root: HTMLElement,
  target: number
): { node: Node; offset: number } | null {
  let pos = 0
  function walk(node: Node): { node: Node; offset: number } | null {
    if (node.nodeType === Node.TEXT_NODE) {
      const len = (node.textContent ?? '').length
      if (pos + len >= target) {
        return { node, offset: target - pos }
      }
      pos += len
      return null
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return null
    const el = node as HTMLElement
    if (el.tagName === 'STYLE' || el.tagName === 'SCRIPT') {
      return null
    }
    if (el.classList.contains(FILE_REF_CLASS) || el.classList.contains(COMMAND_REF_CLASS)) {
      if (pos + 1 >= target) {
        return { node: el, offset: 0 }
      }
      pos += 1
      return null
    }
    if (el.tagName === 'BR') {
      if (pos + 1 >= target) {
        return { node: el, offset: 0 }
      }
      pos += 1
      return null
    }
    for (const c of Array.from(node.childNodes)) {
      const r = walk(c)
      if (r) return r
    }
    return null
  }
  return walk(root)
}

function parseAtQuery(beforeCaret: string): { fullMatch: string; query: string } | null {
  const m = /(?:^|[\s\n])@([^\s@]*)$/.exec(beforeCaret)
  if (!m) return null
  return { fullMatch: m[0], query: m[1] }
}

function parseSlashQuery(beforeCaret: string): { fullMatch: string; query: string } | null {
  const m = /(?:^|[\s\n])\/([^\s/]*)$/.exec(beforeCaret)
  if (!m) return null
  return { fullMatch: m[0], query: m[1] }
}

function isInlineTagElement(node: Node | null): node is HTMLElement {
  return (
    node instanceof HTMLElement &&
    (node.classList.contains(FILE_REF_CLASS) || node.classList.contains(COMMAND_REF_CLASS))
  )
}

export const RichInputArea = forwardRef(function RichInputArea(
  {
    disabled,
    placeholder = PLACEHOLDER,
    suppressSubmit,
    onToggleModeShortcut,
    onSubmit,
    onAtQuery,
    onSlashQuery,
    onContentChange,
    onPasteImage,
    onPasteTextOversized
  }: RichInputAreaProps,
  ref: ForwardedRef<RichInputAreaHandle>
) {
    const editorRef = useRef<HTMLDivElement>(null)
    const [showPh, setShowPh] = useState(true)

    const syncEmpty = useCallback(() => {
      const el = editorRef.current
      if (!el) return
      const t = el.textContent?.replace(/\u00a0/g, ' ').trim() ?? ''
      const hasTags =
        el.querySelector(`.${FILE_REF_CLASS}`) !== null ||
        el.querySelector(`.${COMMAND_REF_CLASS}`) !== null
      setShowPh(!t && !hasTags)
    }, [])

    const adjustHeight = useCallback((): void => {
      const el = editorRef.current
      if (!el) return
      el.style.height = 'auto'
      const lineHeight = parseInt(getComputedStyle(el).lineHeight) || 20
      const maxHeight = lineHeight * MAX_ROWS + 24
      el.style.height = `${Math.min(el.scrollHeight, maxHeight)}px`
    }, [])

    const getText = useCallback((): string => {
      const el = editorRef.current
      if (!el) return ''
      return serializeEditor(el).slice(0, MAX_TEXT_LEN)
    }, [])

    const clear = useCallback((): void => {
      const el = editorRef.current
      if (!el) return
      el.innerHTML = ''
      onAtQuery?.(null)
      onSlashQuery?.(null)
      setShowPh(true)
      adjustHeight()
      onContentChange?.()
    }, [adjustHeight, onAtQuery, onContentChange, onSlashQuery])

    const focusEditor = useCallback((): void => {
      editorRef.current?.focus()
    }, [])

    const insertFileTag = useCallback(
      (relativePath: string): void => {
        const el = editorRef.current
        if (!el) return
        const before = textBeforeCaretForTriggers(el)
        const parsed = parseAtQuery(before)
        if (!parsed) return

        const endLinear = before.length
        const leadingWs = parsed.fullMatch.length > 0 && parsed.fullMatch[0] !== '@' ? 1 : 0
        const startLinear = endLinear - parsed.fullMatch.length + leadingWs
        const startLoc = walkToLinearOffset(el, startLinear)
        const endLoc = walkToLinearOffset(el, endLinear)
        if (!startLoc || !endLoc) return

        const range = document.createRange()
        try {
          range.setStart(startLoc.node, startLoc.offset)
          range.setEnd(endLoc.node, endLoc.offset)
          range.deleteContents()
        } catch {
          return
        }

        const span = document.createElement('span')
        span.className = FILE_REF_CLASS
        span.setAttribute('data-relative-path', relativePath)
        span.setAttribute('contenteditable', 'false')
        span.style.display = 'inline-flex'
        span.style.alignItems = 'center'
        span.style.borderRadius = '4px'
        span.style.background = 'var(--bg-tertiary)'
        span.style.padding = '1px 6px'
        span.style.fontSize = '13px'
        span.style.verticalAlign = 'baseline'
        span.style.whiteSpace = 'nowrap'
        span.style.userSelect = 'none'
        span.style.gap = '4px'
        const fileName = relativePath.split('/').pop() ?? relativePath
        span.textContent = `📄 ${fileName}`
        span.title = relativePath

        const space = document.createTextNode('\u00a0')
        range.insertNode(span)
        range.setStartAfter(span)
        range.insertNode(space)
        range.setStartAfter(space)
        range.collapse(true)
        const sel = window.getSelection()
        sel?.removeAllRanges()
        sel?.addRange(range)

        onAtQuery?.(null)
        onSlashQuery?.(null)
        syncEmpty()
        adjustHeight()
        onContentChange?.()
      },
      [adjustHeight, onAtQuery, onContentChange, onSlashQuery, syncEmpty]
    )

    const insertCommandTag = useCallback(
      (commandName: string): void => {
        const el = editorRef.current
        if (!el) return
        const command = commandName.trim()
        if (!command.startsWith('/')) return
        const before = textBeforeCaretForTriggers(el)
        const parsed = parseSlashQuery(before)
        if (!parsed) return

        const endLinear = before.length
        const leadingWs = parsed.fullMatch.length > 0 && parsed.fullMatch[0] !== '/' ? 1 : 0
        const startLinear = endLinear - parsed.fullMatch.length + leadingWs
        const startLoc = walkToLinearOffset(el, startLinear)
        const endLoc = walkToLinearOffset(el, endLinear)
        if (!startLoc || !endLoc) return

        const range = document.createRange()
        try {
          range.setStart(startLoc.node, startLoc.offset)
          range.setEnd(endLoc.node, endLoc.offset)
          range.deleteContents()
        } catch {
          return
        }

        const span = document.createElement('span')
        span.className = COMMAND_REF_CLASS
        span.setAttribute('data-command', command)
        span.setAttribute('contenteditable', 'false')
        span.style.display = 'inline-flex'
        span.style.alignItems = 'center'
        span.style.borderRadius = '6px'
        span.style.background = 'color-mix(in srgb, var(--accent) 16%, transparent)'
        span.style.border = '1px solid color-mix(in srgb, var(--accent) 38%, transparent)'
        span.style.color = 'var(--accent)'
        span.style.padding = '1px 6px'
        span.style.fontSize = '13px'
        span.style.verticalAlign = 'baseline'
        span.style.whiteSpace = 'nowrap'
        span.style.userSelect = 'none'
        span.style.fontWeight = '600'
        span.textContent = command

        const space = document.createTextNode('\u00a0')
        range.insertNode(span)
        range.setStartAfter(span)
        range.insertNode(space)
        range.setStartAfter(space)
        range.collapse(true)
        const sel = window.getSelection()
        sel?.removeAllRanges()
        sel?.addRange(range)

        onAtQuery?.(null)
        onSlashQuery?.(null)
        syncEmpty()
        adjustHeight()
        onContentChange?.()
      },
      [adjustHeight, onAtQuery, onContentChange, onSlashQuery, syncEmpty]
    )

    const setPlainText = useCallback(
      (text: string): void => {
        const el = editorRef.current
        if (!el) return
        el.textContent = text
        onAtQuery?.(null)
        onSlashQuery?.(null)
        syncEmpty()
        adjustHeight()
        onContentChange?.()
      },
      [adjustHeight, onAtQuery, onContentChange, onSlashQuery, syncEmpty]
    )

    useImperativeHandle(
      ref,
      () => ({
        getText,
        clear,
        focus: focusEditor,
        insertFileTag,
        insertCommandTag,
        setPlainText
      }),
      [getText, clear, focusEditor, insertCommandTag, insertFileTag, setPlainText]
    )

    useEffect(() => {
      syncEmpty()
    }, [syncEmpty])

    const emitTriggerQueries = useCallback((): void => {
      const el = editorRef.current
      if (!el) return
      const before = textBeforeCaretForTriggers(el)
      const atParsed = parseAtQuery(before)
      const slashParsed = parseSlashQuery(before)
      if (atParsed && slashParsed) {
        const atStart = before.length - atParsed.fullMatch.length
        const slashStart = before.length - slashParsed.fullMatch.length
        if (atStart > slashStart) {
          onAtQuery?.(atParsed.query)
          onSlashQuery?.(null)
        } else {
          onAtQuery?.(null)
          onSlashQuery?.(slashParsed.query)
        }
        return
      }
      if (atParsed) {
        onAtQuery?.(atParsed.query)
        onSlashQuery?.(null)
        return
      }
      if (slashParsed) {
        onAtQuery?.(null)
        onSlashQuery?.(slashParsed.query)
        return
      }
      onAtQuery?.(null)
      onSlashQuery?.(null)
    }, [onAtQuery, onSlashQuery])

    const onInput = useCallback((): void => {
      syncEmpty()
      adjustHeight()
      emitTriggerQueries()
      onContentChange?.()
      const el = editorRef.current
      if (el && serializeEditor(el).length > MAX_TEXT_LEN) {
        truncateEditorDomToSerializedLength(el, MAX_TEXT_LEN)
        syncEmpty()
        adjustHeight()
        emitTriggerQueries()
        onContentChange?.()
      }
    }, [adjustHeight, emitTriggerQueries, onContentChange, syncEmpty])

    const onKeyDown = useCallback(
      (e: React.KeyboardEvent<HTMLDivElement>): void => {
        if (e.key === 'Tab' && e.shiftKey) {
          if (onToggleModeShortcut) {
            e.preventDefault()
            onToggleModeShortcut()
          }
          return
        }
        if (e.key === 'Enter' && !e.shiftKey) {
          if (suppressSubmit) {
            e.preventDefault()
            return
          }
          e.preventDefault()
          if (!disabled) onSubmit()
          return
        }
        if (e.key === 'Backspace') {
          const sel = window.getSelection()
          if (!sel || !sel.isCollapsed || !editorRef.current) return
          const range = sel.getRangeAt(0)
          const { startContainer, startOffset } = range
          if (startContainer.nodeType === Node.TEXT_NODE && startOffset === 0) {
            const prev = startContainer.previousSibling
            if (prev && prev.nodeType === Node.TEXT_NODE && (prev.textContent === '\u00a0' || prev.textContent === ' ')) {
              const beforeSpace = prev.previousSibling
              if (isInlineTagElement(beforeSpace)) {
                e.preventDefault()
                beforeSpace.remove()
                prev.remove()
                onInput()
                return
              }
            }
            if (isInlineTagElement(prev)) {
              e.preventDefault()
              prev.remove()
              onInput()
            }
          }
        }
      },
      [disabled, onInput, onSubmit, onToggleModeShortcut, suppressSubmit]
    )

    const onPaste = useCallback(
      (e: React.ClipboardEvent<HTMLDivElement>): void => {
        const items = Array.from(e.clipboardData.items)
        const imageItem = items.find((it) => it.type.startsWith('image/'))
        if (imageItem && onPasteImage) {
          e.preventDefault()
          const file = imageItem.getAsFile()
          if (file) onPasteImage(file)
          return
        }
        const pasted = e.clipboardData.getData('text/plain')
        if (pasted.length > MAX_TEXT_LEN) {
          e.preventDefault()
          const truncated = pasted.slice(0, MAX_TEXT_LEN)
          document.execCommand('insertText', false, truncated)
          onInput()
          onPasteTextOversized?.()
          return
        }
        e.preventDefault()
        const text = e.clipboardData.getData('text/plain')
        document.execCommand('insertText', false, text)
        onInput()
      },
      [onInput, onPasteImage, onPasteTextOversized]
    )

    return (
      <div style={{ position: 'relative', flex: 1, minWidth: 0 }}>
        <style>{`
          .rich-input-area[data-empty="true"]:before {
            content: attr(data-placeholder);
            color: var(--text-dimmed);
            pointer-events: none;
            position: absolute;
          }
          .rich-input-area:focus {
            border-color: var(--border-active);
          }
        `}</style>
        <div
          ref={editorRef}
          role="textbox"
          aria-multiline="true"
          aria-placeholder={placeholder}
          contentEditable={!disabled}
          suppressContentEditableWarning
          data-placeholder={placeholder}
          data-empty={showPh ? 'true' : 'false'}
          onInput={onInput}
          onKeyDown={onKeyDown}
          onPaste={onPaste}
          className="rich-input-area"
          style={{
            position: 'relative',
            flex: 1,
            resize: 'none',
            border: '1px solid var(--border-default)',
            borderRadius: '8px',
            padding: '8px 12px',
            fontSize: '14px',
            lineHeight: '20px',
            fontFamily: 'var(--font-sans)',
            backgroundColor: disabled ? 'var(--bg-tertiary)' : 'var(--bg-secondary)',
            color: disabled ? 'var(--text-dimmed)' : 'var(--text-primary)',
            outline: 'none',
            overflowY: 'auto',
            minHeight: '40px',
            cursor: disabled ? 'not-allowed' : 'text',
            opacity: disabled ? 0.6 : 1
          }}
        />
      </div>
    )
  }
)
