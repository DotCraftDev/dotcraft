import { useState } from 'react'
import type { CronJobWire } from '../../stores/cronStore'
import { useCronStore } from '../../stores/cronStore'
import { useAutomationsStore } from '../../stores/automationsStore'
import { ConfirmDialog } from '../ui/ConfirmDialog'

function formatSchedule(job: CronJobWire): string {
  const s = job.schedule
  if (s.kind === 'at' && s.atMs != null) {
    return `Once at ${new Date(s.atMs).toLocaleString()}`
  }
  if (s.kind === 'every' && s.everyMs != null) {
    const ms = s.everyMs
    const sec = Math.floor(ms / 1000)
    if (sec < 60) return `Every ${sec}s`
    const min = Math.floor(sec / 60)
    if (min < 60) return `Every ${min}m`
    const h = Math.floor(min / 60)
    if (h < 48) return `Every ${h}h`
    const d = Math.floor(h / 24)
    return `Every ${d}d`
  }
  return job.schedule.kind
}

function relativeLastRun(lastRunAtMs?: number | null): string {
  if (lastRunAtMs == null) return 'Never'
  const diff = Date.now() - lastRunAtMs
  const seconds = Math.floor(diff / 1000)
  if (seconds < 60) return 'just now'
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  return `${days}d ago`
}

export function CronJobCard({ job }: { job: CronJobWire }): JSX.Element {
  const [hovered, setHovered] = useState(false)
  const [showDelete, setShowDelete] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const selectCronJob = useCronStore((s) => s.selectCronJob)
  const clearTaskSelection = useAutomationsStore((s) => s.selectTask)
  const removeJob = useCronStore((s) => s.removeJob)
  const enableJob = useCronStore((s) => s.enableJob)
  const selectedId = useCronStore((s) => s.selectedCronJobId)

  const st = job.state
  const ok = st.lastStatus === 'ok' || st.lastStatus == null
  const err = st.lastStatus === 'error'
  const dotColor = !job.enabled
    ? 'var(--text-tertiary)'
    : err
      ? 'var(--error)'
      : ok
        ? 'var(--success)'
        : 'var(--text-tertiary)'

  const preview =
    st.lastError && err
      ? st.lastError
      : st.lastResult
        ? st.lastResult
        : '—'

  async function handleDeleteConfirm(): Promise<void> {
    setDeleting(true)
    try {
      await removeJob(job.id)
      setShowDelete(false)
    } finally {
      setDeleting(false)
    }
  }

  return (
    <>
      <div
        role="button"
        tabIndex={0}
        onClick={() => {
          if (st.lastThreadId) {
            clearTaskSelection(null)
            selectCronJob(job.id)
          }
        }}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            if (st.lastThreadId) {
              clearTaskSelection(null)
              selectCronJob(job.id)
            }
          }
        }}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        style={{
          display: 'flex',
          alignItems: 'flex-start',
          gap: '12px',
          padding: '10px 14px',
          borderRadius: '8px',
          backgroundColor:
            selectedId === job.id
              ? 'var(--bg-tertiary)'
              : hovered
                ? 'var(--bg-secondary)'
                : 'transparent',
          cursor: st.lastThreadId ? 'pointer' : 'default',
          transition: 'background-color 0.15s'
        }}
      >
        <div
          style={{
            width: '8px',
            height: '8px',
            borderRadius: '50%',
            marginTop: '5px',
            backgroundColor: dotColor,
            flexShrink: 0
          }}
        />
        <div style={{ flex: 1, minWidth: 0 }}>
          <div
            style={{
              fontWeight: 600,
              fontSize: '13px',
              color: 'var(--text-primary)',
              whiteSpace: 'nowrap',
              overflow: 'hidden',
              textOverflow: 'ellipsis'
            }}
          >
            {job.name}
          </div>
          <div
            style={{
              fontSize: '11px',
              color: 'var(--text-tertiary)',
              marginTop: '2px'
            }}
          >
            {formatSchedule(job)}
          </div>
          <div
            style={{
              fontSize: '12px',
              color: err ? 'var(--error)' : 'var(--text-secondary)',
              marginTop: '4px',
              whiteSpace: 'nowrap',
              overflow: 'hidden',
              textOverflow: 'ellipsis'
            }}
          >
            Last run: {relativeLastRun(st.lastRunAtMs)} · {preview}
          </div>
        </div>
        <div
          style={{
            flexShrink: 0,
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'flex-end',
            gap: '6px'
          }}
        >
          <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap', justifyContent: 'flex-end' }}>
            {st.lastThreadId && (
              <button
                type="button"
                onClick={(e) => {
                  e.stopPropagation()
                  clearTaskSelection(null)
                  selectCronJob(job.id)
                }}
                style={{
                  padding: '4px 10px',
                  borderRadius: '6px',
                  border: '1px solid var(--border-default)',
                  backgroundColor: 'transparent',
                  color: 'var(--text-secondary)',
                  fontSize: '11px',
                  fontWeight: 500,
                  cursor: 'pointer'
                }}
              >
                View
              </button>
            )}
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation()
                void enableJob(job.id, !job.enabled)
              }}
              style={{
                padding: '4px 10px',
                borderRadius: '6px',
                border: '1px solid var(--border-default)',
                backgroundColor: 'transparent',
                color: 'var(--text-secondary)',
                fontSize: '11px',
                cursor: 'pointer'
              }}
            >
              {job.enabled ? 'Disable' : 'Enable'}
            </button>
            <button
              type="button"
              disabled={deleting}
              onClick={(e) => {
                e.stopPropagation()
                setShowDelete(true)
              }}
              style={{
                padding: '4px 10px',
                borderRadius: '6px',
                border: '1px solid color-mix(in srgb, var(--error) 35%, var(--border-default))',
                backgroundColor: 'transparent',
                color: 'var(--error)',
                fontSize: '11px',
                fontWeight: 600,
                cursor: deleting ? 'default' : 'pointer',
                opacity: deleting ? 0.6 : 1
              }}
            >
              {deleting ? '…' : 'Delete'}
            </button>
          </div>
        </div>
      </div>

      {showDelete && (
        <ConfirmDialog
          title="Delete scheduled job?"
          message={`Remove "${job.name}"? This cannot be undone.`}
          confirmLabel={deleting ? 'Deleting…' : 'Delete'}
          danger
          onConfirm={() => void handleDeleteConfirm()}
          onCancel={() => setShowDelete(false)}
        />
      )}
    </>
  )
}
