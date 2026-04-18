import { useState } from 'react'
import { Check, Copy } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'

interface MessageCopyButtonProps {
  getText: () => string
  visible: boolean
  disabled?: boolean
  ariaLabel?: string
}

/**
 * Small reusable copy button for message bubbles.
 * Uses a Copy -> Check icon transition after successful copy.
 */
export function MessageCopyButton({
  getText,
  visible,
  disabled = false,
  ariaLabel
}: MessageCopyButtonProps): JSX.Element | null {
  const t = useT()
  const [copied, setCopied] = useState(false)

  if (disabled) return null

  async function handleCopy(): Promise<void> {
    const text = getText()
    if (text.length === 0) return
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      addToast(t('toast.copied'), 'success', 2000)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      // Ignore clipboard failures silently.
    }
  }

  return (
    <button
      type="button"
      onClick={() => {
        void handleCopy()
      }}
      aria-label={ariaLabel ?? t('conversation.copyMessage')}
      title={ariaLabel ?? t('conversation.copyMessage')}
      style={{
        position: 'absolute',
        right: '8px',
        bottom: '6px',
        width: '24px',
        height: '24px',
        borderRadius: '6px',
        border: '1px solid var(--border-default)',
        background: 'var(--bg-secondary)',
        color: copied ? 'var(--success)' : 'var(--text-secondary)',
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        cursor: 'pointer',
        opacity: visible ? 1 : 0,
        pointerEvents: visible ? 'auto' : 'none',
        transition: 'opacity 120ms ease, color 120ms ease',
        zIndex: 2
      }}
    >
      {copied ? <Check size={14} aria-hidden /> : <Copy size={14} aria-hidden />}
    </button>
  )
}
