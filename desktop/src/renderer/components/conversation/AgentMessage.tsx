import { useMemo, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { ContextMenu, type ContextMenuItem, type ContextMenuPosition } from '../ui/ContextMenu'
import { MarkdownRenderer } from './MarkdownRenderer'
import { MessageCopyButton } from './MessageCopyButton'

interface AgentMessageProps {
  text: string
  streaming?: boolean
}

/**
 * Renders agent message text as Markdown.
 * Spec §10.3.3
 */
export function AgentMessage({ text, streaming = false }: AgentMessageProps): JSX.Element {
  const t = useT()
  const [hovered, setHovered] = useState(false)
  const [contextMenuPosition, setContextMenuPosition] = useState<ContextMenuPosition | null>(null)
  const [selectionText, setSelectionText] = useState('')

  async function copyText(content: string): Promise<void> {
    if (content.length === 0) return
    try {
      await navigator.clipboard.writeText(content)
      addToast(t('toast.copied'), 'success', 2000)
    } catch {
      // Ignore clipboard failures silently.
    }
  }

  function handleContextMenu(event: React.MouseEvent<HTMLDivElement>): void {
    event.preventDefault()
    const selected = window.getSelection()?.toString() ?? ''
    setSelectionText(selected)
    setContextMenuPosition({ x: event.clientX, y: event.clientY })
  }

  const contextItems = useMemo<ContextMenuItem[]>(() => {
    const items: ContextMenuItem[] = []
    if (selectionText.trim().length > 0) {
      items.push({
        label: t('conversation.copySelection'),
        onClick: () => {
          void copyText(selectionText)
        }
      })
    }
    items.push({
      label: t('conversation.copyMessage'),
      onClick: () => {
        void copyText(text)
      }
    })
    return items
  }, [selectionText, t, text])

  return (
    <div
      style={{ position: 'relative', userSelect: 'text' }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onContextMenu={handleContextMenu}
    >
      <MarkdownRenderer content={text} />
      <MessageCopyButton
        getText={() => text}
        visible={hovered && text.length > 0}
        disabled={streaming}
      />
      {contextMenuPosition && (
        <ContextMenu
          items={contextItems}
          position={contextMenuPosition}
          onClose={() => {
            setContextMenuPosition(null)
          }}
        />
      )}
    </div>
  )
}
