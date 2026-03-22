import { useEffect } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { useUIStore } from '../../stores/uiStore'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { ChangesFileList } from './ChangesFileList'
import { DiffViewer } from './DiffViewer'
import { reconstructOriginalContent, reconstructNewContent } from '../../utils/diffReconstruct'

interface ChangesTabProps {
  workspacePath: string
}

/**
 * Changes tab content — file list + diff viewer.
 * Handles revert/re-apply by writing files to disk via IPC.
 * Spec §11.3
 */
export function ChangesTab({ workspacePath }: ChangesTabProps): JSX.Element {
  const changedFiles = useConversationStore((s) => s.changedFiles)
  const revertFile = useConversationStore((s) => s.revertFile)
  const reapplyFile = useConversationStore((s) => s.reapplyFile)
  const selectedFile = useUIStore((s) => s.selectedChangedFile)
  const selectFile = useUIStore((s) => s.selectChangedFile)
  const confirm = useConfirmDialog()

  const files = Array.from(changedFiles.values())
  const writtenFiles = files.filter((f) => f.status === 'written')
  const totalAdd = files.reduce((s, f) => s + f.additions, 0)
  const totalDel = files.reduce((s, f) => s + f.deletions, 0)

  // Auto-select first file when tab opens or file list changes
  useEffect(() => {
    if (files.length === 0) {
      selectFile(null)
      return
    }
    if (!selectedFile || !changedFiles.has(selectedFile)) {
      selectFile(files[0].filePath)
    }
  }, [changedFiles.size]) // eslint-disable-line react-hooks/exhaustive-deps

  const selectedDiff = selectedFile ? changedFiles.get(selectedFile) ?? null : null

  async function handleRevert(diff: typeof files[0]): Promise<void> {
    const absPath = toAbsPath(diff.filePath, workspacePath)
    try {
      if (diff.isNewFile) {
        await window.api.file.deleteFile(absPath)
      } else {
        const original = reconstructOriginalContent(diff)
        await window.api.file.writeFile(absPath, original)
      }
    } catch (err) {
      console.error('Revert failed:', err)
    }
    revertFile(diff.filePath)
  }

  async function handleReapply(diff: typeof files[0]): Promise<void> {
    const absPath = toAbsPath(diff.filePath, workspacePath)
    try {
      const newContent = reconstructNewContent(diff)
      await window.api.file.writeFile(absPath, newContent)
    } catch (err) {
      console.error('Re-apply failed:', err)
    }
    reapplyFile(diff.filePath)
  }

  async function handleRevertAll(): Promise<void> {
    const count = writtenFiles.length
    if (count === 0) return
    const confirmed = await confirm({
      title: 'Revert all changes?',
      message: `Revert all ${count} file change${count === 1 ? '' : 's'}? This cannot be undone.`,
      confirmLabel: 'Revert All',
      danger: true
    })
    if (!confirmed) return
    for (const diff of writtenFiles) {
      await handleRevert(diff)
    }
  }

  if (files.length === 0) {
    return (
      <div
        style={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          padding: '16px'
        }}
      >
        <p
          style={{
            textAlign: 'center',
            color: 'var(--text-dimmed)',
            fontSize: '13px',
            lineHeight: 1.7,
            whiteSpace: 'pre-line'
          }}
        >
          {'No file changes yet.\nThe agent\'s edits will appear here.'}
        </p>
      </div>
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%', overflow: 'hidden' }}>
      {/* Summary header */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          padding: '6px 10px',
          borderBottom: '1px solid var(--border-default)',
          flexShrink: 0,
          fontSize: '12px',
          color: 'var(--text-secondary)'
        }}
      >
        <span>{files.length} file{files.length !== 1 ? 's' : ''} changed</span>
        <span style={{ display: 'flex', gap: '4px' }}>
          {totalAdd > 0 && <span style={{ color: 'var(--success)' }}>+{totalAdd}</span>}
          {totalDel > 0 && <span style={{ color: 'var(--error)' }}>-{totalDel}</span>}
        </span>
        <span style={{ flex: 1 }} />
        {writtenFiles.length > 0 && (
          <button
            onClick={handleRevertAll}
            style={{
              padding: '2px 8px',
              fontSize: '11px',
              borderRadius: '4px',
              border: '1px solid var(--border-default)',
              background: 'transparent',
              color: 'var(--text-secondary)',
              cursor: 'pointer'
            }}
          >
            Revert All ↺
          </button>
        )}
      </div>

      {/* File list — fixed height portion */}
      <div style={{ flexShrink: 0, maxHeight: '40%', overflowY: 'auto', borderBottom: '1px solid var(--border-default)' }}>
        <ChangesFileList
          files={files}
          selectedFile={selectedFile}
          workspacePath={workspacePath}
          onSelect={selectFile}
          onRevert={handleRevert}
          onReapply={handleReapply}
        />
      </div>

      {/* Diff viewer — fills remaining space */}
      <div style={{ flex: 1, overflow: 'hidden' }}>
        {selectedDiff ? (
          <DiffViewer
            diff={selectedDiff}
            workspacePath={workspacePath}
            onRevert={selectedDiff.status === 'written'
              ? () => { void handleRevert(selectedDiff) }
              : () => { void handleReapply(selectedDiff) }
            }
          />
        ) : (
          <div
            style={{
              height: '100%',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              color: 'var(--text-dimmed)',
              fontSize: '12px'
            }}
          >
            Select a file to view its diff
          </div>
        )}
      </div>
    </div>
  )
}

function toAbsPath(filePath: string, workspacePath: string): string {
  // If filePath is already absolute, return as-is
  if (filePath.startsWith('/') || /^[A-Za-z]:\\/.test(filePath)) return filePath
  // Join workspace + relative path
  const ws = workspacePath.replace(/\\/g, '/')
  const rel = filePath.replace(/\\/g, '/')
  return `${ws}/${rel}`.replace(/\/\//g, '/')
}
