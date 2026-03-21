import { useState, useEffect, useRef } from 'react'

interface RecentWorkspace {
  path: string
  name: string
  lastOpenedAt: string
}

interface WorkspaceHeaderProps {
  workspaceName: string
  workspacePath: string
}

/**
 * Workspace identity header at the top of the sidebar.
 * Shows workspace name and path; click opens a dropdown menu.
 * Spec §9.2, M7-2, M7-3, M7-5
 */
export function WorkspaceHeader({ workspaceName, workspacePath }: WorkspaceHeaderProps): JSX.Element {
  const [open, setOpen] = useState(false)
  const [recents, setRecents] = useState<RecentWorkspace[]>([])
  const [showRecents, setShowRecents] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    // Fetch recents when dropdown opens
    window.api.workspace.getRecent().then((list) => {
      // Exclude the current workspace from the recents submenu
      setRecents(list.filter((r) => r.path !== workspacePath))
    }).catch(() => {})

    function handleClick(e: MouseEvent): void {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false)
        setShowRecents(false)
      }
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [open, workspacePath])

  function openInExplorer(): void {
    setOpen(false)
    void window.api.shell.openPath(workspacePath)
  }

  async function switchWorkspace(): Promise<void> {
    setOpen(false)
    const picked = await window.api.workspace.pickFolder()
    if (picked) {
      await window.api.workspace.switch(picked)
    }
  }

  async function switchToRecent(path: string): Promise<void> {
    setOpen(false)
    setShowRecents(false)
    await window.api.workspace.switch(path)
  }

  return (
    <div
      ref={ref}
      style={{ position: 'relative', padding: '12px 16px', borderBottom: '1px solid var(--border-default)', flexShrink: 0, cursor: 'pointer' }}
      onClick={() => { setOpen((v) => !v); if (open) setShowRecents(false) }}
      title="Workspace options"
    >
      {/* Workspace name */}
      <div
        style={{
          fontWeight: 600,
          fontSize: '14px',
          color: 'var(--text-primary)',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
          paddingRight: '16px'
        }}
      >
        {workspaceName || 'DotCraft'}
      </div>

      {/* Workspace path */}
      <div
        style={{
          fontSize: '11px',
          color: 'var(--text-dimmed)',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
          marginTop: '2px'
        }}
        title={workspacePath}
      >
        {workspacePath}
      </div>

      {/* Chevron indicator */}
      <span
        style={{
          position: 'absolute',
          right: '12px',
          top: '50%',
          transform: `translateY(-50%) rotate(${open ? '180deg' : '0deg'})`,
          transition: 'transform 150ms ease',
          color: 'var(--text-dimmed)',
          fontSize: '10px',
          pointerEvents: 'none'
        }}
        aria-hidden="true"
      >
        ▾
      </span>

      {/* Dropdown */}
      {open && (
        <div
          style={{
            position: 'absolute',
            top: '100%',
            left: '8px',
            right: '8px',
            backgroundColor: 'var(--bg-secondary)',
            border: '1px solid var(--border-default)',
            borderRadius: '6px',
            boxShadow: 'var(--shadow-level-2)',
            zIndex: 100,
            overflow: 'visible'
          }}
          onClick={(e) => e.stopPropagation()}
        >
          <DropdownItem label="Open in Explorer" onClick={openInExplorer} />
          <DropdownItem label="Switch Workspace" onClick={() => { void switchWorkspace() }} />
          {/* Recent workspaces submenu */}
          <div
            style={{ position: 'relative' }}
            onMouseEnter={() => setShowRecents(true)}
            onMouseLeave={() => setShowRecents(false)}
          >
            <DropdownItem
              label="Recent Workspaces"
              disabled={recents.length === 0}
              hasSubmenu={recents.length > 0}
            />
            {showRecents && recents.length > 0 && (
              <div
                style={{
                  position: 'absolute',
                  top: 0,
                  left: '100%',
                  backgroundColor: 'var(--bg-secondary)',
                  border: '1px solid var(--border-default)',
                  borderRadius: '6px',
                  boxShadow: 'var(--shadow-level-2)',
                  zIndex: 101,
                  minWidth: '220px',
                  maxWidth: '320px',
                  maxHeight: '300px',
                  overflowY: 'auto'
                }}
              >
                {recents.map((r) => (
                  <button
                    key={r.path}
                    onClick={() => { void switchToRecent(r.path) }}
                    style={{
                      display: 'flex',
                      flexDirection: 'column',
                      alignItems: 'flex-start',
                      width: '100%',
                      padding: '7px 14px',
                      border: 'none',
                      background: 'transparent',
                      color: 'var(--text-primary)',
                      cursor: 'pointer',
                      textAlign: 'left',
                      gap: '1px'
                    }}
                    onMouseEnter={(e) => {
                      (e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-tertiary)'
                    }}
                    onMouseLeave={(e) => {
                      (e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
                    }}
                    title={r.path}
                  >
                    <span style={{ fontSize: '13px', fontWeight: 500, whiteSpace: 'nowrap' }}>{r.name}</span>
                    <span
                      style={{
                        fontSize: '11px',
                        color: 'var(--text-dimmed)',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                        maxWidth: '280px'
                      }}
                    >
                      {r.path}
                    </span>
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

interface DropdownItemProps {
  label: string
  onClick?: () => void
  disabled?: boolean
  hasSubmenu?: boolean
}

function DropdownItem({ label, onClick, disabled = false, hasSubmenu = false }: DropdownItemProps): JSX.Element {
  return (
    <div
      onClick={disabled ? undefined : onClick}
      style={{
        padding: '7px 14px',
        fontSize: '13px',
        color: disabled ? 'var(--text-dimmed)' : 'var(--text-primary)',
        cursor: disabled ? 'default' : 'pointer',
        transition: 'background-color 100ms ease',
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center'
      }}
      onMouseEnter={(e) => {
        if (!disabled) {
          ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'var(--bg-tertiary)'
        }
      }}
      onMouseLeave={(e) => {
        ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'transparent'
      }}
    >
      <span>{label}</span>
      {hasSubmenu && <span style={{ fontSize: '10px', color: 'var(--text-dimmed)' }}>›</span>}
    </div>
  )
}
