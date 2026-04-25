import { useEffect, useRef } from 'react'
import type { ConversationItem, ApprovalDecision, ApprovalState } from '../../types/conversation'
import { useConversationStore } from '../../stores/conversationStore'
import { Cloud, File, SquareTerminal } from 'lucide-react'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'

interface ApprovalCardProps {
  item: ConversationItem
  /** Called when this card is the active (pending) card — enables keyboard capture */
  isActive: boolean
  /** Ref to focus after decision resolves (input composer textarea) */
  onResolveFocusRef?: React.RefObject<HTMLElement | null>
}

// ---------------------------------------------------------------------------
// Resolved state label helpers
// ---------------------------------------------------------------------------

const RESOLVED_LABELS: Record<ApprovalState, { label: string; color: string } | null> = {
  pending: null,
  accepted: { label: 'Accepted ✓', color: 'var(--success)' },
  acceptedForSession: { label: 'Accepted for Session ✓', color: 'var(--success)' },
  acceptedAlways: { label: 'Always Accepted ✓', color: 'var(--success)' },
  declined: { label: 'Declined ✗', color: 'var(--error)' },
  cancelled: { label: 'Turn Cancelled', color: 'var(--text-dimmed)' },
  timedOut: { label: 'Timed Out', color: 'var(--warning)' }
}

// ---------------------------------------------------------------------------
// Main component
// ---------------------------------------------------------------------------

/**
 * Inline approval card rendered inside the conversation stream.
 *
 * Pending state: interactive card with 5 decision buttons and keyboard shortcuts.
 * Resolved state: compact read-only summary showing the decision outcome.
 *
 * Spec §M5, §10.4, §13
 */
