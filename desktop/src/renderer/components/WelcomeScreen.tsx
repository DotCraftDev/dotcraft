import { useState, useEffect } from 'react'
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
export function WelcomeScreen(): JSX.Element {
  const [recents, setRecents] = useState<RecentWorkspace[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    window.api.workspace.getRecent().then(setRecents).catch(() => {})
  }, [])

  async function handleOpenWorkspace(): Promise<void> {
    const picked = await window.api.workspace.pickFolder()
    if (!picked) return
    setLoading(true)
    setError(null)
    try {
      await window.api.workspace.switch(picked)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setLoading(false)
    }
  }

  async function handleOpenRecent(path: string): Promise<void> {
    setLoading(true)
    setError(null)
    try {
      await window.api.workspace.switch(path)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setLoading(false)
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
      {/* Logo / title */}
      <DotCraftLogo size={72} style={{ marginBottom: '20px' }} />
      <div style={{ marginBottom: '10px', fontSize: '28px', fontWeight: 700, letterSpacing: '-0.5px' }}>
        DotCraft Desktop
      </div>
      <div style={{ fontSize: '15px', color: 'var(--text-secondary)', marginBottom: '40px' }}>
        Select a workspace folder to get started.
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
        aria-label="Open workspace folder"
      >
        {loading ? 'Opening...' : 'Open Workspace'}
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
            Recent
          </div>
          <div
            style={{
              border: '1px solid var(--border-default)',
              borderRadius: '8px',
              overflow: 'hidden'
            }}
          >
            {recents.map((r, idx) => (
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
                  transition: 'background-color 100ms ease'
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
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
