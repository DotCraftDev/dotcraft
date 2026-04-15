import { useEffect, useMemo, useState } from 'react'
import { addToast } from '../../stores/toastStore'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { CHANNEL_DEFS, type ChannelId } from './channelDefs'
import { ChannelCard, type ChannelConnectionState } from './ChannelCard'
import { QQConfigForm } from './QQConfigForm'
import { WeComConfigForm } from './WeComConfigForm'
import { ModuleConfigForm } from './ModuleConfigForm'
import { useChannelConfig } from './useChannelConfig'
import {
  PRESET_EXTERNAL_CHANNELS,
  PRESET_EXTERNAL_CHANNELS_BY_NAME,
  createPresetExternalDraft,
  type PresetExternalChannel
} from './presetExternalChannels'
import {
  ExternalChannelConfigForm,
  type ExternalChannelConfigWire
} from './ExternalChannelConfigForm'
import type { DiscoveredModule } from '../../../preload/api.d'

interface ChannelStatusWire {
  name: string
  category: string
  enabled: boolean
  running: boolean
}

interface ChannelInfoWire {
  name: string
}

type SelectedChannelKey = `native:${ChannelId}` | `module:${string}` | `external:${string}`

interface ExternalChannelViewModel {
  name: string
  draft: ExternalChannelConfigWire
  configured: boolean
  preset?: PresetExternalChannel
}

function moduleLogoPath(channelName: string): string {
  return new URL(`../../assets/channels/${channelName}.svg`, import.meta.url).toString()
}

