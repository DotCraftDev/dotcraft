import { useState, useEffect } from 'react'
import { isWorkspaceLockedSwitchError } from '../../shared/workspaceSwitchErrors'
import type { AppLocale } from '../../shared/locales'
import { useLocale, useSetUiLocale, useT } from '../contexts/LocaleContext'
import { DotCraftLogo } from './ui/DotCraftLogo'

interface RecentWorkspace {
  path: string
  name: string
  lastOpenedAt: string
}

/**
 * Full-screen welcome view shown on first launch (no workspace configured).
 * Spec §16.1, M7-1, M7-5
 */
function isLockError(err: unknown): boolean {
  return isWorkspaceLockedSwitchError(err)
}

export function WelcomeScreen(): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const setUiLocale = useSetUiLocale()
  const isMac = window.api.platform === 'darwin'
  const languageSwitcherTop = isMac ? 20 : window.api.titleBarOverlayHeight + 16
  const [recents, setRecents] = useState<RecentWorkspace[]>([])
  const [loading, setLoading] = useState(false)
  const [switchingLocale, setSwitchingLocale] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [lockedPath, setLockedPath] = useState<string | null>(null)
  // shakingPath drives the animation; cleared on animationEnd to allow re-triggering
  const [shakingPath, setShakingPath] = useState<string | null>(null)

  useEffect(() => {
    window.api.workspace.getRecent().then(setRecents).catch(() => {})
  }, [])

  async function handleOpenWorkspace(): Promise<void> {
    const picked = await window.api.workspace.pickFolder()
    if (!picked) return
    setLoading(true)
    setError(null)
    setLockedPath(null)
    try {
      await window.api.workspace.switch(picked)
    } catch (err) {
      if (isLockError(err)) {
        setLockedPath(picked)
        setShakingPath(picked)
      } else {
        setError(err instanceof Error ? err.message : String(err))
      }
      setLoading(false)
    }
  }

  async function handleOpenRecent(path: string): Promise<void> {
    setLoading(true)
    setError(null)
    setLockedPath(null)
    try {
      await window.api.workspace.switch(path)
    } catch (err) {
      if (isLockError(err)) {
        setLockedPath(path)
        setShakingPath(path)
      } else {
        setError(err instanceof Error ? err.message : String(err))
      }
      setLoading(false)
    }
  }

  async function handleLocaleSwitch(nextLocale: AppLocale): Promise<void> {
    if (nextLocale === locale || switchingLocale) return
    setSwitchingLocale(true)
    setUiLocale(nextLocale)
    try {
      await window.api.settings.set({ locale: nextLocale })
    } catch {
      // Ignore locale persistence failures on welcome screen.
    } finally {
      setSwitchingLocale(false)
    }
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100vh',
        background: 'var(--bg-primary)',
        color: 'var(--text-primary)',
        padding: '48px',
        boxSizing: 'border-box'
      }}
    >
      <div
        style={{
          position: 'absolute',
          top: `${languageSwitcherTop}px`,
          right: '20px',
          display: 'flex',
          alignItems: 'center',
          gap: '8px'
        }}
      >
        <span style={{ fontSize: '12px', color: 'var(--text-dimmed)' }}>{t('welcome.language')}</span>
        <div
          style={{
            display: 'inline-flex',
            border: '1px solid var(--border-default)',
            borderRadius: '999px',
            background: 'var(--bg-secondary)',
            overflow: 'hidden'
          }}
        >
          {(
            [
              ['en', 'EN'],
              ['zh-Hans', '中文']
            ] as const
          ).map(([value, label]) => {
            const active = locale === value
            return (
              <button
                key={value}
                type="button"
                onClick={() => {
                  void handleLocaleSwitch(value)
                }}
                disabled={switchingLocale || loading}
                style={{
                  border: 'none',
                  background: active ? 'var(--accent)' : 'transparent',
                  color: active ? 'var(--on-accent)' : 'var(--text-secondary)',
                  padding: '6px 10px',
                  fontSize: '12px',
                  fontWeight: 600,
                  lineHeight: 1,
                  cursor: switchingLocale || loading ? 'default' : 'pointer',
                  opacity: switchingLocale || loading ? 0.7 : 1
                }}
                aria-label={label}
              >
                {label}
              </button>
            )
          })}
        </div>
      </div>

      {/* Logo / title */}
      <DotCraftLogo size={72} style={{ marginBottom: '20px' }} />
      <div style={{ marginBottom: '10px', fontSize: '28px', fontWeight: 700, letterSpacing: '-0.5px' }}>
        {t('app.brandSubtitle')}
      </div>
      <div style={{ fontSize: '15px', color: 'var(--text-secondary)', marginBottom: '40px' }}>
        {t('welcome.tagline')}
      </div>

      {/* Primary action */}
      <button
        onClick={() => { void handleOpenWorkspace() }}
        disabled={loading}
        style={{
          padding: '12px 28px',
          border: 'none',
          borderRadius: '8px',
          background: 'var(--accent)',
          color: '#fff',
          fontSize: '14px',
          fontWeight: 600,
          cursor: loading ? 'default' : 'pointer',
          opacity: loading ? 0.7 : 1,
          marginBottom: '32px'
        }}
        aria-label={t('welcome.openWorkspace')}
      >
        {loading ? t('welcome.opening') : t('welcome.openWorkspace')}
      </button>

      {/* Error */}
      {error && (
        <div
          style={{
            color: 'var(--error)',
            fontSize: '13px',
            marginBottom: '16px',
            maxWidth: '400px',
            textAlign: 'center'
          }}
        >
          {error}
        </div>
      )}

      {/* Recent workspaces */}
      {recents.length > 0 && (
        <div style={{ width: '100%', maxWidth: '420px' }}>
          <div
            style={{
              fontSize: '11px',
              fontWeight: 600,
              color: 'var(--text-dimmed)',
              textTransform: 'uppercase',
              letterSpacing: '0.05em',
              marginBottom: '8px'
            }}
          >
            {t('welcome.recent')}
          </div>
          <div
            style={{
              border: '1px solid var(--border-default)',
              borderRadius: '8px',
              overflow: 'hidden'
            }}
          >
            {recents.map((r, idx) => {
              const isLocked = lockedPath === r.path
              const isShaking = shakingPath === r.path
              return (
                <button
                  key={r.path}
                  onClick={() => { void handleOpenRecent(r.path) }}
                  disabled={loading}
                  style={{
                    display: 'flex',
                    flexDirection: 'column',
                    alignItems: 'flex-start',
                    gap: '2px',
                    width: '100%',
                    padding: '10px 14px',
                    border: 'none',
                    borderBottom: idx < recents.length - 1 ? '1px solid var(--border-default)' : 'none',
                    background: 'var(--bg-secondary)',
                    color: 'var(--text-primary)',
                    cursor: loading ? 'default' : 'pointer',
                    textAlign: 'left',
                    transition: 'background-color 100ms ease',
                    animation: isShaking ? 'shake 0.4s ease' : undefined
                  }}
                  onAnimationEnd={() => {
                    if (isShaking) setShakingPath(null)
                  }}
                  onMouseEnter={(e) => {
                    if (!loading) (e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-tertiary)'
                  }}
                  onMouseLeave={(e) => {
                    (e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-secondary)'
                  }}
                  aria-label={`Open workspace ${r.name}`}
                >
                  <span style={{ fontSize: '13px', fontWeight: 500 }}>{r.name}</span>
                  <span
                    style={{
                      fontSize: '11px',
                      color: 'var(--text-dimmed)',
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                      maxWidth: '100%'
                    }}
                    title={r.path}
                  >
                    {r.path}
                  </span>
                  {isLocked && (
                    <span style={{ fontSize: '11px', color: 'var(--warning)', marginTop: '2px' }}>
                      {t('welcome.alreadyOpen')}
                    </span>
                  )}
                </button>
              )
            })}
          </div>
        </div>
      )}
    </div>
  )
}
