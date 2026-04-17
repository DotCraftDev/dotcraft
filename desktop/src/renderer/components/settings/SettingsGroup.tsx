import type { CSSProperties, JSX, ReactNode } from 'react'

interface SettingsGroupProps {
  title?: string
  description?: string
  children: ReactNode
  /**
   * When true, the group renders a simple bordered container without row dividers.
   * Useful for groups whose body is a custom layout (e.g. grid of channel icons).
   */
  flush?: boolean
  style?: CSSProperties
}

/**
 * Grouped settings container styled similar to Codex: a single rounded card
 * with optional header, and rows separated by thin horizontal dividers.
 */
export function SettingsGroup({
  title,
  description,
  children,
  flush = false,
  style
}: SettingsGroupProps): JSX.Element {
  return (
    <section style={{ ...groupStyle(), ...style }}>
      {(title || description) && (
        <header style={headerStyle()}>
          {title && (
            <div style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)' }}>
              {title}
            </div>
          )}
          {description && (
            <div
              style={{
                fontSize: '12px',
                color: 'var(--text-dimmed)',
                lineHeight: 1.5,
                marginTop: '4px'
              }}
            >
              {description}
            </div>
          )}
        </header>
      )}
      <div className="dc-settings-group__body" style={flush ? flushBodyStyle() : bodyStyle()}>
        {children}
      </div>
    </section>
  )
}

interface SettingsRowProps {
  label?: ReactNode
  description?: ReactNode
  htmlFor?: string
  control?: ReactNode
  orientation?: 'inline' | 'block'
  children?: ReactNode
  align?: 'center' | 'flex-start'
  style?: CSSProperties
}

/**
 * A single row inside a SettingsGroup. `inline` places label/description on the left
 * and control on the right. `block` stacks control beneath label (for wide controls).
 * If `children` is provided instead of `control`, the full row body is custom content.
 */
export function SettingsRow({
  label,
  description,
  htmlFor,
  control,
  orientation = 'inline',
  children,
  align = 'center',
  style
}: SettingsRowProps): JSX.Element {
  if (children !== undefined && label === undefined && description === undefined && control === undefined) {
    return (
      <div className="dc-settings-row" style={{ ...rowStyle(), ...style }}>
        {children}
      </div>
    )
  }

  if (orientation === 'block') {
    return (
      <div
        className="dc-settings-row"
        style={{ ...rowStyle(), flexDirection: 'column', alignItems: 'stretch', gap: '10px', ...style }}
      >
        {(label || description) && (
          <div>
            {label && (
              <label
                htmlFor={htmlFor}
                style={{
                  display: 'block',
                  fontSize: '13px',
                  fontWeight: 600,
                  color: 'var(--text-primary)'
                }}
              >
                {label}
              </label>
            )}
            {description && (
              <div
                style={{
                  fontSize: '11px',
                  color: 'var(--text-dimmed)',
                  lineHeight: 1.5,
                  marginTop: '4px'
                }}
              >
                {description}
              </div>
            )}
          </div>
        )}
        {control}
        {children}
      </div>
    )
  }

  return (
    <div className="dc-settings-row" style={{ ...rowStyle(), alignItems: align, ...style }}>
      <div style={{ flex: 1, minWidth: 0 }}>
        {label && (
          <label
            htmlFor={htmlFor}
            style={{
              display: 'block',
              fontSize: '13px',
              fontWeight: 600,
              color: 'var(--text-primary)'
            }}
          >
            {label}
          </label>
        )}
        {description && (
          <div
            style={{
              fontSize: '11px',
              color: 'var(--text-dimmed)',
              lineHeight: 1.5,
              marginTop: '4px'
            }}
          >
            {description}
          </div>
        )}
      </div>
      {control !== undefined && <div style={{ flexShrink: 0 }}>{control}</div>}
    </div>
  )
}

function groupStyle(): CSSProperties {
  return {
    border: '1px solid var(--border-default)',
    borderRadius: '12px',
    background: 'var(--bg-secondary)',
    overflow: 'hidden'
  }
}

function headerStyle(): CSSProperties {
  return {
    padding: '14px 16px',
    borderBottom: '1px solid var(--border-default)',
    background: 'transparent'
  }
}

function bodyStyle(): CSSProperties {
  return {
    display: 'flex',
    flexDirection: 'column'
  }
}

function flushBodyStyle(): CSSProperties {
  return {
    padding: '14px 16px'
  }
}

function rowStyle(): CSSProperties {
  return {
    display: 'flex',
    gap: '16px',
    padding: '14px 16px',
    alignItems: 'center',
    borderTop: '1px solid var(--border-default)'
  }
}

