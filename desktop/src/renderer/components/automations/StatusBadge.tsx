import { useT } from '../../contexts/LocaleContext'
import type { MessageKey } from '../../../shared/locales'
import type { AutomationTaskStatus } from '../../stores/automationsStore'

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

function ClockIcon(): JSX.Element {
  return (
    <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5">
      <circle cx="8" cy="8" r="6.5" />
      <path d="M8 4.5V8l2.5 1.5" />
    </svg>
  )
}

function ArrowRightIcon(): JSX.Element {
  return (
    <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5">
      <path d="M3 8h10M9 4l4 4-4 4" />
    </svg>
  )
}

function SpinnerIcon(): JSX.Element {
  return (
    <>
      <style>{spinnerKeyframes}</style>
      <svg
        width="14"
        height="14"
        viewBox="0 0 16 16"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.5"
        style={{ animation: 'automations-spin 1s linear infinite' }}
      >
        <path d="M8 1.5A6.5 6.5 0 1 1 1.5 8" />
      </svg>
    </>
  )
}

function CheckOutlineIcon(): JSX.Element {
  return (
    <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5">
      <circle cx="8" cy="8" r="6.5" />
      <path d="M5.5 8l2 2 3.5-4" />
    </svg>
  )
}

function EyeIcon(): JSX.Element {
  return (
    <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5">
      <path d="M1 8s2.5-5 7-5 7 5 7 5-2.5 5-7 5-7-5-7-5z" />
      <circle cx="8" cy="8" r="2" />
    </svg>
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

function XIcon(): JSX.Element {
  return (
    <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5">
      <circle cx="8" cy="8" r="6.5" />
      <path d="M5.5 5.5l5 5M10.5 5.5l-5 5" />
    </svg>
  )
}

function WarningIcon(): JSX.Element {
  return (
    <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5">
      <path d="M8 1.5L1 14h14L8 1.5z" />
      <path d="M8 6v4M8 12v.5" />
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
  pending: { color: 'var(--text-tertiary)', icon: <ClockIcon /> },
  dispatched: { color: 'var(--accent)', icon: <ArrowRightIcon /> },
  agent_running: { color: 'var(--accent)', icon: <SpinnerIcon /> },
  agent_completed: { color: 'var(--accent)', icon: <CheckOutlineIcon /> },
  awaiting_review: { color: 'var(--warning)', icon: <EyeIcon /> },
  approved: { color: 'var(--success)', icon: <CheckFilledIcon /> },
  rejected: { color: 'var(--error)', icon: <XIcon /> },
  failed: { color: 'var(--error)', icon: <WarningIcon /> }
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
