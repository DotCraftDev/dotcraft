import { useMemo, useState } from 'react'
import type { DiscoveredModule } from '../../../preload/api.d'
import { useT } from '../../contexts/LocaleContext'
import { FieldCard, FormActions, StatusPill, formStyles } from './FormShared'
import { ToggleSwitch } from './ToggleSwitch'

interface ModuleConfigFormProps {
  module: DiscoveredModule
  config: Record<string, unknown>
  onChange: (next: Record<string, unknown>) => void
  onSave: () => void
  saving: boolean
  logoPath?: string
}

function toText(value: unknown): string {
  if (value == null) return ''
  if (typeof value === 'string') return value
  if (typeof value === 'number' || typeof value === 'boolean') return String(value)
  try {
    return JSON.stringify(value)
  } catch {
    return ''
  }
}

function getNestedValue(obj: Record<string, unknown>, dottedKey: string): unknown {
  const parts = dottedKey.split('.').filter(Boolean)
  if (parts.length === 0) return undefined
  let current: unknown = obj
  for (const part of parts) {
    if (current == null || typeof current !== 'object' || Array.isArray(current)) {
      return undefined
    }
    current = (current as Record<string, unknown>)[part]
  }
  return current
}

function setNestedValue(
  obj: Record<string, unknown>,
  dottedKey: string,
  value: unknown
): Record<string, unknown> {
  const parts = dottedKey.split('.').filter(Boolean)
  if (parts.length === 0) return obj
  const result: Record<string, unknown> = { ...obj }
  let current: Record<string, unknown> = result
  for (let i = 0; i < parts.length - 1; i += 1) {
    const existing = current[parts[i]]
    const next =
      existing != null && typeof existing === 'object' && !Array.isArray(existing)
        ? { ...(existing as Record<string, unknown>) }
        : {}
    current[parts[i]] = next
    current = next
  }
  current[parts[parts.length - 1]] = value
  return result
}

function applyValueChange(
  config: Record<string, unknown>,
  key: string,
  value: unknown
): Record<string, unknown> {
  return setNestedValue(config, key, value)
}

