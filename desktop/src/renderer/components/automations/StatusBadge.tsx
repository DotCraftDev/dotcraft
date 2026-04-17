import { useT } from '../../contexts/LocaleContext'
import type { MessageKey } from '../../../shared/locales'
import type { AutomationTaskStatus } from '../../stores/automationsStore'
import { ArrowRight, CircleCheck, CircleX, Clock, Eye, Loader2, TriangleAlert } from 'lucide-react'

interface StatusConfig {
  label: string
  color: string
  icon: JSX.Element
}

const spinnerKeyframes = `
@keyframes automations-spin {
  to { transform: rotate(360deg); }
}
`

function SpinnerIcon(): JSX.Element {
  return (
    <>
      <style>{spinnerKeyframes}</style>
      <Loader2
        size={14}
        strokeWidth={1.5}
        aria-hidden
        style={{ animation: 'automations-spin 1s linear infinite' }}
      />
    </>
  )
}

function CheckFilledIcon(): JSX.Element {
  return (
    <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor">
      <circle cx="8" cy="8" r="7" />
      <path d="M5.5 8l2 2 3.5-4" stroke="var(--bg-primary)" strokeWidth="1.5" fill="none" />
    </svg>
  )
}

const STATUS_LABEL_KEY: Record<AutomationTaskStatus, MessageKey> = {
  pending: 'status.pending',
  dispatched: 'status.dispatched',
  agent_running: 'status.running',
  agent_completed: 'status.agentCompleted',
  awaiting_review: 'status.review',
  approved: 'status.approved',
  rejected: 'status.rejected',
  failed: 'status.failed'
}

const statusMap: Record<AutomationTaskStatus, Omit<StatusConfig, 'label'>> = {
  pending: { color: 'var(--text-tertiary)', icon: <Clock size={14} strokeWidth={1.5} aria-hidden /> },
  dispatched: { color: 'var(--accent)', icon: <ArrowRight size={14} strokeWidth={1.5} aria-hidden /> },
  agent_running: { color: 'var(--accent)', icon: <SpinnerIcon /> },
  agent_completed: { color: 'var(--accent)', icon: <CircleCheck size={14} strokeWidth={1.5} aria-hidden /> },
  awaiting_review: { color: 'var(--warning)', icon: <Eye size={14} strokeWidth={1.5} aria-hidden /> },
  approved: { color: 'var(--success)', icon: <CheckFilledIcon /> },
  rejected: { color: 'var(--error)', icon: <CircleX size={14} strokeWidth={1.5} aria-hidden /> },
  failed: { color: 'var(--error)', icon: <TriangleAlert size={14} strokeWidth={1.5} aria-hidden /> }
}

export function StatusBadge({ status }: { status: AutomationTaskStatus }): JSX.Element {
  const t = useT()
  const cfg = statusMap[status] ?? statusMap.pending
  const labelKey = STATUS_LABEL_KEY[status] ?? STATUS_LABEL_KEY.pending
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '4px',
        color: cfg.color,
        fontSize: '12px',
        fontWeight: 500
      }}
    >
      {cfg.icon}
      {t(labelKey)}
    </span>
  )
}

export function getStatusColor(status: AutomationTaskStatus): string {
  return (statusMap[status] ?? statusMap.pending).color
}
