import { useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { ConfirmDialog } from '../ui/ConfirmDialog'
import { RejectDialog } from './RejectDialog'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useReviewPanelStore } from '../../stores/reviewPanelStore'
import type { AutomationTask } from '../../stores/automationsStore'

interface Props {
  task: AutomationTask
}

/**
 * Approve / Reject action bar for tasks in awaiting_review.
 */
export function ApproveRejectBar({ task }: Props): JSX.Element {
  const t = useT()
  const [showApproveConfirm, setShowApproveConfirm] = useState(false)
  const [showRejectDialog, setShowRejectDialog] = useState(false)
  const approveTask = useAutomationsStore((s) => s.approveTask)
  const rejectTask = useAutomationsStore((s) => s.rejectTask)
  const approving = useReviewPanelStore((s) => s.approving)
  const rejecting = useReviewPanelStore((s) => s.rejecting)
  const actionError = useReviewPanelStore((s) => s.actionError)

  async function handleApprove(): Promise<void> {
    useReviewPanelStore.setState({ approving: true, actionError: null })
    try {
      await approveTask(task.id, task.sourceName)
      setShowApproveConfirm(false)
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      useReviewPanelStore.setState({ actionError: msg })
    } finally {
      useReviewPanelStore.setState({ approving: false })
    }
  }

  async function handleReject(reason: string): Promise<void> {
    useReviewPanelStore.setState({ rejecting: true, actionError: null })
    try {
      await rejectTask(task.id, task.sourceName, reason || undefined)
      setShowRejectDialog(false)
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      useReviewPanelStore.setState({ actionError: msg })
    } finally {
      useReviewPanelStore.setState({ rejecting: false })
    }
  }

  return (
    <>
      <div
        style={{
          padding: '12px 16px',
          borderTop: '1px solid var(--border-default)',
          display: 'flex',
          flexDirection: 'column',
          gap: '8px',
          flexShrink: 0,
          backgroundColor: 'var(--bg-secondary)'
        }}
      >
        {actionError && (
          <div
            style={{
              padding: '8px 10px',
              borderRadius: '6px',
              backgroundColor: 'color-mix(in srgb, var(--error) 12%, transparent)',
              color: 'var(--error)',
              fontSize: '12px'
            }}
          >
            {actionError}
          </div>
        )}
        <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
          <button
            type="button"
            disabled={rejecting || approving}
            onClick={() => {
              useReviewPanelStore.setState({ actionError: null })
              setShowRejectDialog(true)
            }}
            style={{
              padding: '8px 16px',
              borderRadius: '6px',
              border: '1px solid var(--error)',
              backgroundColor: 'transparent',
              color: 'var(--error)',
              fontSize: '13px',
              fontWeight: 600,
              cursor: rejecting || approving ? 'default' : 'pointer',
              opacity: rejecting || approving ? 0.6 : 1
            }}
          >
            {rejecting ? t('auto.rejecting') : t('auto.reject')}
          </button>
          <button
            type="button"
            disabled={approving || rejecting}
            onClick={() => {
              useReviewPanelStore.setState({ actionError: null })
              setShowApproveConfirm(true)
            }}
            style={{
              padding: '8px 16px',
              borderRadius: '6px',
              border: 'none',
              backgroundColor: 'var(--success)',
              color: '#fff',
              fontSize: '13px',
              fontWeight: 600,
              cursor: approving || rejecting ? 'default' : 'pointer',
              opacity: approving || rejecting ? 0.6 : 1
            }}
          >
            {approving ? t('auto.approving') : t('auto.approve')}
          </button>
        </div>
      </div>

      {showApproveConfirm && (
        <ConfirmDialog
          title={t('auto.approveTitle')}
          message={t('auto.approveMessage')}
          confirmLabel={t('auto.approve')}
          onConfirm={() => void handleApprove()}
          onCancel={() => setShowApproveConfirm(false)}
        />
      )}

      {showRejectDialog && (
        <RejectDialog
          onConfirm={(reason) => void handleReject(reason)}
          onCancel={() => setShowRejectDialog(false)}
        />
      )}
    </>
  )
}
