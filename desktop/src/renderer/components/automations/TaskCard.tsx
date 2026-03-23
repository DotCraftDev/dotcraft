import { useState } from 'react'
import type { AutomationTask } from '../../stores/automationsStore'
import { useAutomationsStore } from '../../stores/automationsStore'
import { StatusBadge } from './StatusBadge'

function relativeTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime()
  const seconds = Math.floor(diff / 1000)
  if (seconds < 60) return 'just now'
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  return `${days}d ago`
}

function SourceBadge({ sourceName }: { sourceName: string }): JSX.Element {
  const label = sourceName === 'github' ? 'GitHub' : 'Local'
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
  const [hovered, setHovered] = useState(false)
  const selectTask = useAutomationsStore((s) => s.selectTask)

  const actionButton = (() => {
    switch (task.status) {
      case 'awaiting_review':
        return (
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation()
              selectTask(task.id)
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
            Review
          </button>
        )
      case 'agent_running':
      case 'dispatched':
        return (
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation()
              selectTask(task.id)
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
            View
          </button>
        )
      case 'approved':
        return (
          <span style={{ fontSize: '12px', color: 'var(--success)', fontWeight: 500 }}>Done</span>
        )
      case 'rejected':
        return (
          <span style={{ fontSize: '12px', color: 'var(--error)', fontWeight: 500 }}>Rejected</span>
        )
      default:
        return null
    }
  })()

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={() => selectTask(task.id)}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') selectTask(task.id)
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
          <SourceBadge sourceName={task.sourceName} />
          <span>{relativeTime(task.updatedAt)}</span>
        </div>
      </div>

      <div style={{ flexShrink: 0 }}>{actionButton}</div>
    </div>
  )
}
