/**
 * Popup menu anchored to the `+` button in the detail panel tab strip.
 * M1: "Open File" → opens the Quick-Open dialog.
 * M2 placeholder: "New Browser Tab" (disabled).
 */
import { useRef, useEffect, type KeyboardEvent } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { useThreadStore } from '../../stores/threadStore'
import { useConversationStore } from '../../stores/conversationStore'
import { FolderOpen, Globe, SquareTerminal } from 'lucide-react'

interface AddTabPopupProps {
  anchorRef: React.RefObject<HTMLElement | null>
  onClose: () => void
}

export function AddTabPopup({ anchorRef, onClose }: AddTabPopupProps): JSX.Element {
  const t = useT()
  const setQuickOpenVisible = useUIStore((s) => s.setQuickOpenVisible)
  const setDetailPanelVisible = useUIStore((s) => s.setDetailPanelVisible)
  const setActiveViewerTab = useUIStore((s) => s.setActiveViewerTab)
  const openBrowser = useViewerTabStore((s) => s.openBrowser)
  const openTerminal = useViewerTabStore((s) => s.openTerminal)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const popupRef = useRef<HTMLDivElement>(null)
  const shortcutText =
    window.api.platform === 'darwin'
      ? t('detailPanel.addTabOpenFileShortcut').replace('Ctrl', 'Cmd')
      : t('detailPanel.addTabOpenFileShortcut')

  // Close on outside click
  useEffect(() => {
    const handlePointerDown = (e: PointerEvent): void => {
      if (
        popupRef.current &&
        !popupRef.current.contains(e.target as Node) &&
        anchorRef.current &&
        !anchorRef.current.contains(e.target as Node)
      ) {
        onClose()
      }
    }
    document.addEventListener('pointerdown', handlePointerDown)
    return () => document.removeEventListener('pointerdown', handlePointerDown)
  }, [anchorRef, onClose])

  // Close on Escape
  const handleKeyDown = (e: KeyboardEvent<HTMLDivElement>): void => {
    if (e.key === 'Escape') {
      e.preventDefault()
      onClose()
      anchorRef.current?.focus()
    }
  }

  const handleOpenFile = (): void => {
    setQuickOpenVisible(true)
    setDetailPanelVisible(true)
    onClose()
  }

  const handleOpenBrowser = (): void => {
    if (!activeThreadId || !workspacePath) return
    const tabId = openBrowser({
      threadId: activeThreadId,
      initialLabel: t('viewer.newBrowserTab')
    })
    setActiveViewerTab(tabId)
    void window.api.workspace.viewer.browser.create({
      tabId,
      workspacePath
    })
    onClose()
  }

  const handleOpenTerminal = (): void => {
    if (!activeThreadId || !workspacePath) return
    const tabId = openTerminal({
      threadId: activeThreadId,
      cwd: workspacePath,
      initialLabel: t('viewer.newTerminalTab')
    })
    setActiveViewerTab(tabId)
    onClose()
  }

  // Calculate position from anchor
  const anchor = anchorRef.current?.getBoundingClientRect()
  const top = anchor ? anchor.bottom + 4 : 0
  const left = anchor ? anchor.left : 0

  return (
    <div
      ref={popupRef}
      role="menu"
      aria-label={t('detailPanel.addTab')}
      onKeyDown={handleKeyDown}
      style={{
        position: 'fixed',
        top,
        left,
        zIndex: 1000,
        minWidth: '180px',
        backgroundColor: 'var(--bg-elevated, #2a2a2a)',
        border: '1px solid var(--border-default)',
        borderRadius: '6px',
        boxShadow: '0 4px 16px rgba(0,0,0,0.3)',
        padding: '4px 0',
        outline: 'none'
      }}
      tabIndex={-1}
    >
      {/* Open File */}
      <button
        role="menuitem"
        onClick={handleOpenFile}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          width: '100%',
          padding: '6px 12px',
          border: 'none',
          background: 'transparent',
          color: 'var(--text-primary)',
          fontSize: '13px',
          cursor: 'pointer',
          textAlign: 'left'
        }}
        onMouseEnter={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-hover, rgba(255,255,255,0.06))'
        }}
        onMouseLeave={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
        }}
      >
        <FolderOpen size={14} aria-hidden style={{ display: 'block', flexShrink: 0 }} />
        <span style={{ flex: 1 }}>{t('detailPanel.addTabOpenFile')}</span>
        <span style={{ color: 'var(--text-secondary)', fontSize: '11px' }}>
          {shortcutText}
        </span>
      </button>

      {/* New Browser Tab */}
      <button
        role="menuitem"
        disabled={!activeThreadId || !workspacePath}
        onClick={handleOpenBrowser}
        title={t('detailPanel.addTabNewBrowser')}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          width: '100%',
          padding: '6px 12px',
          border: 'none',
          background: 'transparent',
          color: 'var(--text-primary)',
          fontSize: '13px',
          cursor: activeThreadId && workspacePath ? 'pointer' : 'not-allowed',
          textAlign: 'left'
        }}
        onMouseEnter={(e) => {
          if (!activeThreadId || !workspacePath) return
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-hover, rgba(255,255,255,0.06))'
        }}
        onMouseLeave={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
        }}
      >
        <Globe size={14} aria-hidden style={{ display: 'block', flexShrink: 0 }} />
        {t('detailPanel.addTabNewBrowser')}
      </button>

      <button
        role="menuitem"
        disabled={!activeThreadId || !workspacePath}
        onClick={handleOpenTerminal}
        title={t('detailPanel.addTabNewTerminal')}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          width: '100%',
          padding: '6px 12px',
          border: 'none',
          background: 'transparent',
          color: 'var(--text-primary)',
          fontSize: '13px',
          cursor: activeThreadId && workspacePath ? 'pointer' : 'not-allowed',
          textAlign: 'left'
        }}
        onMouseEnter={(e) => {
          if (!activeThreadId || !workspacePath) return
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-hover, rgba(255,255,255,0.06))'
        }}
        onMouseLeave={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
        }}
      >
        <SquareTerminal size={14} aria-hidden style={{ display: 'block', flexShrink: 0 }} />
        {t('detailPanel.addTabNewTerminal')}
      </button>
    </div>
  )
}
