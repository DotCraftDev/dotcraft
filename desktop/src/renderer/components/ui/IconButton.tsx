import type { ButtonHTMLAttributes, CSSProperties, JSX, ReactNode } from 'react'

interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children'> {
  icon: ReactNode
  label: string
  size?: number
  active?: boolean
}

export function IconButton({
  icon,
  label,
  size = 32,
  active = false,
  disabled = false,
  style,
  ...props
}: IconButtonProps): JSX.Element {
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      disabled={disabled}
      style={{
        ...iconButtonStyle(size, active, disabled),
        ...style
      }}
      {...props}
    >
      {icon}
    </button>
  )
}

function iconButtonStyle(size: number, active: boolean, disabled: boolean): CSSProperties {
  return {
    width: `${size}px`,
    height: `${size}px`,
    minWidth: `${size}px`,
    borderRadius: active ? '10px' : '9px',
    border: active ? '1px solid color-mix(in srgb, var(--accent) 55%, transparent)' : '1px solid var(--border-default)',
    background: active ? 'color-mix(in srgb, var(--accent) 14%, var(--bg-secondary))' : 'var(--bg-secondary)',
    color: disabled ? 'var(--text-dimmed)' : active ? 'var(--accent)' : 'var(--text-secondary)',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    cursor: disabled ? 'default' : 'pointer',
    transition: 'background-color 120ms ease, border-color 120ms ease, color 120ms ease, transform 120ms ease',
    opacity: disabled ? 0.65 : 1,
    padding: 0,
    outline: 'none',
    flexShrink: 0
  }
}
