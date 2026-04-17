import { useEffect, useMemo, useRef, useState } from 'react'
import { addToast } from '../../stores/toastStore'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { FolderIcon, RefreshIcon } from '../ui/AppIcons'
import { IconButton } from '../ui/IconButton'
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
import type {
  ConnectionMode,
  DiscoveredModule,
  ModulesRescanSummaryPayload,
  ModuleStatusEntry,
  ModuleStatusMap,
  QrUpdatePayload
} from '../../../preload/api.d'

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

interface ModuleQrState {
  active: boolean
  qrDataUrl: string | null
  timestamp: number
  successUntil?: number
}

type ModuleQrPhase = 'idle' | 'waitingForQr' | 'qrAvailable' | 'loginSuccess' | 'error'

interface ChannelModuleGroup {
  channelName: string
  activeModuleId: string
  modules: DiscoveredModule[]
}

function normalizeConnectionMode(mode: unknown): ConnectionMode {
  return mode === 'websocket' || mode === 'stdioAndWebSocket' || mode === 'remote'
    ? mode
    : 'stdio'
}

function isModuleWsAvailable(mode: ConnectionMode): boolean {
  return mode !== 'stdio'
}

function normalizeChannelName(value: string): string {
  return value.trim().toLowerCase()
}

function groupModulesByChannel(
  modules: DiscoveredModule[],
  activeModuleVariants: Record<string, string>
): ChannelModuleGroup[] {
  const byChannel = new Map<string, DiscoveredModule[]>()
  for (const module of modules) {
    const key = normalizeChannelName(module.channelName)
    const list = byChannel.get(key)
    if (list) list.push(module)
    else byChannel.set(key, [module])
  }

  const groups: ChannelModuleGroup[] = []
  for (const [channelKey, channelModules] of byChannel.entries()) {
    const persistedActiveModuleId = activeModuleVariants[channelKey]
    const persistedMatch =
      persistedActiveModuleId == null
        ? undefined
        : channelModules.find((module) => module.moduleId === persistedActiveModuleId)
    const userPreferred = channelModules.find((module) => module.source === 'user')
    const active = persistedMatch ?? userPreferred ?? channelModules[0]
    if (!active) continue
    groups.push({
      channelName: active.channelName,
      activeModuleId: active.moduleId,
      modules: channelModules
    })
  }
  return groups
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

function moduleStatusLabelKey(status: ChannelConnectionState): string {
  if (status === 'connecting') return 'channels.modules.connecting'
  if (status === 'error') return 'channels.modules.error'
  if (status === 'stopped') return 'channels.modules.stopped'
  return statusLabelKey(status)
}

function deriveModuleStatus(
  moduleId: string,
  statusMap: ModuleStatusMap,
  persistedEnabled: boolean
): ChannelConnectionState {
  const entry = statusMap[moduleId]
  if (!entry) return persistedEnabled ? 'stopped' : 'notConfigured'
  if (entry.processState === 'crashed') return 'error'
  if (entry.connected) return 'connected'
  if (entry.processState === 'starting') return 'connecting'
  if (entry.processState === 'running') return 'enabledNotConnected'
  if (entry.processState === 'stopped') return persistedEnabled ? 'stopped' : 'notConfigured'
  return 'notConfigured'
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

function getNestedValue(config: Record<string, unknown>, dottedKey: string): unknown {
  const parts = dottedKey.split('.').filter(Boolean)
  if (parts.length === 0) return undefined
  let current: unknown = config
  for (const part of parts) {
    if (current == null || typeof current !== 'object' || Array.isArray(current)) {
      return undefined
    }
    current = (current as Record<string, unknown>)[part]
  }
  return current
}

function setNestedValue(
  config: Record<string, unknown>,
  dottedKey: string,
  value: unknown
): Record<string, unknown> {
  const parts = dottedKey.split('.').filter(Boolean)
  if (parts.length === 0) return config
  const next: Record<string, unknown> = { ...config }
  let current: Record<string, unknown> = next
  for (let index = 0; index < parts.length - 1; index += 1) {
    const key = parts[index]
    const existing = current[key]
    const child =
      existing != null && typeof existing === 'object' && !Array.isArray(existing)
        ? { ...(existing as Record<string, unknown>) }
        : {}
    current[key] = child
    current = child
  }
  current[parts[parts.length - 1]] = value
  return next
}

function cloneDescriptorDefaultValue(value: unknown): unknown {
  if (value == null || typeof value !== 'object') return value
  try {
    return structuredClone(value)
  } catch {
    try {
      return JSON.parse(JSON.stringify(value)) as unknown
    } catch {
      return value
    }
  }
}

function seedConfigWithDescriptorDefaults(
  config: Record<string, unknown>,
  descriptors: DiscoveredModule['configDescriptors']
): Record<string, unknown> {
  let nextConfig = { ...config }
  for (const descriptor of descriptors) {
    if (descriptor.required !== true) continue
    if (descriptor.defaultValue === undefined) continue
    if (getNestedValue(nextConfig, descriptor.key) !== undefined) continue
    nextConfig = setNestedValue(
      nextConfig,
      descriptor.key,
      cloneDescriptorDefaultValue(descriptor.defaultValue)
    )
  }
  return nextConfig
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
  const [moduleStatusMap, setModuleStatusMap] = useState<ModuleStatusMap>({})
  const [moduleQrState, setModuleQrState] = useState<Record<string, ModuleQrState>>({})
  const [togglingModuleId, setTogglingModuleId] = useState<string | null>(null)
  const [variantSwitchingChannel, setVariantSwitchingChannel] = useState<string | null>(null)
  const [activeModuleVariants, setActiveModuleVariants] = useState<Record<string, string>>({})
  const [nodeRuntime, setNodeRuntime] = useState<{ available: boolean; version?: string }>(
    { available: false }
  )
  const [connectionMode, setConnectionMode] = useState<ConnectionMode>('stdio')
  const [moduleLogsById, setModuleLogsById] = useState<Record<string, string[]>>({})
  const [loadingLogsModuleId, setLoadingLogsModuleId] = useState<string | null>(null)
  const moduleConnectedSnapshotRef = useRef<Record<string, boolean>>({})
  const selectedNativeId = selectedChannelKey.startsWith('native:')
    ? (selectedChannelKey.slice('native:'.length) as ChannelId)
    : null
  const selectedModuleId = selectedChannelKey.startsWith('module:')
    ? selectedChannelKey.slice('module:'.length)
    : null
  const selectedExternalName = selectedChannelKey.startsWith('external:')
    ? selectedChannelKey.slice('external:'.length)
    : null

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

    window.api.settings
      .get()
      .then((settings) => {
        setConnectionMode(normalizeConnectionMode(settings.connectionMode))
        const raw = settings.activeModuleVariants
        if (raw != null && typeof raw === 'object' && !Array.isArray(raw)) {
          const normalized: Record<string, string> = {}
          for (const [key, value] of Object.entries(raw)) {
            if (typeof value !== 'string') continue
            const channelName = normalizeChannelName(key)
            const moduleId = value.trim()
            if (!channelName || !moduleId) continue
            normalized[channelName] = moduleId
          }
          setActiveModuleVariants(normalized)
        }
      })
      .catch(() => {})

    window.api.modules
      .nodeCheck()
      .then((status) => setNodeRuntime(status))
      .catch(() => setNodeRuntime({ available: false }))

    const onFocus = () => {
      void window.api.settings
        .get()
        .then((settings) => setConnectionMode(normalizeConnectionMode(settings.connectionMode)))
        .catch(() => {})
    }
    window.addEventListener('focus', onFocus)
    return () => {
      window.removeEventListener('focus', onFocus)
    }
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
      if (rescan && selectedModuleId) {
        const maybeSelected = list.find((module) => module.moduleId === selectedModuleId)
        if (maybeSelected) {
          await loadModuleConfig(maybeSelected)
        }
      }
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
      const baseConfig = result.config ?? {}
      setModuleConfig(seedConfigWithDescriptorDefaults(baseConfig, selectedModule.configDescriptors))
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

    const moduleChannelNames = new Set(modules.map((module) => module.channelName.toLowerCase()))
    const persistedByName = new Map<string, ExternalChannelConfigWire>()
    for (const channel of externalChannels) {
      persistedByName.set(channel.name.toLowerCase(), channel)
    }

    const merged: ExternalChannelViewModel[] = []
    for (const preset of PRESET_EXTERNAL_CHANNELS) {
      if (moduleChannelNames.has(preset.name.toLowerCase())) continue
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
      if (moduleChannelNames.has(channel.name.toLowerCase())) continue
      if (PRESET_EXTERNAL_CHANNELS_BY_NAME.has(channel.name.toLowerCase())) continue
      merged.push({
        name: channel.name,
        draft: cloneExternalChannel(channel),
        configured: true
      })
    }

    return merged
  }, [externalChannels, externalManagementEnabled, modules])

  useEffect(() => {
    void reloadExternalChannels()
  }, [externalManagementEnabled])

  useEffect(() => {
    void reloadModules()
  }, [])

  const moduleGroups = useMemo(
    () => groupModulesByChannel(modules, activeModuleVariants),
    [modules, activeModuleVariants]
  )
  const moduleById = useMemo(() => {
    const map = new Map<string, DiscoveredModule>()
    for (const module of modules) {
      map.set(module.moduleId, module)
    }
    return map
  }, [modules])

  useEffect(() => {
    const unsubscribe = window.api.modules.onRescanSummary((payload: ModulesRescanSummaryPayload) => {
      if (payload.changedRunningModuleIds.length === 0) return
      const labels = payload.changedRunningModuleIds
        .map((moduleId) => moduleById.get(moduleId)?.displayName ?? moduleId)
        .slice(0, 3)
      const labelText = labels.join(', ')
      const hasMore = payload.changedRunningModuleIds.length > labels.length
      addToast(
        t('channels.modules.updatedRestart', {
          names: hasMore ? `${labelText}...` : labelText
        }),
        'success'
      )
    })
    return () => {
      unsubscribe()
    }
  }, [moduleById, t])

  useEffect(() => {
    let disposed = false
    window.api.modules
      .running()
      .then((statusMap) => {
        if (!disposed) {
          const connectedSnapshot: Record<string, boolean> = {}
          for (const [moduleId, entry] of Object.entries(statusMap)) {
            connectedSnapshot[moduleId] = entry?.connected === true
          }
          moduleConnectedSnapshotRef.current = connectedSnapshot
          setModuleStatusMap(statusMap)
        }
      })
      .catch(() => {})

    const unsubscribe = window.api.modules.onStatusChanged((statusMap) => {
      if (disposed) return
      const previous = moduleConnectedSnapshotRef.current
      const nextSnapshot: Record<string, boolean> = {}
      const now = Date.now()
      setModuleQrState((prev) => {
        let changed = false
        const next = { ...prev }
        for (const [moduleId, entry] of Object.entries(statusMap)) {
          const isConnected = entry?.connected === true
          nextSnapshot[moduleId] = isConnected
          const wasConnected = previous[moduleId] === true
          if (!wasConnected && isConnected) {
            const current = next[moduleId]
            next[moduleId] = {
              active: current?.active ?? false,
              qrDataUrl: current?.qrDataUrl ?? null,
              timestamp: current?.timestamp ?? now,
              successUntil: now + 2_000
            }
            changed = true
          } else if (wasConnected && !isConnected && next[moduleId]?.successUntil !== undefined) {
            next[moduleId] = {
              ...next[moduleId],
              successUntil: undefined
            }
            changed = true
          }
        }
        return changed ? next : prev
      })
      moduleConnectedSnapshotRef.current = nextSnapshot
      setModuleStatusMap(statusMap)
    })
    const unsubscribeQr = window.api.modules.onQrUpdate((payload: QrUpdatePayload) => {
      if (disposed) return
      setModuleQrState((prev) => ({
        ...prev,
        [payload.moduleId]: {
          ...(prev[payload.moduleId] ?? {
            active: true,
            qrDataUrl: null,
            timestamp: payload.timestamp
          }),
          active: true,
          qrDataUrl: payload.qrDataUrl,
          timestamp: payload.timestamp
        }
      }))
    })
    return () => {
      disposed = true
      unsubscribe()
      unsubscribeQr()
    }
  }, [])

  useEffect(() => {
    if (!selectedModuleId) return
    let cancelled = false
    window.api.modules
      .qrStatus(selectedModuleId)
      .then((state) => {
        if (cancelled) return
        setModuleQrState((prev) => ({
          ...prev,
          [selectedModuleId]: {
            ...(prev[selectedModuleId] ?? {
              timestamp: Date.now(),
              successUntil: undefined
            }),
            active: state.active,
            qrDataUrl: state.qrDataUrl,
            timestamp: prev[selectedModuleId]?.timestamp ?? Date.now()
          }
        }))
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [selectedModuleId])

  useEffect(() => {
    const now = Date.now()
    const pending = Object.values(moduleQrState)
      .map((state) => state.successUntil ?? 0)
      .filter((value) => value > now)
    if (pending.length === 0) return
    const delay = Math.max(0, Math.min(...pending) - now + 30)
    const timer = setTimeout(() => {
      setModuleQrState((prev) => {
        const current = Date.now()
        let changed = false
        const next: Record<string, ModuleQrState> = {}
        for (const [moduleId, state] of Object.entries(prev)) {
          if (state.successUntil !== undefined && state.successUntil <= current) {
            next[moduleId] = { ...state, successUntil: undefined }
            changed = true
          } else {
            next[moduleId] = state
          }
        }
        return changed ? next : prev
      })
    }, delay)
    return () => clearTimeout(timer)
  }, [moduleQrState])

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
      const processState = moduleStatusMap[selectedModule.moduleId]?.processState
      const running = processState === 'starting' || processState === 'running'
      addToast(t(running ? 'channels.modules.configSavedRestart' : 'channels.savedRestart'), 'success')
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

  async function handleStartModule(moduleId: string): Promise<void> {
    let currentConnectionMode = connectionMode
    try {
      const latestSettings = await window.api.settings.get()
      currentConnectionMode = normalizeConnectionMode(latestSettings.connectionMode)
      setConnectionMode(currentConnectionMode)
    } catch {
      // Ignore settings read failure and use the latest known mode.
    }
    if (!isModuleWsAvailable(currentConnectionMode)) {
      addToast(t('channels.modules.wsRequired'), 'error')
      return
    }
    if (!nodeRuntime.available) {
      addToast(t('channels.modules.nodeMissing'), 'error')
      return
    }
    setTogglingModuleId(moduleId)
    try {
      const result = await window.api.modules.start({ moduleId })
      if (!result.ok) {
        if (result.missingFields && result.missingFields.length > 0) {
          addToast(
            t('channels.modules.missingRequired', {
              fields: result.missingFields.join(', ')
            }),
            'error'
          )
          return
        }
        addToast(
          t('channels.saveFailed', { error: result.error ?? 'Failed to start module process' }),
          'error'
        )
      }
    } catch (err) {
      addToast(
        t('channels.saveFailed', { error: err instanceof Error ? err.message : String(err) }),
        'error'
      )
    } finally {
      setTogglingModuleId((prev) => (prev === moduleId ? null : prev))
    }
  }

  async function handleStopModule(moduleId: string): Promise<void> {
    setTogglingModuleId(moduleId)
    try {
      const result = await window.api.modules.stop({ moduleId })
      if (!result.ok) {
        addToast(t('channels.saveFailed', { error: result.error ?? 'Failed to stop module process' }), 'error')
        return
      }
      await reloadExternalChannels()
    } catch (err) {
      addToast(
        t('channels.saveFailed', { error: err instanceof Error ? err.message : String(err) }),
        'error'
      )
    } finally {
      setTogglingModuleId((prev) => (prev === moduleId ? null : prev))
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

  const persistedModuleEnabledByChannelName = useMemo(() => {
    const enabledByChannel = new Map<string, boolean>()
    for (const channel of externalChannels) {
      enabledByChannel.set(
        channel.name.toLowerCase(),
        channel.enabled === true && channel.transport === 'websocket'
      )
    }
    return enabledByChannel
  }, [externalChannels])

  const selectedDef = selectedNativeId ? CHANNEL_DEFS.find((d) => d.id === selectedNativeId) : null
  const selectedModule = selectedModuleId ? moduleById.get(selectedModuleId) ?? null : null
  const selectedModuleGroup = selectedModule
    ? moduleGroups.find(
        (group) => normalizeChannelName(group.channelName) === normalizeChannelName(selectedModule.channelName)
      ) ?? null
    : null
  const selectedModuleVariants = selectedModuleGroup?.modules ?? []
  const selectedModuleStatus = selectedModule ? moduleStatusMap[selectedModule.moduleId] : undefined
  const selectedModuleQrState = selectedModuleId ? moduleQrState[selectedModuleId] : undefined
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

  const selectedModuleQrPhase: ModuleQrPhase = useMemo(() => {
    if (!selectedModule || !selectedModule.requiresInteractiveSetup) return 'idle'
    if (selectedModuleStatus?.processState === 'crashed') return 'error'
    if (
      selectedModuleQrState?.successUntil !== undefined &&
      selectedModuleQrState.successUntil > Date.now()
    ) {
      return 'loginSuccess'
    }
    if (selectedModuleStatus?.connected === true) return 'idle'
    const processRunning =
      selectedModuleStatus?.processState === 'starting' || selectedModuleStatus?.processState === 'running'
    if (!processRunning) return 'idle'
    if (selectedModuleQrState?.qrDataUrl) return 'qrAvailable'
    return 'waitingForQr'
  }, [selectedModule, selectedModuleStatus, selectedModuleQrState])

  async function handleSetActiveVariant(
    channelName: string,
    moduleId: string
  ): Promise<void> {
    const normalizedChannelName = normalizeChannelName(channelName)
    if (!normalizedChannelName || !moduleId) return
    setVariantSwitchingChannel(normalizedChannelName)
    try {
      const result = await window.api.modules.setActiveVariant({
        channelName,
        moduleId
      })
      if (!result.ok) {
        addToast(
          t('channels.saveFailed', {
            error: result.error ?? 'Failed to switch module variant'
          }),
          'error'
        )
        return
      }
      setActiveModuleVariants((prev) => ({
        ...prev,
        [normalizedChannelName]: moduleId
      }))
      setSelectedChannelKey(`module:${moduleId}`)
      const nextModule = moduleById.get(moduleId)
      if (nextModule) {
        await loadModuleConfig(nextModule)
      }
    } catch (err) {
      addToast(
        t('channels.saveFailed', { error: err instanceof Error ? err.message : String(err) }),
        'error'
      )
    } finally {
      setVariantSwitchingChannel((prev) =>
        prev === normalizedChannelName ? null : prev
      )
    }
  }

  async function handleLoadModuleLogs(moduleId: string): Promise<void> {
    setLoadingLogsModuleId(moduleId)
    try {
      const result = await window.api.modules.getLogs(moduleId)
      setModuleLogsById((prev) => ({ ...prev, [moduleId]: result.lines ?? [] }))
    } catch {
      // Keep silent; logs are diagnostics only.
    } finally {
      setLoadingLogsModuleId((prev) => (prev === moduleId ? null : prev))
    }
  }

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
              <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                <IconButton
                  icon={<RefreshIcon size={16} />}
                  label={t('channels.modules.refresh')}
                  onClick={() => {
                    void reloadModules(true)
                  }}
                  size={30}
                />
                <IconButton
                  icon={<FolderIcon size={16} />}
                  label={t('channels.modules.openFolder')}
                  onClick={() => {
                    void window.api.modules.openFolder().then((result) => {
                      if (!result.ok) {
                        addToast(
                          t('channels.saveFailed', {
                            error: result.error ?? 'Failed to open modules folder'
                          }),
                          'error'
                        )
                      }
                    })
                  }}
                  size={30}
                />
              </div>
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
              {moduleGroups.map((group) => {
                const module = moduleById.get(group.activeModuleId)
                if (!module) return null
                const persistedEnabled =
                  persistedModuleEnabledByChannelName.get(module.channelName.toLowerCase()) === true
                const status = deriveModuleStatus(module.moduleId, moduleStatusMap, persistedEnabled)
                return (
                  <ChannelCard
                    key={group.channelName}
                    logoPath={moduleLogoPath(module.channelName)}
                    label={module.displayName}
                    badgeText={group.modules.length > 1 ? `${group.modules.length}` : undefined}
                    status={status}
                    statusLabel={t(moduleStatusLabelKey(status))}
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
          {!nodeRuntime.available && (
            <div
              style={{
                marginBottom: '12px',
                border: '1px solid rgba(255, 159, 10, 0.45)',
                backgroundColor: 'rgba(255, 159, 10, 0.12)',
                borderRadius: '8px',
                padding: '10px 12px',
                fontSize: '12px',
                color: 'var(--warning, #ff9f0a)'
              }}
            >
              {t('channels.modules.nodeMissing')}
            </div>
          )}

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
              variantModules={selectedModuleVariants}
              onVariantChange={(nextModuleId) => {
                if (!selectedModule) return
                void handleSetActiveVariant(selectedModule.channelName, nextModuleId)
              }}
              variantSwitching={
                selectedModule
                  ? variantSwitchingChannel === normalizeChannelName(selectedModule.channelName)
                  : false
              }
              config={moduleConfig}
              onChange={setModuleConfig}
              onSave={() => void handleSaveModule(selectedModule)}
              saving={savingModule}
              logoPath={selectedModuleLogoPath}
              moduleStatus={selectedModuleStatus as ModuleStatusEntry | undefined}
              persistedEnabled={
                persistedModuleEnabledByChannelName.get(selectedModule.channelName.toLowerCase()) === true
              }
              nodeAvailable={nodeRuntime.available}
              wsAvailable={isModuleWsAvailable(connectionMode)}
              onStart={() => {
                void handleStartModule(selectedModule.moduleId)
              }}
              onStop={() => {
                void handleStopModule(selectedModule.moduleId)
              }}
              starting={togglingModuleId === selectedModule.moduleId}
              qrDataUrl={selectedModuleQrState?.qrDataUrl ?? null}
              qrPhase={selectedModuleQrPhase}
              moduleLogLines={moduleLogsById[selectedModule.moduleId] ?? []}
              logsLoading={loadingLogsModuleId === selectedModule.moduleId}
              onLoadLogs={() => {
                void handleLoadModuleLogs(selectedModule.moduleId)
              }}
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
