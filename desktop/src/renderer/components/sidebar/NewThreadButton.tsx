import { useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useUIStore } from '../../stores/uiStore'
import { ShortcutBadge } from '../ui/ShortcutBadge'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'

/**
 * Primary action button that opens welcome composer for a new chat.
 * Keyboard shortcut Ctrl+N is registered globally in App.tsx.
 * Spec §9.3
 */
export function NewThreadButton(): JSX.Element {
  const t = useT()
  const status = useConnectionStore((s) => s.status)
  const goToNewChat = useUIStore((s) => s.goToNewChat)
  const [active, setActive] = useState(false)

  const isConnected = status === 'connected'
  const showShortcut = isConnected && active

  function handleClick(): void {
    if (!isConnected) return
    goToNewChat()
  }

  return (
    <div style={{ padding: '8px 12px 10px', flexShrink: 0 }}>
      <button
        onClick={handleClick}
        disabled={!isConnected}
        aria-label={t('sidebar.newThread')}
        onFocus={() => setActive(true)}
        onBlur={() => setActive(false)}
        style={{
          width: '100%',
          padding: '6px 12px',
          backgroundColor: isConnected ? 'var(--accent)' : 'var(--bg-tertiary)',
          color: isConnected ? 'var(--on-accent)' : 'var(--text-dimmed)',
          border: 'none',
          borderRadius: '6px',
          fontSize: '13px',
          fontWeight: 500,
          cursor: isConnected ? 'pointer' : 'default',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: '6px',
          transition: 'background-color 150ms ease'
        }}
        onMouseEnter={(e) => {
          setActive(true)
          if (isConnected) {
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--accent-hover)'
          }
        }}
        onMouseLeave={(e) => {
          setActive(false)
          if (isConnected) {
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--accent)'
          }
        }}
      >
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', minWidth: 0 }}>
          <span aria-hidden="true">+</span>
          <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {t('sidebar.newThreadLabel')}
          </span>
        </span>
        {showShortcut && (
          <ShortcutBadge
            shortcut={ACTION_SHORTCUTS.newThread}
            tone="onAccent"
          />
        )}
      </button>
    </div>
  )
}
