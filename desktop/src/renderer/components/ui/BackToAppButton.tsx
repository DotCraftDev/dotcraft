import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { ArrowLeft } from 'lucide-react'

interface BackToAppButtonProps {
  onClick?: () => void
  labelKey?: string
}

/**
 * Reusable navigation affordance for secondary center-panel views.
 * Falls back to returning to the main conversation view.
 */
export function BackToAppButton({ onClick, labelKey = 'common.backToApp' }: BackToAppButtonProps): JSX.Element {
  const t = useT()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)

  function handleClick(): void {
    if (onClick) {
      onClick()
      return
    }
    setActiveMainView('conversation')
  }

  return (
    <button
      type="button"
      onClick={handleClick}
      title={t(labelKey)}
      aria-label={t(labelKey)}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: '40px',
        height: '40px',
        borderRadius: '6px',
        border: '1px solid transparent',
        background: 'transparent',
        color: 'var(--text-secondary)',
        cursor: 'pointer',
        lineHeight: 1,
        transition: 'background-color 120ms ease, border-color 120ms ease, color 120ms ease'
      }}
      onMouseEnter={(e) => {
        e.currentTarget.style.backgroundColor = 'var(--bg-tertiary)'
        e.currentTarget.style.borderColor = 'var(--border-default)'
        e.currentTarget.style.color = 'var(--text-primary)'
      }}
      onMouseLeave={(e) => {
        e.currentTarget.style.backgroundColor = 'transparent'
        e.currentTarget.style.borderColor = 'transparent'
        e.currentTarget.style.color = 'var(--text-secondary)'
      }}
    >
      <ArrowLeft size={22} strokeWidth={2.2} aria-hidden="true" />
    </button>
  )
}
