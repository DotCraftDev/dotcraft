import { useEffect, useMemo, useRef, useState, type CSSProperties, type Dispatch, type JSX, type SetStateAction } from 'react'
import { addToast } from '../../stores/toastStore'
import { applyTheme, resolveTheme, type ThemeMode } from '../../utils/theme'
import { normalizeLocale, type AppLocale } from '../../../shared/locales'
import { useSetUiLocale, useT } from '../../contexts/LocaleContext'
import type { MessageKey } from '../../../shared/locales'
import { ensureVisibleChannelsSeeded } from '../../utils/visibleChannelsDefaults'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import {
  useMcpStore,
  type McpServerConfigWire,
  type McpServerStatusWire,
  type McpTransport
} from '../../stores/mcpStore'

declare const __APP_VERSION__: string | undefined

interface ChannelInfoWire {
  name: string
  category: string
}

interface SettingsViewProps {
  onVisibleChannelsUpdated?: () => void
}

interface KeyValueRow {
  id: string
  key: string
  value: string
}

interface ValueRow {
  id: string
  value: string
}

interface McpTestResultWire {
  success: boolean
  errorCode?: string
  errorMessage?: string
  toolCount?: number
}

type ConnectionMode = 'stdio' | 'websocket' | 'stdioAndWebSocket' | 'remote'
type SettingsTab = 'general' | 'connection' | 'channels' | 'mcp'

const CATEGORY_ORDER = ['builtin', 'social', 'system', 'external'] as const
const DEFAULT_WS_HOST = '127.0.0.1'
const DEFAULT_WS_PORT = 9100

const CATEGORY_LABEL_KEY: Record<string, MessageKey> = {
  builtin: 'settings.channelCategory.builtin',
  social: 'settings.channelCategory.social',
  system: 'settings.channelCategory.system',
  external: 'settings.channelCategory.external'
}

function formatChannelChipLabel(name: string): string {
  return name.toUpperCase()
}

function createRowId(prefix: string): string {
  return `${prefix}-${Math.random().toString(36).slice(2, 10)}`
}

function createEmptyMcpServer(): McpServerConfigWire {
  return {
    name: '',
    enabled: true,
    transport: 'stdio',
    command: '',
    args: [],
    env: {},
    envVars: [],
    cwd: '',
    url: '',
    bearerTokenEnvVar: '',
    httpHeaders: {},
    envHttpHeaders: {},
    startupTimeoutSec: null,
    toolTimeoutSec: null
  }
}

function normalizeValueRows(values?: string[] | null): ValueRow[] {
  if (!values || values.length === 0) return [{ id: createRowId('value'), value: '' }]
  return values.map((value) => ({ id: createRowId('value'), value }))
}

function normalizeKeyValueRows(values?: Record<string, string> | null): KeyValueRow[] {
  const entries = Object.entries(values ?? {})
  if (entries.length === 0) {
    return [{ id: createRowId('kv'), key: '', value: '' }]
  }
  return entries.map(([key, value]) => ({
    id: createRowId('kv'),
    key,
    value
  }))
}

function rowsToValues(rows: ValueRow[]): string[] {
  return rows.map((row) => row.value.trim()).filter((value) => value.length > 0)
}

function rowsToRecord(rows: KeyValueRow[]): Record<string, string> {
  const record: Record<string, string> = {}
  for (const row of rows) {
    const key = row.key.trim()
    const value = row.value.trim()
    if (key.length > 0 && value.length > 0) {
      record[key] = value
    }
  }
  return record
}

function getStatusTone(status?: McpServerStatusWire): { label: string; color: string } {
  switch (status?.startupState) {
    case 'ready':
      return { label: 'Ready', color: '#3fb950' }
    case 'starting':
      return { label: 'Starting', color: '#d29922' }
    case 'error':
      return { label: 'Error', color: '#f85149' }
    case 'disabled':
      return { label: 'Disabled', color: 'var(--text-dimmed)' }
    default:
      return { label: 'Idle', color: 'var(--text-dimmed)' }
  }
}

function cardStyle(): CSSProperties {
  return {
    border: '1px solid var(--border-default)',
    borderRadius: '10px',
    background: 'var(--bg-secondary)',
    padding: '14px'
  }
}

function sectionLabelStyle(): CSSProperties {
  return {
    display: 'block',
    fontSize: '12px',
    fontWeight: 600,
    color: 'var(--text-secondary)',
    marginBottom: '6px'
  }
}

function inputStyle(mono = false): CSSProperties {
  return {
    width: '100%',
    boxSizing: 'border-box',
    padding: '8px 10px',
    fontSize: '13px',
    borderRadius: '8px',
    border: '1px solid var(--border-default)',
    background: 'var(--bg-primary)',
    color: 'var(--text-primary)',
    outline: 'none',
    fontFamily: mono ? 'var(--font-mono)' : undefined
  }
}

function secondaryButtonStyle(disabled = false): CSSProperties {
  return {
    padding: '8px 14px',
    border: '1px solid var(--border-default)',
    borderRadius: '8px',
    background: 'transparent',
    color: 'var(--text-primary)',
    fontSize: '13px',
    fontWeight: 500,
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.7 : 1
  }
}

function primaryButtonStyle(disabled = false): CSSProperties {
  return {
    padding: '8px 14px',
    border: 'none',
    borderRadius: '8px',
    background: 'var(--accent)',
    color: 'var(--on-accent)',
    fontSize: '13px',
    fontWeight: 600,
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.7 : 1
  }
}