export function ModuleConfigForm({
  module,
  config,
  onChange,
  onSave,
  saving,
  logoPath
}: ModuleConfigFormProps): JSX.Element {
  const t = useT()
  const [showSecretByKey, setShowSecretByKey] = useState<Record<string, boolean>>({})
  const [listTextByKey, setListTextByKey] = useState<Record<string, string>>({})
  const [objectTextByKey, setObjectTextByKey] = useState<Record<string, string>>({})
  const descriptors = useMemo(
    () => module.configDescriptors.filter((descriptor) => descriptor.interactiveSetupOnly !== true),
    [module.configDescriptors]
  )

  return (
    <div style={{ maxWidth: '720px' }}>
      <div style={formStyles.header}>
        {logoPath ? (
          <img
            src={logoPath}
            alt={module.displayName}
            width={44}
            height={44}
            style={formStyles.headerLogo}
          />
        ) : (
          <div
            aria-hidden
            style={{
              ...formStyles.headerLogo,
              width: 44,
              height: 44,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              color: 'var(--text-secondary)',
              fontSize: '18px',
              fontWeight: 700
            }}
          >
            {module.displayName.slice(0, 1).toUpperCase()}
          </div>
        )}

        <div style={{ minWidth: 0 }}>
          <div style={formStyles.headerTitle}>{module.displayName}</div>
          <div
            style={{
              marginTop: '4px',
              display: 'flex',
              alignItems: 'center',
              gap: '8px'
            }}
          >
            <span
              style={{
                fontSize: '11px',
                color: 'var(--text-dimmed)',
                border: '1px solid var(--border-default)',
                borderRadius: '999px',
                padding: '2px 8px'
              }}
            >
              {module.source === 'bundled'
                ? t('channels.modules.source.bundled')
                : t('channels.modules.source.user')}
            </span>
            <StatusPill status="notConfigured" label={t('channels.status.notConfigured')} />
          </div>
        </div>
      </div>

      <FieldCard>
        {descriptors.map((descriptor) => {
          const value = getNestedValue(config, descriptor.key)
          const requiredSuffix = descriptor.required ? ` (${t('channels.modules.required')})` : ''
          const placeholder =
            descriptor.defaultValue === undefined ? undefined : String(descriptor.defaultValue)

          if (descriptor.dataKind === 'boolean') {
            return (
              <div key={descriptor.key} style={formStyles.fieldGroup}>
                <ToggleSwitch
                  checked={value === true}
                  onChange={(checked) => {
                    onChange(applyValueChange(config, descriptor.key, checked))
                  }}
                  label={`${descriptor.displayLabel}${requiredSuffix}`}
                  description={descriptor.description}
                />
              </div>
            )
          }

          if (descriptor.dataKind === 'enum') {
            const enumValues = descriptor.enumValues ?? []
            return (
              <div key={descriptor.key} style={formStyles.fieldGroup}>
                <label style={formStyles.label}>{`${descriptor.displayLabel}${requiredSuffix}`}</label>
                <select
                  value={typeof value === 'string' ? value : ''}
                  onChange={(event) => {
                    onChange(applyValueChange(config, descriptor.key, event.target.value))
                  }}
                  onFocus={formStyles.inputFocus}
                  onBlur={formStyles.inputBlur}
                  style={formStyles.input}
                >
                  <option value="" disabled={descriptor.required}>
                    {placeholder ?? ''}
                  </option>
                  {enumValues.map((item) => (
                    <option key={item} value={item}>
                      {item}
                    </option>
                  ))}
                </select>
                {!!descriptor.description && (
                  <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
                    {descriptor.description}
                  </div>
                )}
              </div>
            )
          }

          if (descriptor.dataKind === 'list') {
            const textValue =
              listTextByKey[descriptor.key] ??
              (Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string').join('\n') : '')
            return (
              <div key={descriptor.key} style={formStyles.fieldGroup}>
                <label style={formStyles.label}>{`${descriptor.displayLabel}${requiredSuffix}`}</label>
                <textarea
                  value={textValue}
                  placeholder={placeholder}
                  onChange={(event) => {
                    const nextText = event.target.value
                    setListTextByKey((prev) => ({ ...prev, [descriptor.key]: nextText }))
                    const nextList = nextText
                      .split('\n')
                      .map((item) => item.trim())
                      .filter(Boolean)
                    onChange(applyValueChange(config, descriptor.key, nextList))
                  }}
                  onFocus={formStyles.inputFocus}
                  onBlur={formStyles.inputBlur}
                  style={{ ...formStyles.input, minHeight: '90px', height: 'auto', padding: '8px 10px' }}
                />
                {!!descriptor.description && (
                  <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
                    {descriptor.description}
                  </div>
                )}
              </div>
            )
          }

          if (descriptor.dataKind === 'object') {
            const textValue =
              objectTextByKey[descriptor.key] ??
              (value == null ? '' : JSON.stringify(value, null, 2))
            return (
              <div key={descriptor.key} style={formStyles.fieldGroup}>
                <label style={formStyles.label}>{`${descriptor.displayLabel}${requiredSuffix}`}</label>
                <textarea
                  value={textValue}
                  placeholder={placeholder}
                  onChange={(event) => {
                    setObjectTextByKey((prev) => ({ ...prev, [descriptor.key]: event.target.value }))
                  }}
                  onBlur={(event) => {
                    formStyles.inputBlur(event)
                    const raw = event.target.value.trim()
                    if (raw === '') {
                      onChange(applyValueChange(config, descriptor.key, undefined))
                      return
                    }
                    try {
                      const parsed = JSON.parse(raw) as unknown
                      onChange(applyValueChange(config, descriptor.key, parsed))
                    } catch {
                      // Keep user text untouched until it is valid JSON.
                    }
                  }}
                  onFocus={formStyles.inputFocus}
                  style={{ ...formStyles.input, minHeight: '120px', height: 'auto', padding: '8px 10px' }}
                />
                {!!descriptor.description && (
                  <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
                    {descriptor.description}
                  </div>
                )}
              </div>
            )
          }

          if (descriptor.dataKind === 'number') {
            return (
              <div key={descriptor.key} style={formStyles.fieldGroup}>
                <label style={formStyles.label}>{`${descriptor.displayLabel}${requiredSuffix}`}</label>
                <input
                  type="number"
                  value={typeof value === 'number' && Number.isFinite(value) ? String(value) : ''}
                  placeholder={placeholder}
                  onChange={(event) => {
                    const nextRaw = event.target.value.trim()
                    const parsed = nextRaw === '' ? undefined : Number.parseFloat(nextRaw)
                    onChange(
                      applyValueChange(
                        config,
                        descriptor.key,
                        parsed === undefined || Number.isNaN(parsed) ? undefined : parsed
                      )
                    )
                  }}
                  onFocus={formStyles.inputFocus}
                  onBlur={formStyles.inputBlur}
                  style={formStyles.input}
                />
                {!!descriptor.description && (
                  <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
                    {descriptor.description}
                  </div>
                )}
              </div>
            )
          }

          const isSecret = descriptor.dataKind === 'secret' || descriptor.masked
          return (
            <div key={descriptor.key} style={formStyles.fieldGroup}>
              <label style={formStyles.label}>{`${descriptor.displayLabel}${requiredSuffix}`}</label>
              <div style={{ position: 'relative' }}>
                <input
                  type={isSecret && !showSecretByKey[descriptor.key] ? 'password' : 'text'}
                  value={toText(value)}
                  placeholder={placeholder}
                  onChange={(event) => {
                    onChange(applyValueChange(config, descriptor.key, event.target.value))
                  }}
                  onFocus={formStyles.inputFocus}
                  onBlur={formStyles.inputBlur}
                  style={{
                    ...formStyles.input,
                    paddingRight: isSecret ? '72px' : formStyles.input.paddingRight
                  }}
                />
                {isSecret && (
                  <button
                    type="button"
                    onClick={() => {
                      setShowSecretByKey((prev) => ({
                        ...prev,
                        [descriptor.key]: !prev[descriptor.key]
                      }))
                    }}
                    style={{
                      position: 'absolute',
                      right: '8px',
                      top: '50%',
                      transform: 'translateY(-50%)',
                      border: 'none',
                      background: 'transparent',
                      color: 'var(--text-secondary)',
                      fontSize: '12px',
                      cursor: 'pointer'
                    }}
                  >
                    {showSecretByKey[descriptor.key] ? 'Hide' : 'Show'}
                  </button>
                )}
              </div>
              {!!descriptor.description && (
                <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
                  {descriptor.description}
                </div>
              )}
            </div>
          )
        })}
      </FieldCard>

      <FormActions saving={saving} onSave={onSave} />
    </div>
  )
}
