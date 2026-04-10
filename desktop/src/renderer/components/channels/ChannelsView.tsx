import { useEffect, useMemo, useState } from 'react'
import { addToast } from '../../stores/toastStore'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { CHANNEL_DEFS, type ChannelDefinition, type ChannelId } from './channelDefs'
import { ChannelCard, type ChannelConnectionState } from './ChannelCard'
import { QQConfigForm } from './QQConfigForm'
import { WeComConfigForm } from './WeComConfigForm'
import { useChannelConfig } from './useChannelConfig'
import {
  ExternalChannelConfigForm,
  type ExternalChannelConfigWire
} from './ExternalChannelConfigForm'

interface ChannelStatusWire {
  name: string
  category: string
  enabled: boolean
  running: boolean
}

interface ChannelInfoWire {
  name: string
}

type SelectedChannelKey = `native:${ChannelId}` | `external:${string}`

function createEmptyExternalChannel(): ExternalChannelConfigWire {
  return {
    name: '',
    enabled: true,
    transport: 'subprocess',
    command: '',
    args: [],
    workingDirectory: '',
    env: {}
  }
}

function cloneExternalChannel(channel: ExternalChannelConfigWire): ExternalChannelConfigWire {
  return {
    ...channel,
    args: [...(channel.args ?? [])],
    env: { ...(channel.env ?? {}) }
  }
}

function statusLabelKey(status: ChannelConnectionState): string {
  return status === 'connected'
    ? 'channels.status.connected'
    : status === 'enabledNotConnected'
      ? 'channels.status.enabledNotConnected'
      : 'channels.status.notConfigured'
}

function deriveNativeStatus(
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

  const connected = fallbackConnected?.has(def.channelListName.toLowerCase()) ?? false
  if (connected) return 'connected'

  const configEnabled = channelId === 'qq' ? config.qq.Enabled : config.wecom.Enabled
  return configEnabled ? 'enabledNotConnected' : 'notConfigured'
}

function deriveExternalStatus(
  name: string,
  enabled: boolean,
  statusMap: Map<string, ChannelStatusWire> | null,
  fallbackConnected: Set<string> | null
): ChannelConnectionState {
  if (statusMap !== null) {
    const s = statusMap.get(name.toLowerCase())
    if (!s) return enabled ? 'enabledNotConnected' : 'notConfigured'
    if (s.running) return 'connected'
    if (s.enabled) return 'enabledNotConnected'
    return 'notConfigured'
  }

  const connected = fallbackConnected?.has(name.toLowerCase()) ?? false
  if (connected) return 'connected'
  return enabled ? 'enabledNotConnected' : 'notConfigured'
}

function makeExternalChannelDef(name: string): ChannelDefinition {
  return {
    id: 'qq',
    nameKey: '',
    channelListName: name
  }
}

