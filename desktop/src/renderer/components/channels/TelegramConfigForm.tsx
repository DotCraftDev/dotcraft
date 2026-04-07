import { useT } from '../../contexts/LocaleContext'
import type { TelegramChannelConfig } from './useChannelConfig'
import { ToggleSwitch } from './ToggleSwitch'
import type { ChannelConnectionState } from './ChannelCard'
import { formStyles, StatusPill, FieldCard, FormActions } from './FormShared'

interface TelegramConfigFormProps {
  value: TelegramChannelConfig
  saving: boolean
  logoPath: string
  status: ChannelConnectionState
  statusLabel: string
  onChange: (next: TelegramChannelConfig) => void
  onSave: () => void
}

export function TelegramConfigForm({
  value,
  saving,
  logoPath,
  status,
  statusLabel,
  onChange,
  onSave
}: TelegramConfigFormProps): JSX.Element {
  const t = useT()

  return (
    <div>
      {/* Channel header */}
      <div style={formStyles.header}>
        <img
          src={logoPath}
          alt={t('channels.channel.telegram')}
          width={32}
          height={32}
          style={formStyles.headerLogo}
        />
        <div>
          <div style={formStyles.headerTitle}>{t('channels.telegram.title')}</div>
          <StatusPill status={status} label={statusLabel} />
        </div>
      </div>

      {/* Enable toggle card */}
      <FieldCard>
        <ToggleSwitch
          checked={value.enabled}
          onChange={(checked) => onChange({ ...value, enabled: checked })}
          label={t('channels.enableChannel')}
        />
      </FieldCard>

      {/* Config fields */}
      <div
        style={{
          opacity: value.enabled ? 1 : 0.5,
          pointerEvents: value.enabled ? 'auto' : 'none'
        }}
      >
        <FieldCard>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '14px' }}>
            <span
              style={{
                fontSize: '11px',
                fontWeight: 600,
                color: 'var(--text-secondary)',
                textTransform: 'uppercase',
                letterSpacing: '0.04em'
              }}
            >
              {t('channels.transport')}
            </span>
            <span
              style={{
                fontSize: '12px',
                fontWeight: 500,
                color: 'var(--text-primary)',
                backgroundColor: 'var(--bg-tertiary)',
                border: '1px solid var(--border-default)',
                borderRadius: '4px',
                padding: '1px 6px'
              }}
            >
              Subprocess
            </span>
          </div>
          <div style={formStyles.fieldGroup}>
            <label style={formStyles.label}>{t('channels.telegram.command')}</label>
            <input
              type="text"
              value={value.command}
              onChange={(e) => onChange({ ...value, command: e.target.value })}
              style={formStyles.input}
              onFocus={formStyles.inputFocus}
              onBlur={formStyles.inputBlur}
            />
          </div>
          <div style={formStyles.fieldGroup}>
            <label style={formStyles.label}>{t('channels.telegram.args')}</label>
            <input
              type="text"
              value={value.args.join(' ')}
              onChange={(e) =>
                onChange({
                  ...value,
                  args: e.target.value.trim() ? e.target.value.trim().split(/\s+/) : []
                })
              }
              style={formStyles.input}
              onFocus={formStyles.inputFocus}
              onBlur={formStyles.inputBlur}
            />
          </div>
          <div style={formStyles.fieldGroup}>
            <label style={formStyles.label}>{t('channels.telegram.workingDirectory')}</label>
            <input
              type="text"
              value={value.workingDirectory ?? ''}
              onChange={(e) => onChange({ ...value, workingDirectory: e.target.value })}
              style={formStyles.input}
              onFocus={formStyles.inputFocus}
              onBlur={formStyles.inputBlur}
            />
          </div>
          <div style={{ ...formStyles.fieldGroup, marginBottom: 0 }}>
            <label style={formStyles.label}>{t('channels.telegram.botToken')}</label>
            <input
              type="password"
              value={value.env.TELEGRAM_BOT_TOKEN ?? ''}
              onChange={(e) =>
                onChange({
                  ...value,
                  env: { ...value.env, TELEGRAM_BOT_TOKEN: e.target.value }
                })
              }
              style={formStyles.input}
              onFocus={formStyles.inputFocus}
              onBlur={formStyles.inputBlur}
            />
          </div>
        </FieldCard>
      </div>

      <FormActions saving={saving} onSave={onSave} />
    </div>
  )
}
