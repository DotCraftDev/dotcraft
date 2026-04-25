import type { CSSProperties, DragEventHandler, JSX, ReactNode } from 'react'
import { Square } from 'lucide-react'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { ShortcutSpec } from '../ui/shortcutKeys'

type ComposerActionButtonTone = 'enabled' | 'disabled'

interface ComposerShellProps {
  dragOver: boolean
  dropLabel: string
  attachmentStrip?: ReactNode
  editor: ReactNode
  footerLeading: ReactNode
  footerAction: ReactNode
  onDragOver: DragEventHandler<HTMLDivElement>
  onDragLeave: DragEventHandler<HTMLDivElement>
  onDrop: DragEventHandler<HTMLDivElement>
  opacity?: number
  focused?: boolean
}

interface ComposerModeSwitchProps {
  value: 'agent' | 'plan'
  onToggle: () => void
  agentLabel: string
  planLabel: string
  title?: string
  shortcut?: ShortcutSpec
}

export function ComposerShell({
  dragOver,
  dropLabel,
  attachmentStrip,
  editor,
  footerLeading,
  footerAction,
  onDragOver,
  onDragLeave,
  onDrop,
  opacity = 1,
  focused = false
}: ComposerShellProps): JSX.Element {
  return (
    <div
      style={{
        padding: '14px 14px',
        display: 'flex',
        flexDirection: 'column',
        gap: '6px',
        opacity
      }}
    >
      <div
        style={{
          position: 'relative',
          border: focused ? '1px solid var(--border-active)' : '1px solid var(--border-default)',
          borderRadius: '20px',
          background: 'color-mix(in srgb, var(--bg-secondary) 92%, var(--bg-primary))',
          padding: '10px 10px 8px',
          boxShadow: focused
            ? '0 0 0 1px color-mix(in srgb, var(--border-active) 36%, transparent), 0 12px 28px rgba(0, 0, 0, 0.18)'
            : '0 10px 24px rgba(0, 0, 0, 0.16)'
        }}
        onDragOver={onDragOver}
        onDragLeave={onDragLeave}
        onDrop={onDrop}
      >
        {dragOver && (
          <div
            style={{
              position: 'absolute',
              inset: 0,
              zIndex: 20,
              border: '2px dashed var(--accent)',
              borderRadius: '10px',
              background: 'rgba(124, 58, 237, 0.08)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              pointerEvents: 'none',
              fontSize: '13px',
              color: 'var(--accent)'
            }}
          >
            {dropLabel}
          </div>
        )}

        {attachmentStrip}
        {editor}

        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: '10px',
            marginTop: '8px',
            paddingTop: '6px'
          }}
        >
          {footerLeading}
          {footerAction}
        </div>
      </div>
    </div>
  )
}

export function ComposerModeSwitch({
  value,
  onToggle,
  agentLabel,
  planLabel,
  title,
  shortcut
}: ComposerModeSwitchProps): JSX.Element {
  const activeLabel = value === 'agent' ? agentLabel : planLabel
  const activeTone = value === 'agent' ? 'var(--success)' : 'var(--info)'

  return (
    <ActionTooltip label={title ?? activeLabel} shortcut={shortcut} placement="top">
      <button
        type="button"
        onClick={onToggle}
        aria-label={activeLabel}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '6px',
          padding: '2px 6px',
          borderRadius: '8px',
          border: 'none',
          background: 'transparent',
          color: 'var(--text-secondary)',
          cursor: 'pointer',
          fontSize: '12px',
          fontWeight: 600,
          outline: 'none'
        }}
    >
        <span
          aria-hidden
          style={{
            width: '7px',
            height: '7px',
            borderRadius: '999px',
            background: activeTone,
            flexShrink: 0
          }}
        />
        {activeLabel}
      </button>
    </ActionTooltip>
  )
}

export function composerModelPillStyle(color: string, disabled = false): CSSProperties {
  return {
    fontSize: '12px',
    fontWeight: 600,
    color,
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    maxWidth: '220px',
    height: '22px',
    borderRadius: '999px',
    border: 'none',
    backgroundColor: 'transparent',
    padding: '0 4px',
    outline: 'none',
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    opacity: disabled ? 0.72 : 1,
    boxShadow: 'none'
  }
}

export const composerActionButtonStyle: CSSProperties = {
  width: '32px',
  height: '32px',
  borderRadius: '999px',
  border: 'none',
  flexShrink: 0,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  cursor: 'pointer',
  boxShadow: '0 4px 10px rgba(0, 0, 0, 0.16)',
  transition: 'background-color 100ms ease, transform 100ms ease'
}

export function composerSendButtonStyle(tone: ComposerActionButtonTone): CSSProperties {
  const enabled = tone === 'enabled'

  return {
    ...composerActionButtonStyle,
    backgroundColor: enabled ? '#f5f6f7' : 'color-mix(in srgb, var(--bg-primary) 92%, #ffffff 8%)',
    color: enabled ? '#1f2328' : 'var(--text-dimmed)',
    cursor: enabled ? 'pointer' : 'default'
  }
}

export function SendIcon(): JSX.Element {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M12 19a1.25 1.25 0 0 1-1.25-1.25v-8.03l-3.1 3.1a1.25 1.25 0 1 1-1.77-1.77l5.24-5.24a1.25 1.25 0 0 1 1.76 0l5.24 5.24a1.25 1.25 0 1 1-1.77 1.77l-3.1-3.1v8.03A1.25 1.25 0 0 1 12 19Z" />
    </svg>
  )
}

export function StopIcon(): JSX.Element {
  return <Square size={12} strokeWidth={0} fill="currentColor" aria-hidden="true" />
}
