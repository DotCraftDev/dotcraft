import { useState } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import { useLocale, useT } from '../../contexts/LocaleContext'
import type { AutomationTask, AutomationTaskStatus } from '../../stores/automationsStore'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useCronStore } from '../../stores/cronStore'
import { ConfirmDialog } from '../ui/ConfirmDialog'
import { StatusBadge } from './StatusBadge'

function isTaskDeletable(status: AutomationTaskStatus): boolean {
  return (
    status === 'pending' ||
    status === 'approved' ||
    status === 'rejected' ||
    status === 'failed'
  )
}

function relativeTime(iso: string, locale: AppLocale): string {
  const diff = Date.now() - new Date(iso).getTime()
  const seconds = Math.floor(diff / 1000)
  if (seconds < 60) return translate(locale, 'cron.display.justNow')
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return translate(locale, 'cron.last.minAgo', { n: minutes })
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return translate(locale, 'cron.last.hourAgo', { n: hours })
  const days = Math.floor(hours / 24)
  return translate(locale, 'cron.last.dayAgo', { n: days })
}

function SourceBadgeSource({
  sourceName,
  t
}: {
  sourceName: string
  t: ReturnType<typeof useT>
}): JSX.Element {
  const label = sourceName === 'github' ? t('auto.source.github') : t('auto.source.local')
  return (
    <span
      style={{
        display: 'inline-block',
        padding: '1px 6px',
        borderRadius: '8px',
        backgroundColor: 'var(--bg-tertiary)',
        color: 'var(--text-secondary)',
        fontSize: '11px',
        fontWeight: 500,
        lineHeight: '16px'
      }}
    >
      {label}
    </span>
  )
}

export function TaskCard({ task }: { task: AutomationTask }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const [hovered, setHovered] = useState(false)
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const selectTask = useAutomationsStore((s) => s.selectTask)
  const selectCronJob = useCronStore((s) => s.selectCronJob)
  const deleteTask = useAutomationsStore((s) => s.deleteTask)
  const deletable = isTaskDeletable(task.status)

  function focusThisTask(): void {
    selectCronJob(null)
    selectTask(task.id)
  }

  const actionButton = (() => {
    switch (task.status) {
      case 'awaiting_review':
        return (
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation()
              focusThisTask()
            }}
            style={{
              padding: '4px 12px',
              borderRadius: '6px',
              border: 'none',
              backgroundColor: 'var(--accent)',
              color: '#fff',
              fontSize: '12px',
              fontWeight: 600,
              cursor: 'pointer'
            }}
          >
            {t('auto.task.review')}
          </button>
        )
      case 'agent_running':
      case 'dispatched':
        return (
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation()
              focusThisTask()
            }}
            style={{
              padding: '4px 12px',
              borderRadius: '6px',
              border: '1px solid var(--border-default)',
              backgroundColor: 'transparent',
              color: 'var(--text-secondary)',
              fontSize: '12px',
              fontWeight: 500,
              cursor: 'pointer'
            }}
          >
            {t('auto.task.view')}
          </button>
        )
      case 'approved':
        return (
          <span style={{ fontSize: '12px', color: 'var(--success)', fontWeight: 500 }}>{t('auto.task.done')}</span>
        )
      case 'rejected':
        return (
          <span style={{ fontSize: '12px', color: 'var(--error)', fontWeight: 500 }}>{t('auto.task.rejected')}</span>
        )
      default:
        return null
    }
  })()

  async function handleDeleteConfirm(): Promise<void> {
    setDeleting(true)
    try {
      await deleteTask(task)
      setShowDeleteConfirm(false)
    } finally {
      setDeleting(false)
    }
  }

  return (
    <>
    <div
      role="button"
      tabIndex={0}
      onClick={() => focusThisTask()}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') focusThisTask()
      }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '12px',
        padding: '10px 14px',
        borderRadius: '8px',
        backgroundColor: hovered ? 'var(--bg-secondary)' : 'transparent',
        cursor: 'pointer',
        transition: 'background-color 0.15s'
      }}
    >
      <div style={{ flexShrink: 0 }}>
        <StatusBadge status={task.status} />
      </div>

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
          {task.title}
        </div>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            marginTop: '2px',
            fontSize: '12px',
            color: 'var(--text-tertiary)'
          }}
        >
          <SourceBadgeSource sourceName={task.sourceName} t={t} />
          <span>{relativeTime(task.updatedAt, locale)}</span>
        </div>
      </div>

      <div
        style={{
          flexShrink: 0,
          display: 'flex',
          alignItems: 'center',
          gap: '8px'
        }}
      >
        {deletable && (
          <button
            type="button"
            disabled={deleting}
            onClick={(e) => {
              e.stopPropagation()
              setShowDeleteConfirm(true)
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
            {deleting ? t('auto.deleting') : t('auto.delete')}
          </button>
        )}
        {actionButton}
      </div>
    </div>

    {showDeleteConfirm && (
      <ConfirmDialog
        title={t('auto.task.deleteTitle')}
        message={
          task.threadId ? t('auto.task.deleteWithThread') : t('auto.task.deleteOnly')
        }
        confirmLabel={deleting ? t('auto.deleting') : t('auto.delete')}
        danger
        onConfirm={() => void handleDeleteConfirm()}
        onCancel={() => setShowDeleteConfirm(false)}
      />
    )}
    </>
  )
}