function EditableValueList({
  rows,
  setRows,
  placeholder
}: {
  rows: ValueRow[]
  setRows: Dispatch<SetStateAction<ValueRow[]>>
  placeholder: string
}): JSX.Element {
  function updateRow(id: string, value: string): void {
    setRows((prev) => prev.map((row) => (row.id === id ? { ...row, value } : row)))
  }

  function addRow(): void {
    setRows((prev) => [...prev, { id: createRowId('value'), value: '' }])
  }

  function removeRow(id: string): void {
    setRows((prev) =>
      prev.length <= 1 ? [{ id: createRowId('value'), value: '' }] : prev.filter((row) => row.id !== id)
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
      {rows.map((row) => (
        <div key={row.id} style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: '8px' }}>
          <input
            type="text"
            value={row.value}
            onChange={(e) => updateRow(row.id, e.target.value)}
            placeholder={placeholder}
            style={inputStyle(true)}
          />
          <button type="button" onClick={() => removeRow(row.id)} style={secondaryButtonStyle(false)}>
            Remove
          </button>
        </div>
      ))}
      <button type="button" onClick={addRow} style={secondaryButtonStyle(false)}>
        + Add
      </button>
    </div>
  )
}

function EditableKeyValueList({
  rows,
  setRows,
  keyPlaceholder,
  valuePlaceholder
}: {
  rows: KeyValueRow[]
  setRows: Dispatch<SetStateAction<KeyValueRow[]>>
  keyPlaceholder: string
  valuePlaceholder: string
}): JSX.Element {
  function updateRow(id: string, nextKey: string, nextValue: string): void {
    setRows((prev) =>
      prev.map((row) => (row.id === id ? { ...row, key: nextKey, value: nextValue } : row))
    )
  }

  function addRow(): void {
    setRows((prev) => [...prev, { id: createRowId('kv'), key: '', value: '' }])
  }

  function removeRow(id: string): void {
    setRows((prev) =>
      prev.length <= 1 ? [{ id: createRowId('kv'), key: '', value: '' }] : prev.filter((row) => row.id !== id)
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
      {rows.map((row) => (
        <div key={row.id} style={{ display: 'grid', gridTemplateColumns: '1fr 1fr auto', gap: '8px' }}>
          <input
            type="text"
            value={row.key}
            onChange={(e) => updateRow(row.id, e.target.value, row.value)}
            placeholder={keyPlaceholder}
            style={inputStyle(true)}
          />
          <input
            type="text"
            value={row.value}
            onChange={(e) => updateRow(row.id, row.key, e.target.value)}
            placeholder={valuePlaceholder}
            style={inputStyle(true)}
          />
          <button type="button" onClick={() => removeRow(row.id)} style={secondaryButtonStyle(false)}>
            Remove
          </button>
        </div>
      ))}
      <button type="button" onClick={addRow} style={secondaryButtonStyle(false)}>
        + Add
      </button>
    </div>
  )
}

export function SettingsView({ onVisibleChannelsUpdated }: SettingsViewProps): JSX.Element {
  const t = useT()
  const setUiLocale = useSetUiLocale()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const mcpStatuses = useMcpStore((s) => s.statuses)
  const setMcpStatuses = useMcpStore((s) => s.setStatuses)
  const [binaryPath, setBinaryPath] = useState('')
  const [connectionMode, setConnectionMode] = useState<ConnectionMode>('stdio')
  const [wsHost, setWsHost] = useState(DEFAULT_WS_HOST)
  const [wsPort, setWsPort] = useState(String(DEFAULT_WS_PORT))
  const [remoteUrl, setRemoteUrl] = useState('')
  const [remoteToken, setRemoteToken] = useState('')
  const [theme, setTheme] = useState<ThemeMode>('dark')
  const [locale, setLocale] = useState<AppLocale>(normalizeLocale(undefined))
  const [version, setVersion] = useState('')
  const [saving, setSaving] = useState(false)
  const [restartingAppServer, setRestartingAppServer] = useState(false)
  const [visibleChannels, setVisibleChannels] = useState<string[]>([])
  const [serverChannels, setServerChannels] = useState<ChannelInfoWire[] | null>(null)
  const [channelListError, setChannelListError] = useState(false)
  const [activeSettingsTab, setActiveSettingsTab] = useState<SettingsTab>('general')
  const inputRef = useRef<HTMLInputElement>(null)

  const [mcpServers, setMcpServers] = useState<McpServerConfigWire[]>([])
  const [mcpLoading, setMcpLoading] = useState(false)
  const [mcpError, setMcpError] = useState<string | null>(null)
  const [editingServerName, setEditingServerName] = useState<string | null>(null)
  const [mcpDraft, setMcpDraft] = useState<McpServerConfigWire>(createEmptyMcpServer())
  const [argRows, setArgRows] = useState<ValueRow[]>(normalizeValueRows([]))
  const [envRows, setEnvRows] = useState<KeyValueRow[]>(normalizeKeyValueRows({}))
  const [envVarRows, setEnvVarRows] = useState<ValueRow[]>(normalizeValueRows([]))
  const [httpHeaderRows, setHttpHeaderRows] = useState<KeyValueRow[]>(normalizeKeyValueRows({}))
  const [envHttpHeaderRows, setEnvHttpHeaderRows] = useState<KeyValueRow[]>(normalizeKeyValueRows({}))
  const [testingMcp, setTestingMcp] = useState(false)
  const [savingMcp, setSavingMcp] = useState(false)
  const [deletingMcp, setDeletingMcp] = useState(false)
  const [mcpTestResult, setMcpTestResult] = useState<McpTestResultWire | null>(null)

  const mcpEnabled = capabilities?.mcpManagement === true
  const canRestartManagedAppServer = connectionMode !== 'remote'

  useEffect(() => {
    inputRef.current?.focus()
    window.api.settings
      .get()
      .then(async (s) => {
        setBinaryPath(s.appServerBinaryPath ?? '')
        setConnectionMode((s.connectionMode ?? 'stdio') as ConnectionMode)
        setWsHost(s.webSocket?.host ?? DEFAULT_WS_HOST)
        setWsPort(String(s.webSocket?.port ?? DEFAULT_WS_PORT))
        setRemoteUrl(s.remote?.url ?? '')
        setRemoteToken(s.remote?.token ?? '')
        setTheme(resolveTheme(s.theme))
        setLocale(normalizeLocale(s.locale))
        setVisibleChannels(await ensureVisibleChannelsSeeded(s))
      })
      .catch(() => {})
    setVersion(typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : '0.1.0')
  }, [])

  useEffect(() => {
    let cancelled = false
    setServerChannels(null)
    setChannelListError(false)
    window.api.appServer
      .sendRequest('channel/list', {})
      .then((res) => {
        if (cancelled) return
        const r = res as { channels?: ChannelInfoWire[] }
        setServerChannels(r.channels ?? [])
        setChannelListError(false)
      })
      .catch(() => {
        if (!cancelled) {
          setServerChannels([])
          setChannelListError(true)
        }
      })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    if (!mcpEnabled) return
    let cancelled = false

    async function loadMcpData(): Promise<void> {
      setMcpLoading(true)
      setMcpError(null)
      try {
        const [listRes, statusRes] = await Promise.all([
          window.api.appServer.sendRequest('mcp/list', {}),
          window.api.appServer.sendRequest('mcp/status/list', {})
        ])
        if (cancelled) return
        const list = (listRes as { servers?: McpServerConfigWire[] }).servers ?? []
        const statuses = (statusRes as { servers?: McpServerStatusWire[] }).servers ?? []
        setMcpServers(list)
        setMcpStatuses(statuses)
      } catch (err) {
        if (!cancelled) {
          setMcpServers([])
          setMcpError(err instanceof Error ? err.message : String(err))
        }
      } finally {
        if (!cancelled) {
          setMcpLoading(false)
        }
      }
    }

    void loadMcpData()
    return () => {
      cancelled = true
    }
  }, [mcpEnabled, setMcpStatuses])

  const channelsByCategory = useMemo(() => {
    const list = serverChannels ?? []
    const map = new Map<string, ChannelInfoWire[]>()
    for (const c of list) {
      const cat = c.category || 'builtin'
      const arr = map.get(cat) ?? []
      arr.push(c)
      map.set(cat, arr)
    }
    return map
  }, [serverChannels])

  const mergedMcpServers = useMemo(() => {
    return [...mcpServers].sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }))
  }, [mcpServers])

  function closeSettings(): void {
    setActiveMainView('conversation')
  }

  function startMcpDraft(server?: McpServerConfigWire): void {
    const next = server
      ? {
          ...createEmptyMcpServer(),
          ...server,
          args: [...(server.args ?? [])],
          env: { ...(server.env ?? {}) },
          envVars: [...(server.envVars ?? [])],
          httpHeaders: { ...(server.httpHeaders ?? {}) },
          envHttpHeaders: { ...(server.envHttpHeaders ?? {}) }
        }
      : createEmptyMcpServer()
    setEditingServerName(server?.name ?? '__new__')
    setMcpDraft(next)
    setArgRows(normalizeValueRows(next.args))
    setEnvRows(normalizeKeyValueRows(next.env))
    setEnvVarRows(normalizeValueRows(next.envVars))
    setHttpHeaderRows(normalizeKeyValueRows(next.httpHeaders))
    setEnvHttpHeaderRows(normalizeKeyValueRows(next.envHttpHeaders))
    setMcpTestResult(null)
  }

  function cancelMcpEdit(): void {
    setEditingServerName(null)
    setMcpDraft(createEmptyMcpServer())
    setArgRows(normalizeValueRows([]))
    setEnvRows(normalizeKeyValueRows({}))
    setEnvVarRows(normalizeValueRows([]))
    setHttpHeaderRows(normalizeKeyValueRows({}))
    setEnvHttpHeaderRows(normalizeKeyValueRows({}))
    setMcpTestResult(null)
  }

  function buildDraftPayload(): McpServerConfigWire {
    const transport = mcpDraft.transport
    return {
      name: mcpDraft.name.trim(),
      enabled: mcpDraft.enabled,
      transport,
      command: transport === 'stdio' ? mcpDraft.command?.trim() ?? '' : null,
      args: transport === 'stdio' ? rowsToValues(argRows) : null,
      env: transport === 'stdio' ? rowsToRecord(envRows) : null,
      envVars: transport === 'stdio' ? rowsToValues(envVarRows) : null,
      cwd: transport === 'stdio' ? (mcpDraft.cwd?.trim() || null) : null,
      url: transport === 'streamableHttp' ? mcpDraft.url?.trim() ?? '' : null,
      bearerTokenEnvVar:
        transport === 'streamableHttp' ? mcpDraft.bearerTokenEnvVar?.trim() || null : null,
      httpHeaders: transport === 'streamableHttp' ? rowsToRecord(httpHeaderRows) : null,
      envHttpHeaders: transport === 'streamableHttp' ? rowsToRecord(envHttpHeaderRows) : null,
      startupTimeoutSec: mcpDraft.startupTimeoutSec ?? null,
      toolTimeoutSec: mcpDraft.toolTimeoutSec ?? null
    }
  }

  async function reloadMcpServers(): Promise<void> {
    const listRes = await window.api.appServer.sendRequest('mcp/list', {})
    const list = (listRes as { servers?: McpServerConfigWire[] }).servers ?? []
    setMcpServers(list)
  }

  async function reloadMcpStatuses(): Promise<void> {
    const statusRes = await window.api.appServer.sendRequest('mcp/status/list', {})
    const statuses = (statusRes as { servers?: McpServerStatusWire[] }).servers ?? []
    setMcpStatuses(statuses)
  }

  async function handleMcpTest(): Promise<void> {
    const payload = buildDraftPayload()
    setTestingMcp(true)
    setMcpTestResult(null)
    try {
      const result = (await window.api.appServer.sendRequest('mcp/test', {
        server: payload
      })) as McpTestResultWire
      setMcpTestResult(result)
      addToast(
        result.success
          ? `MCP connection test succeeded${typeof result.toolCount === 'number' ? ` (${result.toolCount} tools)` : ''}`
          : `MCP connection test failed${result.errorMessage ? `: ${result.errorMessage}` : ''}`,
        result.success ? 'success' : 'error'
      )
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err)
      setMcpTestResult({ success: false, errorMessage: message })
      addToast(`MCP connection test failed: ${message}`, 'error')
    } finally {
      setTestingMcp(false)
    }
  }

  async function handleMcpSave(): Promise<void> {
    const payload = buildDraftPayload()
    setSavingMcp(true)
    try {
      const originalName = editingServerName !== '__new__' ? editingServerName?.trim() ?? null : null
      const nextName = payload.name.trim()
      const isRename =
        originalName !== null &&
        originalName.localeCompare(nextName, undefined, { sensitivity: 'accent' }) !== 0

      let renameCleanupFailed = false
      if (isRename) {
        try {
          await window.api.appServer.sendRequest('mcp/remove', { name: originalName })
        } catch (err) {
          renameCleanupFailed = true
          console.warn('Failed to remove old MCP server before rename save', err)
        }
      }

      await window.api.appServer.sendRequest('mcp/upsert', { server: payload })
      await Promise.all([reloadMcpServers(), reloadMcpStatuses()])
      addToast('MCP server saved', 'success')
      if (renameCleanupFailed) {
        addToast('MCP server saved, but the old server entry may still exist', 'error')
      }
      cancelMcpEdit()
    } catch (err) {
      addToast(`Failed to save MCP server: ${err instanceof Error ? err.message : String(err)}`, 'error')
    } finally {
      setSavingMcp(false)
    }
  }

  async function handleMcpDelete(): Promise<void> {
    const name = mcpDraft.name.trim()
    if (!name) return
    setDeletingMcp(true)
    try {
      await window.api.appServer.sendRequest('mcp/remove', { name })
      await Promise.all([reloadMcpServers(), reloadMcpStatuses()])
      addToast('MCP server removed', 'success')
      cancelMcpEdit()
    } catch (err) {
      addToast(`Failed to remove MCP server: ${err instanceof Error ? err.message : String(err)}`, 'error')
    } finally {
      setDeletingMcp(false)
    }
  }

  async function handleThemeChange(next: ThemeMode): Promise<void> {
    setTheme(next)
    applyTheme(next)
    try {
      await window.api.settings.set({ theme: next })
    } catch (err) {
      addToast(
        t('settings.saveThemeFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  async function handleLocaleChange(next: AppLocale): Promise<void> {
    const normalized = normalizeLocale(next)
    const prev = locale
    setLocale(normalized)
    try {
      await window.api.settings.set({ locale: normalized })
      setUiLocale(normalized)
    } catch (err) {
      setLocale(prev)
      addToast(
        t('settings.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  async function setVisibleChannelsAndPersist(next: string[]): Promise<void> {
    setVisibleChannels(next)
    try {
      await window.api.settings.set({ visibleChannels: next })
      onVisibleChannelsUpdated?.()
    } catch (err) {
      addToast(
        t('settings.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  function toggleVisibleChannel(channel: string, checked: boolean): void {
    const next = checked
      ? Array.from(new Set([...visibleChannels, channel]))
      : visibleChannels.filter((c) => c !== channel)
    void setVisibleChannelsAndPersist(next)
  }

  async function handleSave(): Promise<void> {
    setSaving(true)
    try {
      const parsedPort = Number.parseInt(wsPort.trim(), 10)
      const normalizedPort =
        Number.isInteger(parsedPort) && parsedPort > 0 && parsedPort <= 65535
          ? parsedPort
          : DEFAULT_WS_PORT
      await window.api.settings.set({
        appServerBinaryPath: binaryPath.trim() || undefined,
        connectionMode,
        webSocket: {
          host: wsHost.trim() || DEFAULT_WS_HOST,
          port: normalizedPort
        },
        remote: {
          url: remoteUrl.trim() || undefined,
          token: remoteToken.trim() || undefined
        }
      })
      addToast(
        activeSettingsTab === 'connection'
          ? t('settings.restartAppServerSavedHint')
          : t('settings.savedToast'),
        'success'
      )
      if (activeSettingsTab !== 'connection') {
        closeSettings()
      }
    } catch (err) {
      addToast(
        t('settings.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setSaving(false)
    }
  }

  async function handleRestartManagedAppServer(): Promise<void> {
    setRestartingAppServer(true)
    try {
      await window.api.appServer.restartManaged()
      addToast(t('settings.restartAppServerSuccess'), 'success')
    } catch (err) {
      addToast(
        t('settings.restartAppServerFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setRestartingAppServer(false)
    }
  }

  const tabs: Array<{ id: SettingsTab; label: string }> = [
    { id: 'general', label: t('settings.tab.general') },
    { id: 'connection', label: t('settings.tab.connection') },
    { id: 'channels', label: t('settings.tab.channels') }
  ]
  if (mcpEnabled) {
    tabs.push({ id: 'mcp', label: 'MCP' })
  }

  return (
    <div
      aria-label={t('settings.title')}
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
          {t('settings.title')}
        </h1>
        <button
          type="button"
          onClick={closeSettings}
          title={t('settings.close')}
          aria-label={t('settings.closeAria')}
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
            width: '170px',
            borderRight: '1px solid var(--border-default)',
            backgroundColor: 'var(--bg-secondary)',
            padding: '12px 0',
            flexShrink: 0
          }}
        >
          {tabs.map((tab) => {
            const active = activeSettingsTab === tab.id
            return (
              <button
                key={tab.id}
                type="button"
                onClick={() => setActiveSettingsTab(tab.id)}
                style={{
                  width: '100%',
                  textAlign: 'left',
                  padding: '10px 14px',
                  border: 'none',
                  background: active ? 'var(--bg-tertiary)' : 'transparent',
                  borderLeft: active ? '3px solid var(--accent)' : '3px solid transparent',
                  color: active ? 'var(--text-primary)' : 'var(--text-secondary)',
                  fontSize: '13px',
                  fontWeight: active ? 600 : 500,
                  cursor: 'pointer'
                }}
              >
                {tab.label}
              </button>
            )
          })}
        </aside>

        <main style={{ flex: 1, minWidth: 0, overflowY: 'auto', padding: '20px' }}>
          <div style={{ maxWidth: activeSettingsTab === 'mcp' ? '760px' : '560px' }}>
            {activeSettingsTab === 'general' && (
              <>
                <div style={{ marginBottom: '16px' }}>
                  <label htmlFor="settings-language" style={sectionLabelStyle()}>
                    {t('settings.language')}
                  </label>
                  <select
                    id="settings-language"
                    value={locale}
                    onChange={(e) => {
                      void handleLocaleChange(e.target.value as AppLocale)
                    }}
                    style={{ ...inputStyle(), width: '180px', cursor: 'pointer' }}
                  >
                    <option value="en">{t('settings.language.en')}</option>
                    <option value="zh-Hans">{t('settings.language.zhHans')}</option>
                  </select>
                </div>

                <div style={{ marginBottom: '16px' }}>
                  <label htmlFor="settings-theme" style={sectionLabelStyle()}>
                    {t('settings.theme')}
                  </label>
                  <select
                    id="settings-theme"
                    value={theme}
                    onChange={(e) => {
                      void handleThemeChange(e.target.value as ThemeMode)
                    }}
                    style={{ ...inputStyle(), width: '180px', cursor: 'pointer' }}
                  >
                    <option value="dark">{t('settings.optionThemeDark')}</option>
                    <option value="light">{t('settings.optionThemeLight')}</option>
                  </select>
                </div>

                <div style={{ fontSize: '12px', color: 'var(--text-dimmed)' }}>
                  DotCraft Desktop {t('settings.version')} {version}
                </div>
              </>
            )}

            {activeSettingsTab === 'connection' && (
              <>
                <div style={{ marginBottom: '16px' }}>
                  <label htmlFor="settings-connection-mode" style={sectionLabelStyle()}>
                    {t('settings.connectionMode')}
                  </label>
                  <select
                    id="settings-connection-mode"
                    value={connectionMode}
                    onChange={(e) => {
                      setConnectionMode(e.target.value as ConnectionMode)
                    }}
                    style={{ ...inputStyle(), cursor: 'pointer' }}
                  >
                    <option value="stdio">{t('settings.connectionMode.stdio')}</option>
                    <option value="websocket">{t('settings.connectionMode.websocket')}</option>
                    <option value="stdioAndWebSocket">{t('settings.connectionMode.stdioAndWebSocket')}</option>
                    <option value="remote">{t('settings.connectionMode.remote')}</option>
                  </select>
                  <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
                    {t('settings.connectionModeHint')}
                  </div>
                </div>

                {(connectionMode === 'websocket' || connectionMode === 'stdioAndWebSocket') && (
                  <div style={{ marginBottom: '16px', display: 'grid', gridTemplateColumns: '1fr 120px', gap: '8px' }}>
                    <div>
                      <label htmlFor="settings-ws-host" style={sectionLabelStyle()}>
                        {t('settings.wsHost')}
                      </label>
                      <input
                        id="settings-ws-host"
                        type="text"
                        value={wsHost}
                        onChange={(e) => setWsHost(e.target.value)}
                        placeholder={DEFAULT_WS_HOST}
                        style={inputStyle(true)}
                      />
                    </div>
                    <div>
                      <label htmlFor="settings-ws-port" style={sectionLabelStyle()}>
                        {t('settings.wsPort')}
                      </label>
                      <input
                        id="settings-ws-port"
                        type="number"
                        value={wsPort}
                        onChange={(e) => setWsPort(e.target.value)}
                        placeholder={String(DEFAULT_WS_PORT)}
                        min={1}
                        max={65535}
                        style={inputStyle(true)}
                      />
                    </div>
                  </div>
                )}

                {connectionMode === 'remote' && (
                  <div style={{ marginBottom: '16px' }}>
                    <label htmlFor="settings-remote-url" style={sectionLabelStyle()}>
                      {t('settings.remoteUrl')}
                    </label>
                    <input
                      id="settings-remote-url"
                      type="text"
                      value={remoteUrl}
                      onChange={(e) => setRemoteUrl(e.target.value)}
                      placeholder="ws://127.0.0.1:9100/ws"
                      style={inputStyle(true)}
                    />
                    <label htmlFor="settings-remote-token" style={{ ...sectionLabelStyle(), marginTop: '10px' }}>
                      {t('settings.remoteToken')}
                    </label>
                    <input
                      id="settings-remote-token"
                      type="password"
                      value={remoteToken}
                      onChange={(e) => setRemoteToken(e.target.value)}
                      placeholder={t('settings.remoteTokenPlaceholder')}
                      style={inputStyle(true)}
                    />
                  </div>
                )}

                <div style={{ marginBottom: '16px' }}>
                  <label htmlFor="settings-binary-path" style={sectionLabelStyle()}>
                    {t('settings.appServerBinary')}
                  </label>
                  <input
                    id="settings-binary-path"
                    ref={inputRef}
                    type="text"
                    value={binaryPath}
                    onChange={(e) => setBinaryPath(e.target.value)}
                    placeholder={t('settings.binaryPlaceholder')}
                    style={inputStyle(true)}
                  />
                  <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
                    {t('settings.binaryHint')}
                  </div>
                </div>

                {canRestartManagedAppServer && (
                  <div style={{ marginBottom: '16px' }}>
                    <button
                      type="button"
                      onClick={() => {
                        void handleRestartManagedAppServer()
                      }}
                      disabled={restartingAppServer || saving}
                      style={secondaryButtonStyle(restartingAppServer || saving)}
                    >
                      {restartingAppServer ? t('settings.restartingAppServer') : t('settings.restartAppServer')}
                    </button>
                    <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginTop: '6px' }}>
                      {t('settings.restartAppServerHint')}
                    </div>
                  </div>
                )}
              </>
            )}

            {activeSettingsTab === 'channels' && (
              <div style={{ marginBottom: '16px' }}>
                <div style={sectionLabelStyle()}>{t('settings.crossChannelVisibility')}</div>
                <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginBottom: '10px', lineHeight: 1.45 }}>
                  {t('settings.crossChannelHint')}
                </div>
                {serverChannels === null && (
                  <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginBottom: '8px' }}>
                    {t('settings.channelListLoading')}
                  </div>
                )}
                {serverChannels !== null && channelListError && (
                  <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginBottom: '8px' }}>
                    {t('settings.channelListUnavailable')}
                  </div>
                )}
                {serverChannels !== null &&
                  !channelListError &&
                  CATEGORY_ORDER.map((cat) => {
                    const items = channelsByCategory.get(cat)
                    if (!items?.length) return null
                    const labelKey = CATEGORY_LABEL_KEY[cat]
                    return (
                      <div key={cat} style={{ marginBottom: '10px' }}>
                        <div
                          style={{
                            fontSize: '10px',
                            fontWeight: 600,
                            textTransform: 'uppercase',
                            letterSpacing: '0.04em',
                            color: 'var(--text-dimmed)',
                            marginBottom: '6px'
                          }}
                        >
                          {labelKey ? t(labelKey) : cat}
                        </div>
                        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px' }}>
                          {items.map((ch) => {
                            const selected = visibleChannels.includes(ch.name)
                            return (
                              <button
                                key={ch.name}
                                type="button"
                                role="checkbox"
                                aria-checked={selected}
                                onClick={() => {
                                  toggleVisibleChannel(ch.name, !selected)
                                }}
                                style={{
                                  fontSize: '11px',
                                  fontWeight: 600,
                                  letterSpacing: '0.02em',
                                  padding: '4px 8px',
                                  borderRadius: '6px',
                                  cursor: 'pointer',
                                  border: selected ? '1px solid var(--accent)' : '1px solid var(--border-default)',
                                  backgroundColor: selected ? 'var(--accent)' : 'transparent',
                                  color: selected ? 'var(--on-accent)' : 'var(--text-primary)'
                                }}
                              >
                                {formatChannelChipLabel(ch.name)}
                              </button>
                            )
                          })}
                        </div>
                      </div>
                    )
                  })}
              </div>
            )}

            {activeSettingsTab === 'mcp' && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                {!mcpEnabled && (
                  <div style={cardStyle()}>
                    <div style={{ fontSize: '14px', color: 'var(--text-primary)' }}>
                      Current AppServer does not support MCP management.
                    </div>
                  </div>
                )}

                {mcpEnabled && editingServerName === null && (
                  <>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '12px' }}>
                      <div>
                        <div style={{ fontSize: '18px', fontWeight: 600, color: 'var(--text-primary)' }}>MCP Servers</div>
                        <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
                          Connect custom MCP servers stored in workspace <code>.craft/config.json</code>.
                        </div>
                      </div>
                      <button type="button" onClick={() => startMcpDraft()} style={primaryButtonStyle(false)}>
                        + Add Server
                      </button>
                    </div>

                    {mcpLoading && (
                      <div style={cardStyle()}>
                        <div style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>Loading MCP servers...</div>
                      </div>
                    )}

                    {!mcpLoading && mcpError && (
                      <div style={cardStyle()}>
                        <div style={{ fontSize: '13px', color: '#f85149' }}>{mcpError}</div>
                      </div>
                    )}

                    {!mcpLoading && !mcpError && mergedMcpServers.length === 0 && (
                      <div style={{ ...cardStyle(), padding: '22px' }}>
                        <div style={{ fontSize: '14px', color: 'var(--text-primary)', marginBottom: '6px' }}>
                          No custom MCP servers connected
                        </div>
                        <div style={{ fontSize: '12px', color: 'var(--text-dimmed)' }}>
                          Add a server to configure local stdio or streamable HTTP MCP connections.
                        </div>
                      </div>
                    )}

                    {!mcpLoading &&
                      !mcpError &&
                      mergedMcpServers.map((server) => {
                        const status = mcpStatuses[server.name.trim().toLowerCase()]
                        const tone = getStatusTone(status)
                        return (
                          <button
                            key={server.name}
                            type="button"
                            onClick={() => startMcpDraft(server)}
                            style={{
                              ...cardStyle(),
                              display: 'flex',
                              alignItems: 'flex-start',
                              justifyContent: 'space-between',
                              gap: '16px',
                              cursor: 'pointer',
                              textAlign: 'left'
                            }}
                          >
                            <div style={{ minWidth: 0 }}>
                              <div style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)' }}>
                                {server.name}
                              </div>
                              <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
                                {server.transport === 'stdio' ? 'STDIO' : 'Streamable HTTP'}
                                {!server.enabled ? ' · Disabled' : ''}
                              </div>
                              {status?.lastError && (
                                <div style={{ fontSize: '12px', color: '#f85149', marginTop: '8px' }}>
                                  {status.lastError}
                                </div>
                              )}
                            </div>
                            <div
                              style={{
                                flexShrink: 0,
                                fontSize: '12px',
                                fontWeight: 600,
                                color: tone.color
                              }}
                            >
                              {tone.label}
                              {typeof status?.toolCount === 'number' ? ` · ${status.toolCount} tools` : ''}
                            </div>
                          </button>
                        )
                      })}
                  </>
                )}

                {mcpEnabled && editingServerName !== null && (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                      <div>
                        <div style={{ fontSize: '18px', fontWeight: 600, color: 'var(--text-primary)' }}>
                          {editingServerName === '__new__' ? 'Add MCP Server' : 'Edit MCP Server'}
                        </div>
                        <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
                          Configure how this workspace connects to the MCP server.
                        </div>
                      </div>
                      <button type="button" onClick={cancelMcpEdit} style={secondaryButtonStyle(false)}>
                        Back
                      </button>
                    </div>

                    <div style={cardStyle()}>
                      <label style={sectionLabelStyle()}>Name</label>
                      <input
                        type="text"
                        value={mcpDraft.name}
                        onChange={(e) => setMcpDraft((prev) => ({ ...prev, name: e.target.value }))}
                        placeholder="MCP server name"
                        style={inputStyle()}
                      />
                      <div style={{ marginTop: '12px', display: 'flex', gap: '8px' }}>
                        {(['stdio', 'streamableHttp'] as const).map((transport) => {
                          const active = mcpDraft.transport === transport
                          return (
                            <button
                              key={transport}
                              type="button"
                              onClick={() =>
                                setMcpDraft((prev) => ({
                                  ...prev,
                                  transport: transport as McpTransport
                                }))
                              }
                              style={{
                                flex: 1,
                                padding: '8px 12px',
                                borderRadius: '8px',
                                border: active ? '1px solid var(--accent)' : '1px solid var(--border-default)',
                                background: active ? 'var(--bg-tertiary)' : 'transparent',
                                color: 'var(--text-primary)',
                                fontSize: '13px',
                                fontWeight: 600,
                                cursor: 'pointer'
                              }}
                            >
                              {transport === 'stdio' ? 'STDIO' : 'Streamable HTTP'}
                            </button>
                          )
                        })}
                      </div>
                      <label style={{ ...sectionLabelStyle(), marginTop: '12px' }}>
                        <input
                          type="checkbox"
                          checked={mcpDraft.enabled}
                          onChange={(e) =>
                            setMcpDraft((prev) => ({
                              ...prev,
                              enabled: e.target.checked
                            }))
                          }
                          style={{ marginRight: '8px' }}
                        />
                        Enabled
                      </label>
                    </div>

                    {mcpDraft.transport === 'stdio' && (
                      <>
                        <div style={cardStyle()}>
                          <label style={sectionLabelStyle()}>Command</label>
                          <input
                            type="text"
                            value={mcpDraft.command ?? ''}
                            onChange={(e) => setMcpDraft((prev) => ({ ...prev, command: e.target.value }))}
                            placeholder="npx"
                            style={inputStyle(true)}
                          />
                        </div>

                        <div style={cardStyle()}>
                          <div style={sectionLabelStyle()}>Arguments</div>
                          <EditableValueList
                            rows={argRows}
                            setRows={setArgRows}
                            placeholder="e.g. -y or @playwright/mcp@latest"
                          />
                        </div>

                        <div style={cardStyle()}>
                          <div style={sectionLabelStyle()}>Environment Variables</div>
                          <EditableKeyValueList
                            rows={envRows}
                            setRows={setEnvRows}
                            keyPlaceholder="KEY"
                            valuePlaceholder="Value"
                          />
                        </div>

                        <div style={cardStyle()}>
                          <div style={sectionLabelStyle()}>Environment Variable Forwarding</div>
                          <EditableValueList rows={envVarRows} setRows={setEnvVarRows} placeholder="ENV_VAR_NAME" />
                        </div>

                        <div style={cardStyle()}>
                          <label style={sectionLabelStyle()}>Working Directory</label>
                          <input
                            type="text"
                            value={mcpDraft.cwd ?? ''}
                            onChange={(e) => setMcpDraft((prev) => ({ ...prev, cwd: e.target.value }))}
                            placeholder="~/code"
                            style={inputStyle(true)}
                          />
                        </div>
                      </>
                    )}

                    {mcpDraft.transport === 'streamableHttp' && (
                      <>
                        <div style={cardStyle()}>
                          <label style={sectionLabelStyle()}>URL</label>
                          <input
                            type="text"
                            value={mcpDraft.url ?? ''}
                            onChange={(e) => setMcpDraft((prev) => ({ ...prev, url: e.target.value }))}
                            placeholder="https://example.com/mcp"
                            style={inputStyle(true)}
                          />
                        </div>

                        <div style={cardStyle()}>
                          <label style={sectionLabelStyle()}>Bearer Token Env Var</label>
                          <input
                            type="text"
                            value={mcpDraft.bearerTokenEnvVar ?? ''}
                            onChange={(e) =>
                              setMcpDraft((prev) => ({ ...prev, bearerTokenEnvVar: e.target.value }))
                            }
                            placeholder="DOCS_TOKEN"
                            style={inputStyle(true)}
                          />
                        </div>

                        <div style={cardStyle()}>
                          <div style={sectionLabelStyle()}>HTTP Headers</div>
                          <EditableKeyValueList
                            rows={httpHeaderRows}
                            setRows={setHttpHeaderRows}
                            keyPlaceholder="Header"
                            valuePlaceholder="Value"
                          />
                        </div>

                        <div style={cardStyle()}>
                          <div style={sectionLabelStyle()}>Environment-backed Headers</div>
                          <EditableKeyValueList
                            rows={envHttpHeaderRows}
                            setRows={setEnvHttpHeaderRows}
                            keyPlaceholder="Header"
                            valuePlaceholder="ENV_VAR_NAME"
                          />
                        </div>
                      </>
                    )}

                    {mcpTestResult && (
                      <div style={cardStyle()}>
                        <div
                          style={{
                            fontSize: '13px',
                            fontWeight: 600,
                            color: mcpTestResult.success ? '#3fb950' : '#f85149'
                          }}
                        >
                          {mcpTestResult.success ? 'Connection test succeeded' : 'Connection test failed'}
                        </div>
                        {typeof mcpTestResult.toolCount === 'number' && (
                          <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
                            Tools discovered: {mcpTestResult.toolCount}
                          </div>
                        )}
                        {mcpTestResult.errorMessage && (
                          <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
                            {mcpTestResult.errorMessage}
                          </div>
                        )}
                      </div>
                    )}

                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: '12px' }}>
                      <div>
                        {editingServerName !== '__new__' && (
                          <button
                            type="button"
                            onClick={() => {
                              void handleMcpDelete()
                            }}
                            disabled={deletingMcp || savingMcp}
                            style={{
                              ...secondaryButtonStyle(deletingMcp || savingMcp),
                              color: '#f85149',
                              borderColor: 'rgba(248,81,73,0.45)'
                            }}
                          >
                            {deletingMcp ? 'Removing...' : 'Delete'}
                          </button>
                        )}
                      </div>
                      <div style={{ display: 'flex', gap: '8px' }}>
                        <button
                          type="button"
                          onClick={() => {
                            void handleMcpTest()
                          }}
                          disabled={testingMcp || savingMcp}
                          style={secondaryButtonStyle(testingMcp || savingMcp)}
                        >
                          {testingMcp ? 'Testing...' : 'Test Connection'}
                        </button>
                        <button
                          type="button"
                          onClick={() => {
                            void handleMcpSave()
                          }}
                          disabled={savingMcp || deletingMcp}
                          style={primaryButtonStyle(savingMcp || deletingMcp)}
                        >
                          {savingMcp ? 'Saving...' : 'Save'}
                        </button>
                      </div>
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        </main>
      </div>

      <footer
        style={{
          padding: '12px 20px',
          borderTop: '1px solid var(--border-default)',
          display: 'flex',
          justifyContent: 'flex-end',
          gap: '8px',
          flexShrink: 0
        }}
      >
        <button type="button" onClick={closeSettings} style={secondaryButtonStyle(false)}>
          {t('common.cancel')}
        </button>
        {activeSettingsTab !== 'mcp' && (
          <button
            type="button"
            onClick={() => {
              void handleSave()
            }}
            disabled={saving}
            style={primaryButtonStyle(saving)}
          >
            {saving ? t('settings.saving') : t('settings.save')}
          </button>
        )}
      </footer>
    </div>
  )
}
