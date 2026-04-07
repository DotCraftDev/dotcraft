import { useT } from '../../contexts/LocaleContext'
import type { QQChannelConfig } from './useChannelConfig'
import { ToggleSwitch } from './ToggleSwitch'
import type { ChannelConnectionState } from './ChannelCard'
import { formStyles, StatusPill, FieldCard, FormActions } from './FormShared'

interface QQConfigFormProps {
  value: QQChannelConfig
  saving: boolean
  logoPath: string
  status: ChannelConnectionState
  statusLabel: string
  onChange: (next: QQChannelConfig) => void
  onSave: () => void
}

export function QQConfigForm({
  value,
  saving,
  logoPath,
  status,
  statusLabel,
  onChange,
  onSave
}: QQConfigFormProps): JSX.Element {
  const t = useT()

  return (
    <div>
      {/* Channel header */}
      <div style={formStyles.header}>
        <img
          src={logoPath}
          alt={t('channels.channel.qq')}
          width={32}
          height={32}
          style={formStyles.headerLogo}
        />
        <div>
          <div style={formStyles.headerTitle}>{t('channels.qq.title')}</div>
          <StatusPill status={status} label={statusLabel} />
        </div>
      </div>

      {/* Enable toggle card */}
      <FieldCard>
        <ToggleSwitch
          checked={value.Enabled}
          onChange={(checked) => onChange({ ...value, Enabled: checked })}
          label={t('channels.enableChannel')}
          description={t('channels.qq.enableDescription')}
        />
      </FieldCard>

      {/* Config fields */}
      <div style={{ opacity: value.Enabled ? 1 : 0.5, pointerEvents: value.Enabled ? 'auto' : 'none' }}>
        <FieldCard>
          <div style={formStyles.fieldGroup}>
            <label style={formStyles.label}>{t('channels.qq.host')}</label>
            <input
              type="text"
              value={value.Host}
              onChange={(e) => onChange({ ...value, Host: e.target.value })}
              style={formStyles.input}
              onFocus={formStyles.inputFocus}
              onBlur={formStyles.inputBlur}
            />
          </div>
          <div style={formStyles.fieldGroup}>
            <label style={formStyles.label}>{t('channels.qq.port')}</label>
            <input
              type="number"
              value={String(value.Port)}
              onChange={(e) =>
                onChange({ ...value, Port: Number.parseInt(e.target.value || '0', 10) || 0 })
              }
              style={formStyles.input}
              onFocus={formStyles.inputFocus}
              onBlur={formStyles.inputBlur}
            />
          </div>
          <div style={{ ...formStyles.fieldGroup, marginBottom: 0 }}>
            <label style={formStyles.label}>{t('channels.qq.accessToken')}</label>
            <input
              type="password"
              value={value.AccessToken}
              onChange={(e) => onChange({ ...value, AccessToken: e.target.value })}
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
