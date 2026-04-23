import { useMemo, useState } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import { useLocale, useT } from '../../contexts/LocaleContext'
import type {
  AutomationSchedule,
  AutomationTask,
  AutomationTaskStatus
} from '../../stores/automationsStore'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useCronStore } from '../../stores/cronStore'
import { useThreadStore } from '../../stores/threadStore'
import { ConfirmDialog } from '../ui/ConfirmDialog'
import { StatusBadge } from './StatusBadge'

/** MIME key for drag-and-drop binding a task to a thread entry. */
export const AUTOMATION_TASK_DRAG_MIME = 'application/x-dotcraft-automation-task'

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

function formatSchedule(s: AutomationSchedule | null | undefined, t: ReturnType<typeof useT>): string | null {
  if (!s) return null
  if (s.kind === 'daily') {
    const h = String(s.dailyHour ?? 9).padStart(2, '0')
    const m = String(s.dailyMinute ?? 0).padStart(2, '0')
    return t('auto.taskCard.scheduleDaily', { time: `${h}:${m}` })
  }
  if (s.kind === 'every') {
    const ms = s.everyMs ?? 0
    const HOUR = 60 * 60 * 1000
    if (ms === HOUR) return t('auto.taskCard.scheduleHourly')
    if (ms === 7 * 24 * HOUR) return t('auto.taskCard.scheduleWeekly')
    const minutes = Math.round(ms / 60_000)
    return t('auto.taskCard.scheduleEvery', { interval: `${minutes}m` })
  }
  return null
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
  const threadList = useThreadStore((s) => s.threadList)
  const deletable = isTaskDeletable(task.status)
  const draggable = task.sourceName === 'local'

  const boundThreadName = useMemo(() => {
    const id = task.threadBinding?.threadId
    if (!id) return null
    const m = threadList.find((th) => th.id === id)
    return m?.displayName ?? id
  }, [task.threadBinding, threadList])

  const scheduleText = useMemo(() => formatSchedule(task.schedule, t), [task.schedule, t])

  function handleDragStart(e: React.DragEvent): void {
    e.dataTransfer.setData(AUTOMATION_TASK_DRAG_MIME, `${task.sourceName}::${task.id}`)
    // Provide a human-readable label for targets that surface it via effectAllowed.
    e.dataTransfer.setData('text/plain', task.title)
    e.dataTransfer.effectAllowed = 'link'
  }

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
      draggable={draggable}
      onDragStart={draggable ? handleDragStart : undefined}
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
            color: 'var(--text-tertiary)',
            flexWrap: 'wrap'
          }}
        >
          <SourceBadgeSource sourceName={task.sourceName} t={t} />
          <span>{relativeTime(task.updatedAt, locale)}</span>
          {boundThreadName && (
            <span
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: '3px',
                padding: '1px 6px',
                borderRadius: '8px',
                backgroundColor: 'color-mix(in srgb, var(--accent) 12%, transparent)',
                color: 'var(--accent)',
                fontSize: '11px',
                fontWeight: 500
              }}
              title={t('auto.taskCard.bound', { name: boundThreadName })}
            >
              💬 {boundThreadName}
            </span>
          )}
          {scheduleText && (
            <span
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                padding: '1px 6px',
                borderRadius: '8px',
                backgroundColor: 'var(--bg-tertiary)',
                color: 'var(--text-secondary)',
                fontSize: '11px',
                fontWeight: 500
              }}
            >
              ⏰ {scheduleText}
            </span>
          )}
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
