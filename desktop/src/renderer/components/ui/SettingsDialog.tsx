import { useState, useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { addToast } from '../../stores/toastStore'
import { applyTheme, resolveTheme, type ThemeMode } from '../../utils/theme'

interface SettingsDialogProps {
  onClose: () => void
}

/**
 * Settings modal dialog.
 * Allows configuring AppServer binary path and displays app info.
 * Spec M7-7, §17.1 (Ctrl+,)
 */
export function SettingsDialog({ onClose }: SettingsDialogProps): JSX.Element {
  const [binaryPath, setBinaryPath] = useState('')
  const [theme, setTheme] = useState<ThemeMode>('dark')
  const [version, setVersion] = useState('')
  const [saving, setSaving] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    inputRef.current?.focus()
    window.api.settings
      .get()
      .then((s) => {
        setBinaryPath(s.appServerBinaryPath ?? '')
        setTheme(resolveTheme(s.theme))
      })
      .catch(() => {})
    setVersion(typeof process !== 'undefined' ? (process.env.npm_package_version ?? '0.1.0') : '0.1.0')

    function handleKeyDown(e: KeyboardEvent): void {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  async function handleThemeChange(next: ThemeMode): Promise<void> {
    setTheme(next)
    applyTheme(next)
    try {
      await window.api.settings.set({ theme: next })
    } catch (err) {
      addToast(`Failed to save theme: ${err instanceof Error ? err.message : String(err)}`, 'error')
    }
  }

  async function handleSave(): Promise<void> {
    setSaving(true)
    try {
      await window.api.settings.set({ appServerBinaryPath: binaryPath.trim() || undefined })
      addToast('Settings saved', 'success')
      onClose()
    } catch (err) {
      addToast(`Failed to save settings: ${err instanceof Error ? err.message : String(err)}`, 'error')
    } finally {
      setSaving(false)
    }
  }

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Settings"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 20000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'var(--overlay-scrim)'
      }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose()
      }}
    >
      <div
        style={{
          backgroundColor: 'var(--bg-secondary)',
          borderRadius: '10px',
          boxShadow: 'var(--shadow-level-3)',
          padding: '24px',
          width: '420px',
          maxWidth: 'calc(100vw - 48px)'
        }}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <h2
          style={{
            margin: '0 0 20px',
            fontSize: '15px',
            fontWeight: 600,
            color: 'var(--text-primary)'
          }}
        >
          Settings
        </h2>

        {/* AppServer binary path */}
        <div style={{ marginBottom: '16px' }}>
          <label
            htmlFor="settings-binary-path"
            style={{
              display: 'block',
              fontSize: '12px',
              fontWeight: 500,
              color: 'var(--text-secondary)',
              marginBottom: '6px'
            }}
          >
            AppServer binary path
          </label>
          <input
            id="settings-binary-path"
            ref={inputRef}
            type="text"
            value={binaryPath}
            onChange={(e) => setBinaryPath(e.target.value)}
            placeholder="Leave empty to use dotcraft from PATH"
            style={{
              width: '100%',
              boxSizing: 'border-box',
              padding: '7px 10px',
              fontSize: '13px',
              borderRadius: '6px',
              border: '1px solid var(--border-default)',
              background: 'var(--bg-primary)',
              color: 'var(--text-primary)',
              outline: 'none',
              fontFamily: 'var(--font-mono)'
            }}
            onFocus={(e) => { e.currentTarget.style.borderColor = 'var(--border-active)' }}
            onBlur={(e) => { e.currentTarget.style.borderColor = 'var(--border-default)' }}
          />
          <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
            Override the default dotcraft binary location.
          </div>
        </div>

        <div style={{ marginBottom: '16px' }}>
          <label
            htmlFor="settings-theme"
            style={{
              display: 'block',
              fontSize: '12px',
              fontWeight: 500,
              color: 'var(--text-secondary)',
              marginBottom: '6px'
            }}
          >
            Theme
          </label>
          <select
            id="settings-theme"
            value={theme}
            onChange={(e) => { void handleThemeChange(e.target.value as ThemeMode) }}
            style={{
              padding: '7px 10px',
              fontSize: '13px',
              borderRadius: '6px',
              border: '1px solid var(--border-default)',
              background: 'var(--bg-primary)',
              color: 'var(--text-primary)',
              cursor: 'pointer',
              width: '180px'
            }}
          >
            <option value="dark">Dark (default)</option>
            <option value="light">Light</option>
          </select>
        </div>

        <div
          style={{
            fontSize: '12px',
            color: 'var(--text-dimmed)',
            marginBottom: '20px'
          }}
        >
          DotCraft Desktop v{version}
        </div>

        <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
          <button
            onClick={onClose}
            style={{
              padding: '7px 16px',
              border: '1px solid var(--border-default)',
              borderRadius: '6px',
              backgroundColor: 'transparent',
              color: 'var(--text-primary)',
              fontSize: '13px',
              cursor: 'pointer'
            }}
          >
            Cancel
          </button>
          <button
            onClick={() => { void handleSave() }}
            disabled={saving}
            style={{
              padding: '7px 16px',
              border: 'none',
              borderRadius: '6px',
              backgroundColor: 'var(--accent)',
              color: 'var(--on-accent)',
              fontSize: '13px',
              fontWeight: 500,
              cursor: saving ? 'default' : 'pointer',
              opacity: saving ? 0.7 : 1
            }}
          >
            {saving ? 'Saving...' : 'Save'}
          </button>
        </div>
      </div>
    </div>
  )

  return createPortal(dialog, document.body) as JSX.Element
}
