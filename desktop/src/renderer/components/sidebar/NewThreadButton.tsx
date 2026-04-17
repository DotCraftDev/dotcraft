import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useUIStore } from '../../stores/uiStore'

/**
 * Primary action button that opens welcome composer for a new chat.
 * Keyboard shortcut Ctrl+N is registered globally in App.tsx.
 * Spec §9.3
 */
export function NewThreadButton(): JSX.Element {
  const t = useT()
  const status = useConnectionStore((s) => s.status)
  const goToNewChat = useUIStore((s) => s.goToNewChat)

  const isConnected = status === 'connected'

  function handleClick(): void {
    if (!isConnected) return
    goToNewChat()
  }

  return (
    <div style={{ padding: '8px 12px 10px', flexShrink: 0 }}>
      <button
        onClick={handleClick}
        disabled={!isConnected}
        title={t('sidebar.newThread')}
        aria-label={t('sidebar.newThread')}
        style={{
          width: '100%',
          padding: '6px 12px',
          backgroundColor: isConnected ? 'var(--accent)' : 'var(--bg-tertiary)',
          color: isConnected ? '#ffffff' : 'var(--text-dimmed)',
          border: 'none',
          borderRadius: '6px',
          fontSize: '13px',
          fontWeight: 500,
          cursor: isConnected ? 'pointer' : 'default',
          display: 'flex',
          alignItems: 'center',
          gap: '6px',
          transition: 'background-color 150ms ease'
        }}
        onMouseEnter={(e) => {
          if (isConnected) {
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--accent-hover)'
          }
        }}
        onMouseLeave={(e) => {
          if (isConnected) {
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--accent)'
          }
        }}
      >
        <span aria-hidden="true">+</span>
        {t('sidebar.newThreadLabel')}
      </button>
    </div>
  )
}
