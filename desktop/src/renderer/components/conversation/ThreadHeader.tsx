import { useState, useRef, useEffect } from 'react'
import { PanelRightClose, PanelRightOpen } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { CommitDialog } from '../detail/CommitDialog'
import { CommitIcon } from '../ui/AppIcons'
import { OpenWorkspaceButton } from './OpenWorkspaceButton'

interface ThreadHeaderProps {
  threadName: string
  threadId: string
  workspacePath: string
}

/**
 * Fixed header bar at top of the conversation panel.
 * Shows thread name (double-click to rename inline), "Open" and "Commit" buttons.
 * Spec §10.2, M7-10
 */
export function ThreadHeader({ threadName, threadId, workspacePath }: ThreadHeaderProps): JSX.Element {
  const t = useT()
  const [commitOpen, setCommitOpen] = useState(false)
  const [renaming, setRenaming] = useState(false)
  const [renameValue, setRenameValue] = useState(threadName)
  const renameInputRef = useRef<HTMLInputElement>(null)
  const changedFiles = useConversationStore((s) => s.changedFiles)
  const detailPanelPreferredVisible = useUIStore((s) => s.detailPanelPreferredVisible)
  const toggleDetailPanel = useUIStore((s) => s.toggleDetailPanel)

  const writtenFiles = Array.from(changedFiles.values()).filter((f) => f.status === 'written')
  const hasWrittenFiles = writtenFiles.length > 0

  // Keep rename input value in sync when threadName changes externally
  useEffect(() => {
    if (!renaming) setRenameValue(threadName)
  }, [threadName, renaming])

  // Focus the input when entering rename mode
  useEffect(() => {
    if (renaming) {
      renameInputRef.current?.focus()
      renameInputRef.current?.select()
    }
  }, [renaming])

  function startRename(): void {
    setRenameValue(threadName)
    setRenaming(true)
  }

  async function commitRename(): Promise<void> {
    const newName = renameValue.trim()
    setRenaming(false)
    if (!newName || newName === threadName) return
    useThreadStore.getState().renameThread(threadId, newName)
    try {
      await window.api.appServer.sendRequest('thread/rename', {
        threadId,
        displayName: newName
      })
    } catch {
      // Roll back on failure
      useThreadStore.getState().renameThread(threadId, threadName)
    }
  }

  function cancelRename(): void {
    setRenaming(false)
    setRenameValue(threadName)
  }

  function handleRenameKeyDown(e: React.KeyboardEvent<HTMLInputElement>): void {
    if (e.key === 'Enter') { e.preventDefault(); void commitRename() }
    if (e.key === 'Escape') { e.preventDefault(); cancelRename() }
  }

  return (
    <>
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          padding: '10px 16px',
          borderBottom: '1px solid var(--border-default)',
          flexShrink: 0,
          height: 'var(--chrome-header-height)',
          boxSizing: 'border-box'
        }}
      >
        {/* Thread name — double-click to rename */}
        {renaming ? (
          <input
            ref={renameInputRef}
            value={renameValue}
            onChange={(e) => setRenameValue(e.target.value)}
            onKeyDown={handleRenameKeyDown}
            onBlur={() => { void commitRename() }}
            aria-label={t('threadHeader.renameAria')}
            style={{
              flex: 1,
              fontSize: '14px',
              fontWeight: 600,
              color: 'var(--text-primary)',
              background: 'var(--bg-secondary)',
              border: '1px solid var(--border-active)',
              borderRadius: '4px',
              padding: '2px 6px',
              outline: 'none',
              fontFamily: 'inherit'
            }}
          />
        ) : (
          <h1
            onDoubleClick={startRename}
            title={t('threadHeader.renameTitle')}
            style={{
              flex: 1,
              margin: 0,
              fontSize: '14px',
              fontWeight: 600,
              color: 'var(--text-primary)',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              cursor: 'default',
              userSelect: 'none'
            }}
          >
            {threadName}
          </h1>
        )}

        {/* Open button */}
        <OpenWorkspaceButton workspacePath={workspacePath} />

        <button
          onClick={toggleDetailPanel}
          title={detailPanelPreferredVisible ? t('threadHeader.panelToggleHide') : t('threadHeader.panelToggleShow')}
          aria-label={detailPanelPreferredVisible ? t('threadHeader.panelToggleHide') : t('threadHeader.panelToggleShow')}
          style={headerButtonStyle}
        >
          {detailPanelPreferredVisible
            ? <PanelRightClose size={13} aria-hidden />
            : <PanelRightOpen size={13} aria-hidden />}
        </button>

        {/* Commit button */}
        <button
          onClick={() => setCommitOpen(true)}
          disabled={!hasWrittenFiles}
          title={hasWrittenFiles ? t('threadHeader.commitTitle') : t('threadHeader.noCommitTitle')}
          style={{
            ...headerButtonStyle,
            opacity: hasWrittenFiles ? 1 : 0.4,
            cursor: hasWrittenFiles ? 'pointer' : 'default'
          }}
          aria-label={t('threadHeader.commitTitle')}
        >
          <CommitIcon size={13} />
          {t('threadHeader.commit')}
        </button>
      </div>

      {commitOpen && (
        <CommitDialog
          workspacePath={workspacePath}
          threadId={threadId}
          onClose={() => setCommitOpen(false)}
        />
      )}
    </>
  )
}

const headerButtonStyle: React.CSSProperties = {
  padding: '4px 10px',
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  fontSize: '12px',
  fontWeight: 500,
  color: 'var(--text-secondary)',
  backgroundColor: 'transparent',
  border: '1px solid var(--border-default)',
  borderRadius: '6px',
  cursor: 'pointer',
  flexShrink: 0,
  transition: 'background-color 100ms ease, color 100ms ease'
}
