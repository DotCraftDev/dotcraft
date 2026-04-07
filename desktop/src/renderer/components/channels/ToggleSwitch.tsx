import { useId } from 'react'

interface ToggleSwitchProps {
  checked: boolean
  onChange: (checked: boolean) => void
  label?: string
  description?: string
  disabled?: boolean
}

export function ToggleSwitch({
  checked,
  onChange,
  label,
  description,
  disabled = false
}: ToggleSwitchProps): JSX.Element {
  const id = useId()
  const labelId = `${id}-label`

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: '12px',
        opacity: disabled ? 0.5 : 1,
        pointerEvents: disabled ? 'none' : 'auto'
      }}
    >
      {(label || description) && (
        <div style={{ flex: 1, minWidth: 0 }}>
          {label && (
            <label
              id={labelId}
              htmlFor={id}
              style={{
                display: 'block',
                fontSize: '13px',
                fontWeight: 600,
                color: 'var(--text-primary)',
                cursor: 'pointer',
                lineHeight: 1.4
              }}
            >
              {label}
            </label>
          )}
          {description && (
            <p
              style={{
                margin: '2px 0 0 0',
                fontSize: '12px',
                color: 'var(--text-secondary)',
                lineHeight: 1.4
              }}
            >
              {description}
            </p>
          )}
        </div>
      )}

      {/* Hidden native checkbox for accessibility */}
      <input
        id={id}
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
        style={{
          position: 'absolute',
          width: 1,
          height: 1,
          margin: -1,
          padding: 0,
          border: 0,
          overflow: 'hidden',
          clip: 'rect(0,0,0,0)',
          whiteSpace: 'nowrap'
        }}
      />

      {/* Visual pill track */}
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        aria-labelledby={label ? labelId : undefined}
        onClick={() => onChange(!checked)}
        style={{
          flexShrink: 0,
          width: 36,
          height: 20,
          borderRadius: 10,
          border: 'none',
          padding: 0,
          cursor: 'pointer',
          backgroundColor: checked ? 'var(--accent)' : 'var(--border-active)',
          transition: 'background-color 150ms ease',
          position: 'relative',
          outline: 'none'
        }}
        onFocus={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.boxShadow =
            '0 0 0 2px var(--bg-primary), 0 0 0 4px var(--accent)'
        }}
        onBlur={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.boxShadow = 'none'
        }}
      >
        <span
          aria-hidden
          style={{
            position: 'absolute',
            top: 2,
            left: checked ? 18 : 2,
            width: 16,
            height: 16,
            borderRadius: '50%',
            backgroundColor: 'white',
            transition: 'left 150ms ease',
            boxShadow: '0 1px 3px rgba(0,0,0,0.2)'
          }}
        />
      </button>
    </div>
  )
}