export function ApprovalCard({ item, isActive, onResolveFocusRef }: ApprovalCardProps): JSX.Element {
  const cardRef = useRef<HTMLDivElement>(null)
  const acceptBtnRef = useRef<HTMLButtonElement>(null)
  const onApprovalDecision = useConversationStore((s) => s.onApprovalDecision)
  const pendingApproval = useConversationStore((s) => s.pendingApproval)

  const approvalType = item.approvalType ?? 'shell'
  const typeLabel =
    approvalType === 'shell'
      ? 'Shell Command'
      : approvalType === 'remoteResource'
        ? 'Remote Resource Operation'
        : 'File Operation'
  const TypeIcon =
    approvalType === 'shell'
      ? SquareTerminal
      : approvalType === 'remoteResource'
        ? Cloud
        : File
  const operation = item.approvalOperation ?? ''
  const target = item.approvalTarget ?? ''
  const reason = item.approvalReason ?? ''
  const approvalState = item.approvalState ?? 'pending'
  const isPending = approvalState === 'pending'

  // Focus the Accept button when card becomes active (spec §13.4)
  useEffect(() => {
    if (isActive && isPending) {
      // Small delay to let the DOM update complete
      const id = requestAnimationFrame(() => {
        cardRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
        acceptBtnRef.current?.focus()
      })
      return () => cancelAnimationFrame(id)
    }
  }, [isActive, isPending])

  function sendDecision(decision: ApprovalDecision): void {
    if (!isPending || !pendingApproval) return
    window.api.appServer.sendServerResponse(pendingApproval.bridgeId, { decision })
    onApprovalDecision(decision)
    // Focus will return to input composer after item/approval/resolved arrives,
    // but we schedule a fallback here in case the notification is delayed
    requestAnimationFrame(() => {
      onResolveFocusRef?.current?.focus()
    })
  }

  // Keyboard shortcuts while card is active (spec §13.2)
  function handleKeyDown(e: React.KeyboardEvent): void {
    if (!isPending) return
    // Prevent global shortcuts from firing while approval card is focused
    e.stopPropagation()

    if (e.key === 'Enter' || (e.key === 'a' && !e.shiftKey)) {
      e.preventDefault()
      sendDecision('accept')
    } else if (e.key === 's' || (e.key === 'S' && !e.shiftKey)) {
      e.preventDefault()
      sendDecision('acceptForSession')
    } else if (e.key === 'A' && e.shiftKey) {
      e.preventDefault()
      sendDecision('acceptAlways')
    } else if (e.key === 'd' || e.key === 'D') {
      e.preventDefault()
      sendDecision('decline')
    } else if (e.key === 'Escape') {
      e.preventDefault()
      sendDecision('cancel')
    }
  }

  // ── Resolved / non-pending state ──────────────────────────────────────────
  if (!isPending) {
    const resolved = RESOLVED_LABELS[approvalState]
    return (
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          padding: '6px 10px',
          borderRadius: '6px',
          border: '1px solid var(--border-default)',
          background: 'var(--bg-secondary)',
          fontSize: '12px',
          color: 'var(--text-secondary)'
        }}
      >
        <span style={{ color: 'var(--text-dimmed)', flexShrink: 0 }}>
          <TypeIcon size={16} strokeWidth={1.5} aria-hidden style={{ flexShrink: 0 }} />
        </span>
        <span style={{ fontWeight: 500, color: 'var(--text-primary)' }}>{typeLabel}</span>
        {operation && (
          <>
            <span style={{ color: 'var(--text-dimmed)' }}>—</span>
            <span
              style={{
                fontFamily: 'var(--font-mono)',
                fontSize: '11px',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
                flex: 1
              }}
            >
              {operation}
            </span>
          </>
        )}
        {resolved && (
          <span style={{ color: resolved.color, fontWeight: 500, flexShrink: 0, marginLeft: 'auto' }}>
            {resolved.label}
          </span>
        )}
      </div>
    )
  }

  // ── Pending state (interactive) ────────────────────────────────────────────
  return (
    <div
      ref={cardRef}
      tabIndex={-1}
      onKeyDown={handleKeyDown}
      style={{
        borderRadius: '8px',
        border: '1px solid var(--warning)',
        background: 'var(--bg-secondary)',
        overflow: 'hidden',
        outline: 'none'
      }}
    >
      {/* Header */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          padding: '8px 12px',
          background: 'var(--bg-tertiary)',
          borderBottom: '1px solid var(--border-default)',
          fontSize: '12px',
          color: 'var(--text-dimmed)',
          fontWeight: 500,
          letterSpacing: '0.05em',
          textTransform: 'uppercase'
        }}
      >
        <span style={{ color: 'var(--warning)' }}>⚠</span>
        Approval Required
      </div>

      {/* Content */}
      <div style={{ padding: '12px' }}>
        {/* Type + operation */}
        <div style={{ display: 'flex', alignItems: 'flex-start', gap: '8px', marginBottom: '10px' }}>
          <span style={{ color: 'var(--text-secondary)', marginTop: '1px' }}>
            <TypeIcon size={16} strokeWidth={1.5} aria-hidden style={{ flexShrink: 0 }} />
          </span>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontWeight: 600, color: 'var(--text-primary)', fontSize: '13px', marginBottom: '3px' }}>
              {typeLabel}
            </div>
            {operation && (
              <div
                style={{
                  fontFamily: 'var(--font-mono)',
                  fontSize: '12px',
                  color: 'var(--text-primary)',
                  background: 'var(--bg-primary)',
                  padding: '3px 6px',
                  borderRadius: '3px',
                  wordBreak: 'break-all',
                  marginBottom: '3px'
                }}
              >
                {operation}
              </div>
            )}
            {target && (
              <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', wordBreak: 'break-all' }}>
                {target}
              </div>
            )}
          </div>
        </div>

        {/* Reason */}
        {reason && (
          <div
            style={{
              fontSize: '12px',
              color: 'var(--text-secondary)',
              marginBottom: '12px',
              paddingLeft: '24px'
            }}
          >
            {reason}
          </div>
        )}

        {/* Primary decision buttons */}
        <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', marginBottom: '8px' }}>
          {/* Accept — primary */}
          <ActionTooltip label="Accept" shortcut={ACTION_SHORTCUTS.send} alternateShortcuts={[['A']]} placement="top">
            <button
              ref={acceptBtnRef}
              onClick={() => sendDecision('accept')}
              style={primaryButtonStyle('var(--accent)')}
            >
              Accept
            </button>
          </ActionTooltip>

          {/* Accept for Session — secondary */}
          <ActionTooltip label="Accept for Session" shortcut={['S']} placement="top">
            <button
              onClick={() => sendDecision('acceptForSession')}
              style={secondaryButtonStyle}
            >
              Accept for Session
            </button>
          </ActionTooltip>

          {/* Decline — primary */}
          <ActionTooltip label="Decline" shortcut={['D']} placement="top">
            <button
              onClick={() => sendDecision('decline')}
              style={primaryButtonStyle('var(--error)')}
            >
              Decline
            </button>
          </ActionTooltip>
        </div>

        {/* Text link decisions */}
        <div style={{ display: 'flex', gap: '16px', paddingLeft: '2px' }}>
          <ActionTooltip label="Accept Always" shortcut={['Shift', 'A']} placement="top">
            <button
              onClick={() => sendDecision('acceptAlways')}
              style={textLinkStyle}
            >
              Accept Always
            </button>
          </ActionTooltip>
          <ActionTooltip label="Cancel Turn" shortcut={ACTION_SHORTCUTS.cancel} placement="top">
            <button
              onClick={() => sendDecision('cancel')}
              style={textLinkStyle}
            >
              Cancel Turn
            </button>
          </ActionTooltip>
        </div>

        {/* Keyboard hint */}
        <div style={{ fontSize: '10px', color: 'var(--text-dimmed)', marginTop: '8px' }}>
          Enter/A · S · D · Shift+A · Esc
        </div>
      </div>
    </div>
  )
}

// ---------------------------------------------------------------------------
// Style helpers
// ---------------------------------------------------------------------------

function primaryButtonStyle(color: string): React.CSSProperties {
  return {
    padding: '5px 12px',
    borderRadius: '5px',
    border: `1px solid ${color}`,
    background: color,
    color: '#fff',
    fontSize: '12px',
    fontWeight: 600,
    cursor: 'pointer',
    transition: 'opacity 150ms ease'
  }
}

const secondaryButtonStyle: React.CSSProperties = {
  padding: '5px 12px',
  borderRadius: '5px',
  border: '1px solid var(--border-active)',
  background: 'var(--bg-tertiary)',
  color: 'var(--text-primary)',
  fontSize: '12px',
  fontWeight: 500,
  cursor: 'pointer',
  transition: 'background 150ms ease'
}

const textLinkStyle: React.CSSProperties = {
  background: 'none',
  border: 'none',
  padding: 0,
  color: 'var(--text-secondary)',
  fontSize: '12px',
  cursor: 'pointer',
  textDecoration: 'underline',
  textDecorationColor: 'var(--text-dimmed)'
}