function createEmptyExternalChannel(): ExternalChannelConfigWire {
  return {
    name: '',
    enabled: false,
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
  configured: boolean,
  statusMap: Map<string, ChannelStatusWire> | null,
  fallbackConnected: Set<string> | null
): ChannelConnectionState {
  if (statusMap !== null) {
    const s = statusMap.get(name.toLowerCase())
    if (!s) return configured && enabled ? 'enabledNotConnected' : 'notConfigured'
    if (s.running) return 'connected'
    if (s.enabled) return 'enabledNotConnected'
    return 'notConfigured'
  }

  const connected = fallbackConnected?.has(name.toLowerCase()) ?? false
  if (connected) return 'connected'
  return configured && enabled ? 'enabledNotConnected' : 'notConfigured'
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
  const [modules, setModules] = useState<DiscoveredModule[]>([])
  const [modulesLoading, setModulesLoading] = useState(false)
  const [modulesError, setModulesError] = useState<string | null>(null)
  const [moduleConfig, setModuleConfig] = useState<Record<string, unknown>>({})
  const [savingModule, setSavingModule] = useState(false)

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

  async function reloadModules(rescan = false): Promise<void> {
    setModulesLoading(true)
    setModulesError(null)
    try {
      const list = rescan ? await window.api.modules.rescan() : await window.api.modules.list()
      setModules(list)
    } catch (err) {
      setModules([])
      setModulesError(err instanceof Error ? err.message : String(err))
    } finally {
      setModulesLoading(false)
    }
  }

  async function loadModuleConfig(selectedModule: DiscoveredModule): Promise<void> {
    try {
      const result = await window.api.modules.readConfig({
        configFileName: selectedModule.configFileName
      })
      setModuleConfig(result.config ?? {})
    } catch (err) {
      setModuleConfig({})
      addToast(
        t('channels.loadFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  /**
   * When saving a new external channel, pass `selectedExternalNameOverride` so draft hydration
   * does not rely on stale `selectedChannelKey` (e.g. still `external:__new__` before React re-renders).
   */
  async function reloadExternalChannels(selectedExternalNameOverride?: string): Promise<void> {
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
        selectedExternalNameOverride !== undefined
          ? selectedExternalNameOverride
          : selectedChannelKey.startsWith('external:')
            ? selectedChannelKey.slice('external:'.length)
            : null
      if (selectedName && selectedName !== '__new__') {
        const selected = list.find((item) => item.name.toLowerCase() === selectedName.toLowerCase())
        if (selected) {
          setExternalDraft(cloneExternalChannel(selected))
        } else {
          const preset = PRESET_EXTERNAL_CHANNELS_BY_NAME.get(selectedName.toLowerCase())
          if (preset) {
            setExternalDraft(createPresetExternalDraft(preset.name))
          } else {
            setSelectedChannelKey('native:qq')
            setExternalDraft(createEmptyExternalChannel())
          }
        }
      }
    } catch (err) {
      setExternalChannels([])
      setExternalError(err instanceof Error ? err.message : String(err))
    } finally {
      setExternalLoading(false)
    }
  }

  const externalChannelCards = useMemo<ExternalChannelViewModel[]>(() => {
    if (!externalManagementEnabled) return []

    const persistedByName = new Map<string, ExternalChannelConfigWire>()
    for (const channel of externalChannels) {
      persistedByName.set(channel.name.toLowerCase(), channel)
    }

    const merged: ExternalChannelViewModel[] = []
    for (const preset of PRESET_EXTERNAL_CHANNELS) {
      const persisted = persistedByName.get(preset.name.toLowerCase())
      if (persisted) {
        merged.push({
          name: persisted.name,
          draft: cloneExternalChannel(persisted),
          configured: true,
          preset
        })
      } else {
        merged.push({
          name: preset.name,
          draft: createPresetExternalDraft(preset.name),
          configured: false,
          preset
        })
      }
    }

    for (const channel of externalChannels) {
      if (PRESET_EXTERNAL_CHANNELS_BY_NAME.has(channel.name.toLowerCase())) continue
      merged.push({
        name: channel.name,
        draft: cloneExternalChannel(channel),
        configured: true
      })
    }

    return merged
  }, [externalChannels, externalManagementEnabled])

  useEffect(() => {
    void reloadExternalChannels()
  }, [externalManagementEnabled])

  useEffect(() => {
    void reloadModules()
  }, [])

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

  async function handleSaveModule(selectedModule: DiscoveredModule): Promise<void> {
    setSavingModule(true)
    try {
      await window.api.modules.writeConfig({
        configFileName: selectedModule.configFileName,
        config: moduleConfig
      })
      addToast(t('channels.savedRestart'), 'success')
    } catch (err) {
      addToast(
        t('channels.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setSavingModule(false)
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

      const upsertRes = (await window.api.appServer.sendRequest('externalChannel/upsert', {
        channel: payload
      })) as { channel?: ExternalChannelConfigWire }
      const savedChannel = upsertRes.channel
        ? cloneExternalChannel(upsertRes.channel)
        : cloneExternalChannel(payload)

      setSelectedChannelKey(`external:${savedChannel.name}`)
      setExternalDraft(savedChannel)
      await reloadExternalChannels(savedChannel.name)
      addToast(t('channels.savedRestart'), 'success')
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
      if (!PRESET_EXTERNAL_CHANNELS_BY_NAME.has(name.toLowerCase())) {
        setSelectedChannelKey('native:qq')
        setExternalDraft(createEmptyExternalChannel())
      }
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
    for (const channel of externalChannelCards) {
      map.set(
        channel.name.toLowerCase(),
        deriveExternalStatus(
          channel.name,
          channel.draft.enabled,
          channel.configured,
          channelStatusMap,
          fallbackConnected
        )
      )
    }
    if (selectedChannelKey === 'external:__new__') {
      map.set(
        '__new__',
        deriveExternalStatus('__new__', externalDraft.enabled, false, channelStatusMap, fallbackConnected)
      )
    }
    return map
  }, [externalChannelCards, externalDraft.enabled, channelStatusMap, fallbackConnected, selectedChannelKey])

  const selectedNativeId = selectedChannelKey.startsWith('native:')
    ? (selectedChannelKey.slice('native:'.length) as ChannelId)
    : null
  const selectedModuleId = selectedChannelKey.startsWith('module:')
    ? selectedChannelKey.slice('module:'.length)
    : null
  const selectedExternalName = selectedChannelKey.startsWith('external:')
    ? selectedChannelKey.slice('external:'.length)
    : null
  const selectedDef = selectedNativeId ? CHANNEL_DEFS.find((d) => d.id === selectedNativeId) : null
  const selectedModule = selectedModuleId
    ? modules.find((item) => item.moduleId === selectedModuleId) ?? null
    : null
  const selectedModuleLogoPath =
    selectedModule && selectedModule.channelName
      ? moduleLogoPath(selectedModule.channelName)
      : undefined
  const selectedExternalCard = selectedExternalName
    ? externalChannelCards.find((item) => item.name.toLowerCase() === selectedExternalName.toLowerCase())
    : null

  useEffect(() => {
    if (selectedModuleId && !selectedModule) {
      setSelectedChannelKey('native:qq')
    }
  }, [selectedModuleId, selectedModule])

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
          <div style={{ fontSize: '11px', fontWeight: 700, color: 'var(--text-secondary)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: 8 }}>
            Native
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
            {CHANNEL_DEFS.map((channel) => {
              const status = statusById.get(channel.id) ?? 'notConfigured'
              return (
                <ChannelCard
                  key={channel.id}
                  logoPath={channel.logoPath}
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
                {t('channels.modules.group')}
              </div>
              <button
                type="button"
                onClick={() => {
                  void reloadModules(true)
                }}
                title={t('channels.modules.refresh')}
                aria-label={t('channels.modules.refresh')}
                style={{
                  border: 'none',
                  background: 'transparent',
                  color: 'var(--accent)',
                  cursor: 'pointer',
                  fontSize: '14px',
                  fontWeight: 700,
                  padding: 0
                }}
              >
                ↻
              </button>
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
              {modules.map((module) => {
                const status: ChannelConnectionState = 'notConfigured'
                return (
                  <ChannelCard
                    key={module.moduleId}
                    logoPath={moduleLogoPath(module.channelName)}
                    label={module.displayName}
                    status={status}
                    statusLabel={t(statusLabelKey(status))}
                    active={selectedChannelKey === `module:${module.moduleId}`}
                    onClick={() => {
                      setSelectedChannelKey(`module:${module.moduleId}`)
                      void loadModuleConfig(module)
                    }}
                  />
                )
              })}
            </div>
            {!modulesLoading && modules.length === 0 && (
              <div style={{ marginTop: 10, fontSize: '12px', color: 'var(--text-dimmed)' }}>
                {t('channels.modules.empty')}
              </div>
            )}
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
              {externalChannelCards.map((channel) => {
                const status = externalStatusByName.get(channel.name.toLowerCase()) ?? 'notConfigured'
                const label = channel.preset ? t(channel.preset.nameKey) : channel.name
                return (
                  <ChannelCard
                    key={channel.name}
                    logoPath={channel.preset?.logoPath}
                    label={label}
                    status={status}
                    statusLabel={t(statusLabelKey(status))}
                    active={selectedChannelKey === `external:${channel.name}`}
                    onClick={() => {
                      setExternalDraft(cloneExternalChannel(channel.draft))
                      setSelectedChannelKey(`external:${channel.name}`)
                    }}
                  />
                )
              })}
            </div>

            {!externalLoading && externalManagementEnabled && externalChannelCards.length === 0 && (
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

          {!loading && selectedModuleId && selectedModule && (
            <ModuleConfigForm
              module={selectedModule}
              config={moduleConfig}
              onChange={setModuleConfig}
              onSave={() => void handleSaveModule(selectedModule)}
              saving={savingModule}
              logoPath={selectedModuleLogoPath}
            />
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
                  logoPath={selectedExternalCard?.preset?.logoPath}
                  headerTitle={
                    selectedExternalCard?.preset ? t(selectedExternalCard.preset.titleKey) : undefined
                  }
                  status={
                    selectedExternalName === '__new__'
                      ? deriveExternalStatus(
                          '__new__',
                          externalDraft.enabled,
                          false,
                          channelStatusMap,
                          fallbackConnected
                        )
                      : externalStatusByName.get(selectedExternalName.toLowerCase()) ?? 'notConfigured'
                  }
                  statusLabel={t(
                    statusLabelKey(
                      selectedExternalName === '__new__'
                        ? deriveExternalStatus(
                            '__new__',
                            externalDraft.enabled,
                            false,
                            channelStatusMap,
                            fallbackConnected
                          )
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

          {(error || statusError || externalError || modulesError) && (
            <div style={{ marginTop: '16px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
              {error
                ? t('channels.loadFailed', { error })
                : modulesError
                  ? t('channels.loadFailed', { error: modulesError })
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
