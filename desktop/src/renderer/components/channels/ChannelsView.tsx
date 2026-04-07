import { useEffect, useMemo, useState } from 'react'
import { addToast } from '../../stores/toastStore'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { CHANNEL_DEFS, type ChannelId } from './channelDefs'
import { ChannelCard, type ChannelConnectionState } from './ChannelCard'
import { QQConfigForm } from './QQConfigForm'
import { WeComConfigForm } from './WeComConfigForm'
import { WeixinConfigForm } from './WeixinConfigForm'
import { TelegramConfigForm } from './TelegramConfigForm'
import { useChannelConfig } from './useChannelConfig'

/** Shape returned by channel/status */
interface ChannelStatusWire {
  name: string
  category: string
  enabled: boolean
  running: boolean
}

/** Shape returned by channel/list (fallback when channelStatus capability absent) */
interface ChannelInfoWire {
  name: string
}

function deriveStatus(
  channelId: ChannelId,
  statusMap: Map<string, ChannelStatusWire> | null,
  fallbackConnected: Set<string> | null,
  config: ReturnType<typeof useChannelConfig>['config']
): ChannelConnectionState {
  const def = CHANNEL_DEFS.find((d) => d.id === channelId)
  if (!def) return 'notConfigured'

  if (statusMap !== null) {
    const s = statusMap.get(def.channelListName.toLowerCase())
    if (!s) return 'notConfigured'
    if (s.running) return 'connected'
    if (s.enabled) return 'enabledNotConnected'
    return 'notConfigured'
  }

  // Fallback: config-based detection using channel/list result
  const connected = fallbackConnected?.has(def.channelListName.toLowerCase()) ?? false
  if (connected) return 'connected'

  let configEnabled = false
  if (channelId === 'qq') configEnabled = config.qq.Enabled
  else if (channelId === 'wecom') configEnabled = config.wecom.Enabled
  else if (channelId === 'weixin') configEnabled = config.weixin.enabled
  else if (channelId === 'telegram') configEnabled = config.telegram.enabled

  return configEnabled ? 'enabledNotConnected' : 'notConfigured'
}