export function ChannelsView(): JSX.Element {
  const t = useT()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const [workspacePath, setWorkspacePath] = useState('')
  const [selectedChannelKey, setSelectedChannelKey] = useState<SelectedChannelKey>('native:qq')
  const [channelStatusMap, setChannelStatusMap] = useState<Map<string, ChannelStatusWire> | null>(null)
  const [fallbackConnected, setFallbackConnected] = useState<Set<string> | null>(null)
  const [statusError, setStatusError] = useState(false)
  const [externalChannels, setExternalChannels] = useState<ExternalChannelConfigWire[]>([])
  const [externalLoading, setExternalLoading] = useState(false)
  const [externalError, setExternalError] = useState<string | null>(null)
  const [externalDraft, setExternalDraft] = useState<ExternalChannelConfigWire>(createEmptyExternalChannel())
  const [savingExternal, setSavingExternal] = useState(false)
  const [deletingExternal, setDeletingExternal] = useState(false)

  const externalManagementEnabled = capabilities?.externalChannelManagement === true

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
      window.api.appServer
        .sendRequest('channel/list', {})
        .then((res) => {
          if (cancelled) return
          const wire = res as { channels?: ChannelInfoWire[] }
          setFallbackConnected(new Set((wire.channels ?? []).map((c) => c.name.toLowerCase())))
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

  async function reloadExternalChannels(): Promise<void> {
    if (!externalManagementEnabled) {
      setExternalChannels([])
      return
    }

    setExternalLoading(true)
    setExternalError(null)
    try {
      const res = (await window.api.appServer.sendRequest('externalChannel/list', {})) as {
        channels?: ExternalChannelConfigWire[]
      }
      const list = (res.channels ?? []).map(cloneExternalChannel)
      setExternalChannels(list)

      const selectedName =
        selectedChannelKey.startsWith('external:') ? selectedChannelKey.slice('external:'.length) : null
      if (selectedName && selectedName !== '__new__') {
        const selected = list.find((item) => item.name.toLowerCase() === selectedName.toLowerCase())
        if (selected) {
          setExternalDraft(cloneExternalChannel(selected))
        } else {
          setSelectedChannelKey('native:qq')
          setExternalDraft(createEmptyExternalChannel())
        }
      }
    } catch (err) {
      setExternalChannels([])
      setExternalError(err instanceof Error ? err.message : String(err))
    } finally {
      setExternalLoading(false)
    }
  }

  useEffect(() => {
    void reloadExternalChannels()
  }, [externalManagementEnabled])

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

  async function handleSaveExternal(): Promise<void> {
    setSavingExternal(true)
    try {
      const payload: ExternalChannelConfigWire = {
        ...externalDraft,
        name: externalDraft.name.trim(),
        command:
          externalDraft.transport === 'subprocess' ? externalDraft.command?.trim() ?? '' : null,
        args:
          externalDraft.transport === 'subprocess'
            ? (externalDraft.args ?? []).map((arg) => arg.trim()).filter(Boolean)
            : null,
        workingDirectory:
          externalDraft.transport === 'subprocess'
            ? (externalDraft.workingDirectory?.trim() || null)
            : null,
        env:
          externalDraft.transport === 'subprocess' && externalDraft.env
            ? Object.fromEntries(
                Object.entries(externalDraft.env).filter(([key]) => key.trim() !== '')
              )
            : null
      }

      await window.api.appServer.sendRequest('externalChannel/upsert', { channel: payload })
      await reloadExternalChannels()
      addToast(t('channels.savedRestart'), 'success')
      setSelectedChannelKey(`external:${payload.name}`)
    } catch (err) {
      addToast(
        t('channels.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setSavingExternal(false)
    }
  }

  async function handleDeleteExternal(): Promise<void> {
    const name = externalDraft.name.trim()
    if (!name) return
    setDeletingExternal(true)
    try {
      await window.api.appServer.sendRequest('externalChannel/remove', { name })
      await reloadExternalChannels()
      setSelectedChannelKey('native:qq')
      setExternalDraft(createEmptyExternalChannel())
      addToast(t('channels.external.removed'), 'success')
    } catch (err) {
      addToast(
        t('channels.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setDeletingExternal(false)
    }
  }

  const statusById = useMemo(() => {
    const statusMap = new Map<ChannelId, ChannelConnectionState>()
    for (const channel of CHANNEL_DEFS) {
      statusMap.set(channel.id, deriveNativeStatus(channel.id, channelStatusMap, fallbackConnected, config))
    }
    return statusMap
  }, [config, channelStatusMap, fallbackConnected])

  const externalStatusByName = useMemo(() => {
    const map = new Map<string, ChannelConnectionState>()
    for (const channel of externalChannels) {
      map.set(
        channel.name.toLowerCase(),
        deriveExternalStatus(channel.name, channel.enabled, channelStatusMap, fallbackConnected)
      )
    }
    if (selectedChannelKey === 'external:__new__') {
      map.set('__new__', deriveExternalStatus('__new__', externalDraft.enabled, channelStatusMap, fallbackConnected))
    }
    return map
  }, [externalChannels, externalDraft.enabled, channelStatusMap, fallbackConnected, selectedChannelKey])

  const selectedNativeId = selectedChannelKey.startsWith('native:')
    ? (selectedChannelKey.slice('native:'.length) as ChannelId)
    : null
  const selectedExternalName = selectedChannelKey.startsWith('external:')
    ? selectedChannelKey.slice('external:'.length)
    : null
  const selectedDef = selectedNativeId ? CHANNEL_DEFS.find((d) => d.id === selectedNativeId) : null

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
            width: '220px',
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
              return (
                <ChannelCard
                  key={channel.id}
                  channel={channel}
                  label={t(channel.nameKey)}
                  status={status}
                  statusLabel={t(statusLabelKey(status))}
                  active={selectedChannelKey === `native:${channel.id}`}
                  onClick={() => setSelectedChannelKey(`native:${channel.id}`)}
                />
              )
            })}
          </div>

          <div style={{ marginTop: 18 }}>
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                marginBottom: 8
              }}
            >
              <div style={{ fontSize: '11px', fontWeight: 700, color: 'var(--text-secondary)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
                {t('channels.external.group')}
              </div>
              {externalManagementEnabled && (
                <button
                  type="button"
                  onClick={() => {
                    setExternalDraft(createEmptyExternalChannel())
                    setSelectedChannelKey('external:__new__')
                  }}
                  style={{
                    border: 'none',
                    background: 'transparent',
                    color: 'var(--accent)',
                    cursor: 'pointer',
                    fontSize: '12px',
                    fontWeight: 600,
                    padding: 0
                  }}
                >
                  {t('channels.external.add')}
                </button>
              )}
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
              {externalChannels.map((channel) => {
                const status = externalStatusByName.get(channel.name.toLowerCase()) ?? 'notConfigured'
                return (
                  <ChannelCard
                    key={channel.name}
                    channel={makeExternalChannelDef(channel.name)}
                    label={channel.name}
                    status={status}
                    statusLabel={t(statusLabelKey(status))}
                    active={selectedChannelKey === `external:${channel.name}`}
                    onClick={() => {
                      setExternalDraft(cloneExternalChannel(channel))
                      setSelectedChannelKey(`external:${channel.name}`)
                    }}
                  />
                )
              })}
            </div>

            {!externalLoading && externalManagementEnabled && externalChannels.length === 0 && (
              <div style={{ marginTop: 10, fontSize: '12px', color: 'var(--text-dimmed)' }}>
                {t('channels.external.empty')}
              </div>
            )}
          </div>
        </aside>

        <main style={{ flex: 1, minWidth: 0, overflowY: 'auto', padding: '20px' }}>
          {loading && (
            <div style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>{t('channels.loading')}</div>
          )}

          {!loading && selectedDef && selectedNativeId === 'qq' && (
            <div style={{ maxWidth: '640px' }}>
              <QQConfigForm
                value={config.qq}
                saving={savingChannelId === 'qq'}
                logoPath={selectedDef.logoPath ?? ''}
                status={statusById.get('qq') ?? 'notConfigured'}
                statusLabel={t(statusLabelKey(statusById.get('qq') ?? 'notConfigured'))}
                onChange={(next) => setChannelConfig('qq', next)}
                onSave={() => void handleSave('qq')}
              />
            </div>
          )}

          {!loading && selectedDef && selectedNativeId === 'wecom' && (
            <div style={{ maxWidth: '640px' }}>
              <WeComConfigForm
                value={config.wecom}
                saving={savingChannelId === 'wecom'}
                logoPath={selectedDef.logoPath ?? ''}
                status={statusById.get('wecom') ?? 'notConfigured'}
                statusLabel={t(statusLabelKey(statusById.get('wecom') ?? 'notConfigured'))}
                onChange={(next) => setChannelConfig('wecom', next)}
                onSave={() => void handleSave('wecom')}
              />
            </div>
          )}

          {!loading && selectedExternalName && (
            <div style={{ maxWidth: '640px' }}>
              {!externalManagementEnabled ? (
                <div style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>
                  {t('channels.external.unavailable')}
                </div>
              ) : (
                <ExternalChannelConfigForm
                  value={externalDraft}
                  saving={savingExternal}
                  deleting={deletingExternal}
                  isNew={selectedExternalName === '__new__'}
                  status={
                    selectedExternalName === '__new__'
                      ? deriveExternalStatus('__new__', externalDraft.enabled, channelStatusMap, fallbackConnected)
                      : externalStatusByName.get(selectedExternalName.toLowerCase()) ?? 'notConfigured'
                  }
                  statusLabel={t(
                    statusLabelKey(
                      selectedExternalName === '__new__'
                        ? deriveExternalStatus('__new__', externalDraft.enabled, channelStatusMap, fallbackConnected)
                        : externalStatusByName.get(selectedExternalName.toLowerCase()) ?? 'notConfigured'
                    )
                  )}
                  onChange={setExternalDraft}
                  onSave={() => void handleSaveExternal()}
                  onDelete={
                    selectedExternalName === '__new__'
                      ? undefined
                      : () => {
                          void handleDeleteExternal()
                        }
                  }
                />
              )}
            </div>
          )}

          {(error || statusError || externalError) && (
            <div style={{ marginTop: '16px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
              {error
                ? t('channels.loadFailed', { error })
                : externalError
                  ? t('channels.loadFailed', { error: externalError })
                  : t('channels.statusUnavailable')}
            </div>
          )}
        </main>
      </div>
    </div>
  )
}
