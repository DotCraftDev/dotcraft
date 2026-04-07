import { useT } from '../../contexts/LocaleContext'
import type { WeixinChannelConfig } from './useChannelConfig'
import { ToggleSwitch } from './ToggleSwitch'
import type { ChannelConnectionState } from './ChannelCard'
import { formStyles, StatusPill, FieldCard, FormActions } from './FormShared'

interface WeixinConfigFormProps {
  value: WeixinChannelConfig
  saving: boolean
  logoPath: string
  status: ChannelConnectionState
  statusLabel: string
  onChange: (next: WeixinChannelConfig) => void
  onSave: () => void
}

export function WeixinConfigForm({
  value,
  saving,
  logoPath,
  status,
  statusLabel,
  onChange,
  onSave
}: WeixinConfigFormProps): JSX.Element {
  const t = useT()

  return (
    <div>
      {/* Channel header */}
      <div style={formStyles.header}>
        <img
          src={logoPath}
          alt={t('channels.channel.weixin')}
          width={32}
          height={32}
          style={formStyles.headerLogo}
        />
        <div>
          <div style={formStyles.headerTitle}>{t('channels.weixin.title')}</div>
          <StatusPill status={status} label={statusLabel} />
        </div>
      </div>

      {/* Enable toggle card */}
      <FieldCard>
        <ToggleSwitch
          checked={value.enabled}
          onChange={(checked) => onChange({ ...value, enabled: checked })}
          label={t('channels.enableChannel')}
          description={t('channels.weixin.enableDescription')}
        />
      </FieldCard>

      {/* Info card */}
      <div
        style={{
          opacity: value.enabled ? 1 : 0.5,
          pointerEvents: value.enabled ? 'auto' : 'none'
        }}
      >
        <FieldCard>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
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
              WebSocket
            </span>
          </div>
          <p
            style={{
              margin: 0,
              fontSize: '12px',
              color: 'var(--text-secondary)',
              lineHeight: 1.5
            }}
          >
            {t('channels.weixin.transportNote')}
          </p>
        </FieldCard>
      </div>

      <FormActions saving={saving} onSave={onSave} />
    </div>
  )
}