export function ChannelsView(): JSX.Element {
  const t = useT()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const [workspacePath, setWorkspacePath] = useState('')
  const [selectedChannelId, setSelectedChannelId] = useState<ChannelId>('qq')

  // channel/status data (accurate)
  const [channelStatusMap, setChannelStatusMap] = useState<Map<string, ChannelStatusWire> | null>(
    null
  )
  // channel/list fallback data
  const [fallbackConnected, setFallbackConnected] = useState<Set<string> | null>(null)
  const [statusError, setStatusError] = useState(false)

  const {
    config,
    loading,
    error,
    savingChannelId,
    setChannelConfig,
    reload,
    saveChannel
  } = useChannelConfig(workspacePath)

  useEffect(() => {
    window.api.window
      .getWorkspacePath()
      .then((path) => setWorkspacePath(path))
      .catch(() => {})
  }, [])

  useEffect(() => {
    if (!workspacePath) return
    void reload()
  }, [workspacePath, reload])

  // Fetch runtime channel status (or fall back to channel/list)
  useEffect(() => {
    let cancelled = false
    const hasChannelStatus = capabilities?.channelStatus === true

    if (hasChannelStatus) {
      window.api.appServer
        .sendRequest('channel/status', {})
        .then((res) => {
          if (cancelled) return
          const wire = res as { channels?: ChannelStatusWire[] }
          const map = new Map<string, ChannelStatusWire>()
          for (const ch of wire.channels ?? []) {
            map.set(ch.name.toLowerCase(), ch)
          }
          setChannelStatusMap(map)
          setFallbackConnected(null)
          setStatusError(false)
        })
        .catch(() => {
          if (cancelled) return
          setChannelStatusMap(null)
          setStatusError(true)
        })
    } else {
      // Fallback: use channel/list (less accurate — doesn't distinguish enabled vs running)
      window.api.appServer
        .sendRequest('channel/list', {})
        .then((res) => {
          if (cancelled) return
          const wire = res as { channels?: ChannelInfoWire[] }
          setFallbackConnected(
            new Set((wire.channels ?? []).map((c) => c.name.toLowerCase()))
          )
          setChannelStatusMap(null)
          setStatusError(false)
        })
        .catch(() => {
          if (cancelled) return
          setFallbackConnected(null)
          setStatusError(true)
        })
    }

    return () => {
      cancelled = true
    }
  }, [capabilities])

  async function handleSave(channelId: ChannelId): Promise<void> {
    try {
      await saveChannel(channelId)
      addToast(t('channels.savedRestart'), 'success')
      await reload()
    } catch (err) {
      addToast(
        t('channels.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  const statusById = useMemo(() => {
    const statusMap = new Map<ChannelId, ChannelConnectionState>()
    for (const channel of CHANNEL_DEFS) {
      statusMap.set(
        channel.id,
        deriveStatus(channel.id, channelStatusMap, fallbackConnected, config)
      )
    }
    return statusMap
  }, [config, channelStatusMap, fallbackConnected])

  const selectedDef = CHANNEL_DEFS.find((d) => d.id === selectedChannelId)

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        minHeight: 0,
        backgroundColor: 'var(--bg-primary)'
      }}
    >
      <header
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: '12px',
          padding: '16px 20px',
          borderBottom: '1px solid var(--border-default)',
          flexShrink: 0
        }}
      >
        <h1 style={{ margin: 0, fontSize: '18px', fontWeight: 600, color: 'var(--text-primary)' }}>
          {t('channels.title')}
        </h1>
        <button
          type="button"
          onClick={() => setActiveMainView('conversation')}
          title={t('channels.close')}
          aria-label={t('channels.close')}
          style={{
            width: '30px',
            height: '30px',
            borderRadius: '6px',
            border: '1px solid var(--border-default)',
            background: 'transparent',
            color: 'var(--text-secondary)',
            cursor: 'pointer',
            fontSize: '18px',
            lineHeight: 1
          }}
        >
          ×
        </button>
      </header>

      <div style={{ display: 'flex', flex: 1, minHeight: 0 }}>
        <aside
          style={{
            width: '190px',
            borderRight: '1px solid var(--border-default)',
            backgroundColor: 'var(--bg-secondary)',
            padding: '12px',
            flexShrink: 0,
            overflowY: 'auto'
          }}
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
            {CHANNEL_DEFS.map((channel) => {
              const status = statusById.get(channel.id) ?? 'notConfigured'
              const statusKey =
                status === 'connected'
                  ? 'channels.status.connected'
                  : status === 'enabledNotConnected'
                    ? 'channels.status.enabledNotConnected'
                    : 'channels.status.notConfigured'
              return (
                <ChannelCard
                  key={channel.id}
                  channel={channel}
                  label={t(channel.nameKey)}
                  status={status}
                  statusLabel={t(statusKey)}
                  active={selectedChannelId === channel.id}
                  onClick={() => setSelectedChannelId(channel.id)}
                />
              )
            })}
          </div>
        </aside>

        <main style={{ flex: 1, minWidth: 0, overflowY: 'auto', padding: '20px' }}>
          {loading && (
            <div style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>{t('channels.loading')}</div>
          )}
          {!loading && selectedDef && (
            <div style={{ maxWidth: '640px' }}>
              {selectedChannelId === 'qq' && (
                <QQConfigForm
                  value={config.qq}
                  saving={savingChannelId === 'qq'}
                  logoPath={selectedDef.logoPath}
                  status={statusById.get('qq') ?? 'notConfigured'}
                  statusLabel={t(
                    (statusById.get('qq') ?? 'notConfigured') === 'connected'
                      ? 'channels.status.connected'
                      : (statusById.get('qq') ?? 'notConfigured') === 'enabledNotConnected'
                        ? 'channels.status.enabledNotConnected'
                        : 'channels.status.notConfigured'
                  )}
                  onChange={(next) => setChannelConfig('qq', next)}
                  onSave={() => void handleSave('qq')}
                />
              )}
              {selectedChannelId === 'wecom' && (
                <WeComConfigForm
                  value={config.wecom}
                  saving={savingChannelId === 'wecom'}
                  logoPath={selectedDef.logoPath}
                  status={statusById.get('wecom') ?? 'notConfigured'}
                  statusLabel={t(
                    (statusById.get('wecom') ?? 'notConfigured') === 'connected'
                      ? 'channels.status.connected'
                      : (statusById.get('wecom') ?? 'notConfigured') === 'enabledNotConnected'
                        ? 'channels.status.enabledNotConnected'
                        : 'channels.status.notConfigured'
                  )}
                  onChange={(next) => setChannelConfig('wecom', next)}
                  onSave={() => void handleSave('wecom')}
                />
              )}
              {selectedChannelId === 'weixin' && (
                <WeixinConfigForm
                  value={config.weixin}
                  saving={savingChannelId === 'weixin'}
                  logoPath={selectedDef.logoPath}
                  status={statusById.get('weixin') ?? 'notConfigured'}
                  statusLabel={t(
                    (statusById.get('weixin') ?? 'notConfigured') === 'connected'
                      ? 'channels.status.connected'
                      : (statusById.get('weixin') ?? 'notConfigured') === 'enabledNotConnected'
                        ? 'channels.status.enabledNotConnected'
                        : 'channels.status.notConfigured'
                  )}
                  onChange={(next) => setChannelConfig('weixin', next)}
                  onSave={() => void handleSave('weixin')}
                />
              )}
              {selectedChannelId === 'telegram' && (
                <TelegramConfigForm
                  value={config.telegram}
                  saving={savingChannelId === 'telegram'}
                  logoPath={selectedDef.logoPath}
                  status={statusById.get('telegram') ?? 'notConfigured'}
                  statusLabel={t(
                    (statusById.get('telegram') ?? 'notConfigured') === 'connected'
                      ? 'channels.status.connected'
                      : (statusById.get('telegram') ?? 'notConfigured') === 'enabledNotConnected'
                        ? 'channels.status.enabledNotConnected'
                        : 'channels.status.notConfigured'
                  )}
                  onChange={(next) => setChannelConfig('telegram', next)}
                  onSave={() => void handleSave('telegram')}
                />
              )}
            </div>
          )}

          {(error || statusError) && (
            <div style={{ marginTop: '16px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
              {error ? t('channels.loadFailed', { error }) : t('channels.statusUnavailable')}
            </div>
          )}
        </main>
      </div>
    </div>
  )
}
