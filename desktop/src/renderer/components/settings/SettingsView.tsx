import { useEffect, useMemo, useRef, useState, type CSSProperties, type Dispatch, type JSX, type SetStateAction } from 'react'
import { addToast } from '../../stores/toastStore'
import { applyTheme, resolveTheme, type ThemeMode } from '../../utils/theme'
import { normalizeLocale, type AppLocale } from '../../../shared/locales'
import { useSetUiLocale, useT } from '../../contexts/LocaleContext'
import type { MessageKey } from '../../../shared/locales'
import { ensureVisibleChannelsSeeded } from '../../utils/visibleChannelsDefaults'
import { mergeAvailableChannels } from '../../utils/availableChannels'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { SecretInput } from '../channels/FormShared'
import { ArchivedThreadsSettingsView } from './ArchivedThreadsSettingsView'
import { FolderIcon, OpenInBrowserIcon, RefreshIcon } from '../ui/AppIcons'
import { ChannelIconBadge } from '../ui/channelMeta'
import { IconButton } from '../ui/IconButton'
import { InputWithAction } from '../ui/InputWithAction'
import { SelectionCard, ResolvedPill } from '../ui/SelectionCard'
import { PillSwitch } from '../ui/PillSwitch'
import { BackToAppButton } from '../ui/BackToAppButton'
import { SettingsGroup, SettingsRow } from './SettingsGroup'
import {
  useMcpStore,
  type McpServerConfigWire,
  type McpServerStatusWire,
  type McpTransport
} from '../../stores/mcpStore'
import type { BinarySource, ProxyAuthFileSummary, ProxyOAuthProvider } from '../../../preload/api'

declare const __APP_VERSION__: string | undefined

interface ChannelInfoWire {
  name: string
  category?: string
}

interface SettingsViewProps {
  workspacePath?: string
  onThreadListRefreshRequested?: () => void
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
type SettingsTab = 'general' | 'connection' | 'proxy' | 'usage' | 'channels' | 'archivedThreads' | 'mcp'
type ProxyRuntimeStatus = 'stopped' | 'starting' | 'running' | 'error'
type ProxyProviderStatus = 'idle' | 'checking' | 'pending' | 'ok' | 'error'

const CATEGORY_ORDER = ['builtin', 'social', 'system'] as const
const DEFAULT_WS_HOST = '127.0.0.1'
const DEFAULT_WS_PORT = 9100
const DEFAULT_PROXY_PORT = 8317
const PROXY_STATUS_RETRY_MS = 1000
const PROXY_AUTH_FILES_RETRY_MS = 1000
const PROXY_AUTH_RECOVERY_ATTEMPTS = 5
const AUTHENTICATED_PROXY_AUTH_STATUSES = new Set(['ready', 'active'])
const PROXY_OAUTH_POLL_ATTEMPTS = 150
const PROXY_OAUTH_POLL_INTERVAL_MS = 1200
const PROXY_OAUTH_PROVIDERS: ProxyOAuthProvider[] = ['codex', 'claude', 'gemini', 'qwen', 'iflow']

const CATEGORY_LABEL_KEY: Record<string, MessageKey> = {
  builtin: 'settings.channelCategory.builtin',
  social: 'settings.channelCategory.social',
  system: 'settings.channelCategory.system'
}

function createRowId(prefix: string): string {
  return `${prefix}-${Math.random().toString(36).slice(2, 10)}`
}

function createProxyProviderMap<T>(value: T): Record<ProxyOAuthProvider, T> {
  return {
    codex: value,
    claude: value,
    gemini: value,
    qwen: value,
    iflow: value
  }
}

function isAuthenticatedProxyAuthFile(file: ProxyAuthFileSummary, provider?: ProxyOAuthProvider): boolean {
  return AUTHENTICATED_PROXY_AUTH_STATUSES.has(file.status) &&
    !file.disabled &&
    !file.unavailable &&
    (provider === undefined || file.provider === provider)
}

function getReadyProxyProviders(files: ProxyAuthFileSummary[]): Set<ProxyOAuthProvider> {
  return new Set(
    files.filter((file) => isAuthenticatedProxyAuthFile(file)).map((file) => file.provider)
  )
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

function getStatusTone(
  t: (key: MessageKey | string, vars?: Record<string, string | number>) => string,
  status?: McpServerStatusWire
): { label: string; color: string } {
  switch (status?.startupState) {
    case 'ready':
      return { label: t('settings.mcp.status.connected'), color: '#3fb950' }
    case 'starting':
      return { label: t('settings.mcp.status.connecting'), color: '#d29922' }
    case 'error':
      return { label: t('settings.mcp.status.error'), color: '#f85149' }
    case 'disabled':
      return { label: t('settings.mcp.disabledSuffix').replace(/^ · /, ''), color: 'var(--text-dimmed)' }
    default:
      return { label: t('settings.mcp.status.idle'), color: 'var(--text-dimmed)' }
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

function secondaryActionButtonStyle(disabled = false): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    gap: '8px',
    minHeight: '34px',
    padding: '0 14px',
    border: '1px solid var(--border-default)',
    borderRadius: '8px',
    background: 'var(--bg-tertiary)',
    color: 'var(--text-primary)',
    fontSize: '13px',
    fontWeight: 600,
    lineHeight: 1,
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.7 : 1,
    whiteSpace: 'nowrap',
    flexShrink: 0
  }
}

function formatCompactNumber(value: number): string {
  if (!Number.isFinite(value)) return '0'
  return new Intl.NumberFormat(undefined, {
    notation: 'compact',
    maximumFractionDigits: 1
  }).format(value)
}

function ProxyRuntimeStatusPill({
  status,
  label
}: {
  status: ProxyRuntimeStatus
  label: string
}): JSX.Element {
  const tone =
    status === 'running'
      ? { bg: 'rgba(52, 199, 89, 0.15)', text: 'var(--success)' }
      : status === 'starting'
        ? { bg: 'rgba(255, 149, 0, 0.15)', text: 'var(--warning)' }
        : status === 'error'
          ? { bg: 'rgba(255, 69, 58, 0.15)', text: 'var(--error, #ff453a)' }
          : { bg: 'var(--bg-tertiary)', text: 'var(--text-dimmed)' }
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '6px',
        padding: '3px 9px',
        borderRadius: '12px',
        fontSize: '11px',
        fontWeight: 600,
        backgroundColor: tone.bg,
        color: tone.text
      }}
    >
      <span
        aria-hidden
        style={{
          width: '6px',
          height: '6px',
          borderRadius: '50%',
          backgroundColor: tone.text
        }}
      />
      {label}
    </span>
  )
}

function ProxyOAuthStatusPill({
  status,
  label
}: {
  status: ProxyProviderStatus
  label: string
}): JSX.Element {
  const tone =
    status === 'ok'
      ? { bg: 'rgba(52, 199, 89, 0.15)', text: 'var(--success)' }
      : status === 'checking'
        ? { bg: 'rgba(120, 120, 128, 0.18)', text: 'var(--text-secondary)' }
      : status === 'pending'
        ? { bg: 'rgba(255, 149, 0, 0.15)', text: 'var(--warning)' }
        : status === 'error'
          ? { bg: 'rgba(255, 69, 58, 0.15)', text: 'var(--error, #ff453a)' }
          : { bg: 'var(--bg-tertiary)', text: 'var(--text-dimmed)' }
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        padding: '2px 8px',
        borderRadius: '10px',
        fontSize: '11px',
        fontWeight: 600,
        backgroundColor: tone.bg,
        color: tone.text
      }}
    >
      {label}
    </span>
  )
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

export function SettingsView({
  workspacePath,
  onThreadListRefreshRequested
}: SettingsViewProps): JSX.Element {
  const t = useT()
  const setUiLocale = useSetUiLocale()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const dashboardUrl = useConnectionStore((s) => s.dashboardUrl)
  const mcpStatuses = useMcpStore((s) => s.statuses)
  const setMcpStatuses = useMcpStore((s) => s.setStatuses)
  const [binarySource, setBinarySource] = useState<BinarySource>('bundled')
  const [binaryPath, setBinaryPath] = useState('')
  const [resolvedBinaryPath, setResolvedBinaryPath] = useState<string | null>(null)
  const [resolvingBinary, setResolvingBinary] = useState(false)
  const [connectionMode, setConnectionMode] = useState<ConnectionMode>('stdio')
  const [savedConnectionMode, setSavedConnectionMode] = useState<ConnectionMode>('stdio')
  const [wsHost, setWsHost] = useState(DEFAULT_WS_HOST)
  const [wsPort, setWsPort] = useState(String(DEFAULT_WS_PORT))
  const [remoteUrl, setRemoteUrl] = useState('')
  const [remoteToken, setRemoteToken] = useState('')
  const [proxyEnabled, setProxyEnabled] = useState(false)
  const [proxyPort, setProxyPort] = useState(String(DEFAULT_PROXY_PORT))
  const [proxyAuthDir, setProxyAuthDir] = useState('')
  const [proxyBinarySource, setProxyBinarySource] = useState<BinarySource>('bundled')
  const [proxyBinaryPath, setProxyBinaryPath] = useState('')
  const [resolvedProxyBinaryPath, setResolvedProxyBinaryPath] = useState<string | null>(null)
  const [resolvingProxyBinary, setResolvingProxyBinary] = useState(false)
  const [proxyStatusText, setProxyStatusText] = useState<ProxyRuntimeStatus>('stopped')
  const [proxyStatusError, setProxyStatusError] = useState('')
  const [proxyUsage, setProxyUsage] = useState<{
    totalRequests: number
    successCount: number
    failureCount: number
    totalTokens: number
  } | null>(null)
  const [proxyUsageLoading, setProxyUsageLoading] = useState(false)
  const [proxyProviderStatus, setProxyProviderStatus] = useState<Record<ProxyOAuthProvider, ProxyProviderStatus>>(
    createProxyProviderMap<ProxyProviderStatus>('idle')
  )
  const [proxyProviderError, setProxyProviderError] = useState<Record<ProxyOAuthProvider, string>>(
    createProxyProviderMap('')
  )
  const [proxyProviderLoading, setProxyProviderLoading] = useState<Record<ProxyOAuthProvider, boolean>>(
    createProxyProviderMap(false)
  )
  const [proxyStatusRefreshTick, setProxyStatusRefreshTick] = useState(0)
  const [proxyAuthRefreshTick, setProxyAuthRefreshTick] = useState(0)
  const [proxyAuthRecoveryAttempt, setProxyAuthRecoveryAttempt] = useState(0)
  const [proxyAuthRecoverySettled, setProxyAuthRecoverySettled] = useState(false)
  const [restartingProxy, setRestartingProxy] = useState(false)
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
  const canRestartManagedAppServer = savedConnectionMode !== 'remote'

  useEffect(() => {
    inputRef.current?.focus()
    window.api.settings
      .get()
      .then(async (s) => {
        const loadedMode = (s.connectionMode ?? 'stdio') as ConnectionMode
        setBinarySource((s.binarySource ?? (s.appServerBinaryPath ? 'custom' : 'bundled')) as BinarySource)
        setBinaryPath(s.appServerBinaryPath ?? '')
        setConnectionMode(loadedMode)
        setSavedConnectionMode(loadedMode)
        setWsHost(s.webSocket?.host ?? DEFAULT_WS_HOST)
        setWsPort(String(s.webSocket?.port ?? DEFAULT_WS_PORT))
        setRemoteUrl(s.remote?.url ?? '')
        setRemoteToken(s.remote?.token ?? '')
        setProxyEnabled(s.proxy?.enabled === true)
        setProxyPort(String(s.proxy?.port ?? DEFAULT_PROXY_PORT))
        setProxyAuthDir(s.proxy?.authDir ?? '')
        setProxyBinarySource((s.proxy?.binarySource ?? (s.proxy?.binaryPath ? 'custom' : 'bundled')) as BinarySource)
        setProxyBinaryPath(s.proxy?.binaryPath ?? '')
        setTheme(resolveTheme(s.theme))
        setLocale(normalizeLocale(s.locale))
        setVisibleChannels(await ensureVisibleChannelsSeeded(s))
      })
      .catch(() => {})
    setVersion(typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : '0.1.0')
  }, [])

  useEffect(() => {
    let cancelled = false
    setResolvingBinary(true)
    window.api.appServer
      .getResolvedBinary({
        binarySource,
        binaryPath
      })
      .then((result) => {
        if (!cancelled) {
          setResolvedBinaryPath(result.path)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setResolvedBinaryPath(null)
        }
      })
      .finally(() => {
        if (!cancelled) {
          setResolvingBinary(false)
        }
      })
    return () => {
      cancelled = true
    }
  }, [binaryPath, binarySource])

  useEffect(() => {
    let cancelled = false
    setResolvingProxyBinary(true)
    window.api.proxy
      .getResolvedBinary({
        binarySource: proxyBinarySource,
        binaryPath: proxyBinaryPath
      })
      .then((result) => {
        if (!cancelled) {
          setResolvedProxyBinaryPath(result.path)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setResolvedProxyBinaryPath(null)
        }
      })
      .finally(() => {
        if (!cancelled) {
          setResolvingProxyBinary(false)
        }
      })
    return () => {
      cancelled = true
    }
  }, [proxyBinaryPath, proxyBinarySource])

  useEffect(() => {
    let cancelled = false
    window.api.proxy
      .getStatus()
      .then((status) => {
        if (cancelled) return
        setProxyStatusText(status.status)
        setProxyStatusError(status.errorMessage ?? '')
        if (proxyEnabled && status.status !== 'running') {
          window.setTimeout(() => {
            if (!cancelled) {
              setProxyStatusRefreshTick((prev) => prev + 1)
            }
          }, PROXY_STATUS_RETRY_MS)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setProxyStatusText('error')
          setProxyStatusError('Failed to read proxy status')
          if (proxyEnabled) {
            window.setTimeout(() => {
              if (!cancelled) {
                setProxyStatusRefreshTick((prev) => prev + 1)
              }
            }, PROXY_STATUS_RETRY_MS)
          }
        }
      })
    return () => {
      cancelled = true
    }
  }, [proxyEnabled, restartingProxy, saving, proxyStatusRefreshTick])

  useEffect(() => {
    let cancelled = false
    if (!proxyEnabled) {
      setProxyAuthRecoveryAttempt(0)
      setProxyAuthRecoverySettled(false)
      setProxyProviderStatus(createProxyProviderMap<ProxyProviderStatus>('idle'))
      setProxyProviderError(createProxyProviderMap(''))
      setProxyProviderLoading(createProxyProviderMap(false))
      return
    }
    if (activeSettingsTab !== 'proxy') {
      return
    }
    if (proxyStatusText !== 'running') {
      setProxyProviderStatus((prev) => {
        const next = { ...prev }
        for (const provider of PROXY_OAUTH_PROVIDERS) {
          if (prev[provider] === 'idle' || prev[provider] === 'checking') {
            next[provider] = 'checking'
          }
        }
        return next
      })
      setProxyProviderError((prev) => {
        const next = { ...prev }
        for (const provider of PROXY_OAUTH_PROVIDERS) {
          if (next[provider] && !proxyProviderLoading[provider]) {
            next[provider] = ''
          }
        }
        return next
      })
      setProxyAuthRecoveryAttempt(0)
      setProxyAuthRecoverySettled(false)
      return
    }
    window.api.proxy
      .listAuthFiles()
      .then((files) => {
        if (!cancelled) {
          const readyProviders = getReadyProxyProviders(files)
          if (readyProviders.size > 0) {
            setProxyAuthRecoveryAttempt(0)
            setProxyAuthRecoverySettled(true)
            applyProxyAuthFiles(files, { fallbackStatus: 'idle', preservePending: true })
            return
          }
          if (!proxyAuthRecoverySettled && proxyAuthRecoveryAttempt < PROXY_AUTH_RECOVERY_ATTEMPTS - 1) {
            applyProxyAuthFiles(files, {
              fallbackStatus: 'checking',
              preservePending: true,
              preserveAuthenticated: true
            })
            window.setTimeout(() => {
              if (!cancelled) {
                setProxyAuthRecoveryAttempt((prev) => prev + 1)
                setProxyAuthRefreshTick((prev) => prev + 1)
              }
            }, PROXY_AUTH_FILES_RETRY_MS)
            return
          }
          setProxyAuthRecoveryAttempt(0)
          setProxyAuthRecoverySettled(true)
          applyProxyAuthFiles(files, { fallbackStatus: 'idle', preservePending: true })
        }
      })
      .catch(() => {
        if (!proxyAuthRecoverySettled && proxyAuthRecoveryAttempt < PROXY_AUTH_RECOVERY_ATTEMPTS - 1) {
          applyProxyAuthFiles([], {
            fallbackStatus: 'checking',
            preservePending: true,
            preserveAuthenticated: true
          })
          window.setTimeout(() => {
            if (!cancelled) {
              setProxyAuthRecoveryAttempt((prev) => prev + 1)
              setProxyAuthRefreshTick((prev) => prev + 1)
            }
          }, PROXY_AUTH_FILES_RETRY_MS)
          return
        }
        setProxyAuthRecoveryAttempt(0)
        setProxyAuthRecoverySettled(true)
        applyProxyAuthFiles([], { fallbackStatus: 'idle', preservePending: true })
      })
    return () => {
      cancelled = true
    }
  }, [
    activeSettingsTab,
    proxyEnabled,
    proxyStatusText,
    proxyAuthRefreshTick,
    proxyAuthRecoveryAttempt,
    proxyAuthRecoverySettled,
    restartingProxy,
    saving,
    proxyProviderLoading
  ])

  useEffect(() => {
    let cancelled = false
    setServerChannels(null)
    setChannelListError(false)
    Promise.allSettled([
      window.api.appServer.sendRequest('channel/list', {}),
      window.api.modules.list()
    ])
      .then((results) => {
        if (cancelled) return
        const [channelListResult, modulesResult] = results
        const channelListOk = channelListResult.status === 'fulfilled'
        const serverList = channelListOk
          ? ((channelListResult.value as { channels?: ChannelInfoWire[] }).channels ?? [])
          : []
        const modules = modulesResult.status === 'fulfilled' ? modulesResult.value : []
        setServerChannels(mergeAvailableChannels(serverList, modules))
        setChannelListError(!channelListOk)
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
      const rawCategory = c.category || 'builtin'
      const cat = rawCategory === 'external' ? 'social' : rawCategory
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
    const name = (editingServerName !== '__new__' ? editingServerName?.trim() : mcpDraft.name.trim()) ?? ''
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
      onThreadListRefreshRequested?.()
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
      const parsedProxyPort = Number.parseInt(proxyPort.trim(), 10)
      const normalizedProxyPort =
        Number.isInteger(parsedProxyPort) && parsedProxyPort > 0 && parsedProxyPort <= 65535
          ? parsedProxyPort
          : DEFAULT_PROXY_PORT
      await window.api.settings.set({
        binarySource,
        appServerBinaryPath: binaryPath.trim() || undefined,
        connectionMode,
        webSocket: {
          host: wsHost.trim() || DEFAULT_WS_HOST,
          port: normalizedPort
        },
        remote: {
          url: remoteUrl.trim() || undefined,
          token: remoteToken.trim() || undefined
        },
        proxy: {
          enabled: proxyEnabled,
          port: normalizedProxyPort,
          binarySource: proxyBinarySource,
          binaryPath: proxyBinaryPath.trim() || undefined,
          authDir: proxyAuthDir.trim() || undefined
        }
      })
      setSavedConnectionMode(connectionMode)
      addToast(
        activeSettingsTab === 'connection'
          ? t('settings.restartAppServerSavedHint')
          : t('settings.savedToast'),
        'success'
      )
      if (activeSettingsTab !== 'connection' && activeSettingsTab !== 'proxy') {
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

  async function handlePickBinary(): Promise<void> {
    try {
      const picked = await window.api.appServer.pickBinary()
      if (picked) {
        setBinaryPath(picked)
      }
    } catch (err) {
      addToast(
        t('settings.pickBinaryFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  async function handlePickProxyBinary(): Promise<void> {
    try {
      const picked = await window.api.proxy.pickBinary()
      if (picked) {
        setProxyBinaryPath(picked)
      }
    } catch (err) {
      addToast(
        t('settings.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  async function handlePickProxyAuthDir(): Promise<void> {
    try {
      const picked = await window.api.workspace.pickFolder()
      if (picked) {
        setProxyAuthDir(picked)
      }
    } catch (err) {
      addToast(
        t('settings.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  async function handleRestartProxy(): Promise<void> {
    setRestartingProxy(true)
    try {
      await window.api.proxy.restartManaged()
      setProxyStatusText('running')
      setProxyStatusError('')
      addToast(t('settings.proxy.restartSuccess'), 'success')
    } catch (err) {
      addToast(
        t('settings.proxy.restartFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setRestartingProxy(false)
    }
  }

  function applyProxyAuthFiles(
    files: ProxyAuthFileSummary[],
    options?: {
      fallbackStatus?: ProxyProviderStatus | 'keep'
      preservePending?: boolean
      preserveAuthenticated?: boolean
    }
  ): Set<ProxyOAuthProvider> {
    const readyProviders = getReadyProxyProviders(files)
    const fallbackStatus = options?.fallbackStatus ?? 'keep'
    const preservePending = options?.preservePending ?? false
    const preserveAuthenticated = options?.preserveAuthenticated ?? false

    setProxyProviderStatus((prev) => {
      const next = { ...prev }
      for (const provider of PROXY_OAUTH_PROVIDERS) {
        if (readyProviders.has(provider)) {
          next[provider] = 'ok'
          continue
        }
        if (preservePending && prev[provider] === 'pending') {
          continue
        }
        if (preserveAuthenticated && prev[provider] === 'ok') {
          continue
        }
        if (fallbackStatus !== 'keep') {
          next[provider] = fallbackStatus
        }
      }
      return next
    })
    setProxyProviderError((prev) => {
      const next = { ...prev }
      for (const provider of PROXY_OAUTH_PROVIDERS) {
        if (readyProviders.has(provider) || fallbackStatus === 'idle' || fallbackStatus === 'checking') {
          next[provider] = ''
        }
      }
      return next
    })

    return readyProviders
  }

  async function refreshProxyProviderStatuses(options?: {
    fallbackStatus?: ProxyProviderStatus | 'keep'
    preservePending?: boolean
  }): Promise<Set<ProxyOAuthProvider>> {
    const files = await window.api.proxy.listAuthFiles()
    return applyProxyAuthFiles(files, options)
  }

  function markProxyProviderAuthenticated(provider: ProxyOAuthProvider, withToast = true): void {
    setProxyProviderStatus((prev) => ({ ...prev, [provider]: 'ok' }))
    setProxyProviderError((prev) => ({ ...prev, [provider]: '' }))
    setProxyProviderLoading((prev) => ({ ...prev, [provider]: false }))
    if (withToast) {
      addToast(t('settings.proxy.oauthStatusOk'), 'success')
    }
  }

  function markProxyProviderFailure(provider: ProxyOAuthProvider, message: string, toastKey: MessageKey): void {
    setProxyProviderStatus((prev) => ({ ...prev, [provider]: 'error' }))
    setProxyProviderError((prev) => ({ ...prev, [provider]: message }))
    setProxyProviderLoading((prev) => ({ ...prev, [provider]: false }))
    addToast(t(toastKey, { error: message }), 'error')
  }

  async function pollProxyOAuthStatus(provider: ProxyOAuthProvider, state: string): Promise<void> {
    for (let attempt = 0; attempt < PROXY_OAUTH_POLL_ATTEMPTS; attempt += 1) {
      try {
        const result = await window.api.proxy.getAuthStatus(state)
        if (result.status === 'ok') {
          markProxyProviderAuthenticated(provider)
          void refreshProxyProviderStatuses({
            fallbackStatus: 'keep',
            preservePending: false
          }).catch(() => {})
          return
        }

        if (result.status === 'error') {
          const message = result.error || result.status
          const readyProviders = await refreshProxyProviderStatuses({
            fallbackStatus: 'keep',
            preservePending: true
          }).catch(() => new Set<ProxyOAuthProvider>())
          if (readyProviders.has(provider)) {
            markProxyProviderAuthenticated(provider)
            return
          }
          markProxyProviderFailure(provider, message, 'settings.proxy.oauthStatusError')
          return
        }
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err)
        const readyProviders = await refreshProxyProviderStatuses({
          fallbackStatus: 'keep',
          preservePending: true
        }).catch(() => new Set<ProxyOAuthProvider>())
        if (readyProviders.has(provider)) {
          markProxyProviderAuthenticated(provider)
          return
        }
        markProxyProviderFailure(provider, message, 'settings.proxy.oauthStatusFailed')
        return
      }

      await new Promise((resolve) => window.setTimeout(resolve, PROXY_OAUTH_POLL_INTERVAL_MS))
    }

    const timeoutMessage = t('settings.proxy.oauthStatusTimeout')
    const readyProviders = await refreshProxyProviderStatuses({
      fallbackStatus: 'keep',
      preservePending: true
    }).catch(() => new Set<ProxyOAuthProvider>())
    if (readyProviders.has(provider)) {
      markProxyProviderAuthenticated(provider)
      return
    }
    setProxyProviderStatus((prev) => ({ ...prev, [provider]: 'error' }))
    setProxyProviderError((prev) => ({ ...prev, [provider]: timeoutMessage }))
    setProxyProviderLoading((prev) => ({ ...prev, [provider]: false }))
    addToast(timeoutMessage, 'error')
  }

  async function handleStartProxyOAuth(provider: ProxyOAuthProvider): Promise<void> {
    setProxyProviderStatus((prev) => ({ ...prev, [provider]: 'pending' }))
    setProxyProviderError((prev) => ({ ...prev, [provider]: '' }))
    setProxyProviderLoading((prev) => ({ ...prev, [provider]: true }))
    try {
      const result = await window.api.proxy.startOAuth(provider)
      addToast(t('settings.proxy.oauthStarted'), 'success')
      if (result.state) {
        void pollProxyOAuthStatus(provider, result.state)
      } else {
        setProxyProviderLoading((prev) => ({ ...prev, [provider]: false }))
      }
    } catch (err) {
      setProxyProviderStatus((prev) => ({ ...prev, [provider]: 'error' }))
      setProxyProviderError((prev) => ({
        ...prev,
        [provider]: err instanceof Error ? err.message : String(err)
      }))
      setProxyProviderLoading((prev) => ({ ...prev, [provider]: false }))
      addToast(
        t('settings.proxy.oauthStartFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  async function handleRefreshProxyUsage(): Promise<void> {
    setProxyUsageLoading(true)
    try {
      const usage = await window.api.proxy.getUsageSummary()
      setProxyUsage({
        totalRequests: usage.totalRequests,
        successCount: usage.successCount,
        failureCount: usage.failureCount,
        totalTokens: usage.totalTokens
      })
    } catch (err) {
      addToast(
        t('settings.proxy.usageFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setProxyUsageLoading(false)
    }
  }

  const tabs: Array<{ id: SettingsTab; label: string }> = [
    { id: 'general', label: t('settings.tab.general') },
    { id: 'connection', label: t('settings.tab.connection') },
    { id: 'proxy', label: t('settings.tab.proxy') },
    { id: 'usage', label: t('settings.tab.usage') },
    { id: 'channels', label: t('settings.tab.channels') },
    { id: 'archivedThreads', label: t('settings.tab.archivedThreads') }
  ]
  if (mcpEnabled) {
    tabs.push({ id: 'mcp', label: 'MCP' })
  }

  return (
    <div
      aria-label={t('settings.title')}
      style={{
        display: 'flex',
        flexDirection: 'row',
        height: '100%',
        minHeight: 0,
        backgroundColor: 'var(--bg-primary)'
      }}
    >
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

      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          flex: 1,
          minWidth: 0,
          minHeight: 0
        }}
      >
        <header
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '12px',
            padding: '16px 20px',
            borderBottom: '1px solid var(--border-default)',
            flexShrink: 0
          }}
        >
          <BackToAppButton onClick={closeSettings} />
          <h1 style={{ margin: 0, fontSize: '18px', fontWeight: 600, color: 'var(--text-primary)' }}>
            {t('settings.title')}
          </h1>
        </header>

        <main style={{ flex: 1, minWidth: 0, overflowY: 'auto', padding: '20px' }}>
          <div style={{ maxWidth: activeSettingsTab === 'mcp' ? '760px' : '560px' }}>
            {activeSettingsTab === 'general' && (
              <SettingsGroup title={t('settings.group.general')}>
                <SettingsRow
                  label={t('settings.language')}
                  htmlFor="settings-language"
                  control={
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
                  }
                />

                <SettingsRow
                  label={t('settings.theme')}
                  htmlFor="settings-theme"
                  control={
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
                  }
                />

                <SettingsRow>
                  <div style={{ fontSize: '12px', color: 'var(--text-dimmed)' }}>
                    DotCraft Desktop {t('settings.version')} {version}
                  </div>
                </SettingsRow>
              </SettingsGroup>
            )}

            {activeSettingsTab === 'connection' && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                <SettingsGroup title={t('settings.group.connection')}>
                  <SettingsRow
                    orientation="block"
                    label={t('settings.connectionMode')}
                    description={t('settings.connectionModeHint')}
                    htmlFor="settings-connection-mode"
                  >
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
                  </SettingsRow>

                  {(connectionMode === 'websocket' || connectionMode === 'stdioAndWebSocket') && (
                    <SettingsRow orientation="block" label={t('settings.wsHost')} htmlFor="settings-ws-host">
                      <div style={{ display: 'grid', gridTemplateColumns: '1fr 120px', gap: '8px' }}>
                        <input
                          id="settings-ws-host"
                          type="text"
                          value={wsHost}
                          onChange={(e) => setWsHost(e.target.value)}
                          placeholder={DEFAULT_WS_HOST}
                          style={inputStyle(true)}
                        />
                        <input
                          id="settings-ws-port"
                          type="number"
                          value={wsPort}
                          onChange={(e) => setWsPort(e.target.value)}
                          placeholder={String(DEFAULT_WS_PORT)}
                          min={1}
                          max={65535}
                          style={inputStyle(true)}
                          aria-label={t('settings.wsPort')}
                        />
                      </div>
                    </SettingsRow>
                  )}

                  {connectionMode === 'remote' && (
                    <SettingsRow
                      orientation="block"
                      label={t('settings.remoteUrl')}
                      htmlFor="settings-remote-url"
                    >
                      <input
                        id="settings-remote-url"
                        type="text"
                        value={remoteUrl}
                        onChange={(e) => setRemoteUrl(e.target.value)}
                        placeholder="ws://127.0.0.1:9100/ws"
                        style={inputStyle(true)}
                      />
                      <label style={{ ...sectionLabelStyle(), marginTop: '10px' }}>
                        {t('settings.remoteToken')}
                      </label>
                      <SecretInput
                        value={remoteToken}
                        onChange={setRemoteToken}
                        placeholder={t('settings.remoteTokenPlaceholder')}
                        style={inputStyle(true)}
                      />
                    </SettingsRow>
                  )}
                </SettingsGroup>

                <SettingsGroup title={t('settings.appServerBinary')} description={t('settings.binaryHint')}>
                  <SettingsRow orientation="block">
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                      {(['bundled', 'path', 'custom'] as BinarySource[]).map((source) => {
                        const active = binarySource === source
                        const titleKey =
                          source === 'bundled'
                            ? 'settings.binarySource.bundled'
                            : source === 'path'
                              ? 'settings.binarySource.path'
                              : 'settings.binarySource.custom'
                        const descKey =
                          source === 'bundled'
                            ? 'settings.binarySource.bundledDesc'
                            : source === 'path'
                              ? 'settings.binarySource.pathDesc'
                              : 'settings.binarySource.customDesc'
                        const showResolved = !resolvingBinary && !!resolvedBinaryPath
                        const showError = !resolvingBinary && !resolvedBinaryPath
                        const errorText =
                          source === 'bundled'
                            ? t('settings.binaryNotFound.bundled')
                            : source === 'path'
                              ? t('settings.binaryNotFound.path')
                              : t('settings.binaryNotFound.custom')
                        return (
                          <SelectionCard
                            key={source}
                            name="settings-binary-source"
                            value={source}
                            active={active}
                            onSelect={() => setBinarySource(source)}
                            title={t(titleKey)}
                            description={t(descKey)}
                            resolvedBadge={
                              showResolved ? <ResolvedPill label={t('settings.binaryResolved')} /> : undefined
                            }
                            errorHint={showError ? errorText : undefined}
                            extra={
                              source === 'custom' ? (
                                <InputWithAction
                                  id="settings-binary-path"
                                  inputRef={inputRef}
                                  mono
                                  value={binaryPath}
                                  onChange={(e) => setBinaryPath(e.target.value)}
                                  placeholder={t('settings.binaryPlaceholder')}
                                  onInputClick={(e) => e.stopPropagation()}
                                  actionIcon={<FolderIcon size={16} />}
                                  actionLabel={t('settings.binaryBrowse')}
                                  onAction={(e) => {
                                    e.stopPropagation()
                                    void handlePickBinary()
                                  }}
                                />
                              ) : undefined
                            }
                          />
                        )
                      })}
                      {resolvingBinary && (
                        <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', lineHeight: 1.5 }}>
                          {t('settings.binaryResolving')}
                        </div>
                      )}
                    </div>
                  </SettingsRow>

                  {canRestartManagedAppServer && (
                    <SettingsRow
                      label={t('settings.appServerControl')}
                      description={t('settings.restartAppServerHint')}
                      control={
                        <button
                          type="button"
                          aria-label={
                            restartingAppServer
                              ? t('settings.restartingAppServer')
                              : t('settings.restartAppServer')
                          }
                          onClick={() => {
                            void handleRestartManagedAppServer()
                          }}
                          disabled={restartingAppServer || saving}
                          style={secondaryActionButtonStyle(restartingAppServer || saving)}
                        >
                          <RefreshIcon
                            size={16}
                            style={restartingAppServer ? { animation: 'spin 0.8s linear infinite' } : undefined}
                          />
                          <span>
                            {restartingAppServer ? t('settings.action.restarting') : t('settings.action.restart')}
                          </span>
                        </button>
                      }
                    />
                  )}
                </SettingsGroup>
              </div>
            )}

            {activeSettingsTab === 'proxy' && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                <SettingsGroup title={t('settings.group.proxyToggle')}>
                  <SettingsRow
                    label={t('settings.proxy.enable')}
                    description={t('settings.proxy.enableHint')}
                    control={<PillSwitch checked={proxyEnabled} onChange={setProxyEnabled} />}
                  />
                  <SettingsRow
                    label={
                      <span style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                        <span>{t('settings.proxy.restart')}</span>
                        <ProxyRuntimeStatusPill
                          status={proxyStatusText}
                          label={t(`settings.proxy.status.${proxyStatusText}`)}
                        />
                      </span>
                    }
                    description={
                      <>
                        {t('settings.proxy.restartHint')}
                        {proxyStatusError && (
                          <div style={{ fontSize: '12px', color: 'var(--error)', marginTop: '6px' }}>
                            {proxyStatusError}
                          </div>
                        )}
                      </>
                    }
                    control={
                      <button
                        type="button"
                        aria-label={restartingProxy ? t('settings.proxy.restarting') : t('settings.proxy.restart')}
                        onClick={() => {
                          void handleRestartProxy()
                        }}
                        disabled={restartingProxy || saving || !proxyEnabled}
                        style={secondaryActionButtonStyle(restartingProxy || saving || !proxyEnabled)}
                      >
                        <RefreshIcon
                          size={16}
                          style={restartingProxy ? { animation: 'spin 0.8s linear infinite' } : undefined}
                        />
                        <span>{restartingProxy ? t('settings.action.restarting') : t('settings.action.restart')}</span>
                      </button>
                    }
                  />
                </SettingsGroup>

                <SettingsGroup
                  title={t('settings.group.proxyConfig')}
                  style={{
                    opacity: proxyEnabled ? 1 : 0.55,
                    pointerEvents: proxyEnabled ? 'auto' : 'none'
                  }}
                >
                  <SettingsRow
                    orientation="block"
                    label={t('settings.proxy.authDir')}
                    htmlFor="settings-proxy-auth-dir"
                  >
                    <InputWithAction
                      id="settings-proxy-auth-dir"
                      value={proxyAuthDir}
                      onChange={(e) => setProxyAuthDir(e.target.value)}
                      placeholder="~/.cli-proxy-api"
                      mono
                      actionIcon={<FolderIcon size={16} />}
                      actionLabel={t('settings.binaryBrowse')}
                      onAction={() => {
                        void handlePickProxyAuthDir()
                      }}
                    />
                  </SettingsRow>
                  <SettingsRow
                    label={t('settings.proxy.port')}
                    htmlFor="settings-proxy-port"
                    control={
                      <input
                        id="settings-proxy-port"
                        type="number"
                        value={proxyPort}
                        onChange={(e) => setProxyPort(e.target.value)}
                        min={1}
                        max={65535}
                        style={{ ...inputStyle(true), width: 130 }}
                      />
                    }
                  />
                </SettingsGroup>

                <SettingsGroup
                  title={t('settings.proxy.binaryTitle')}
                  style={{
                    opacity: proxyEnabled ? 1 : 0.55,
                    pointerEvents: proxyEnabled ? 'auto' : 'none'
                  }}
                >
                  <SettingsRow orientation="block">
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                      {(['bundled', 'path', 'custom'] as BinarySource[]).map((source) => {
                        const active = proxyBinarySource === source
                        const titleKey =
                          source === 'bundled'
                            ? 'settings.binarySource.bundled'
                            : source === 'path'
                              ? 'settings.binarySource.path'
                              : 'settings.binarySource.custom'
                        const descKey =
                          source === 'bundled'
                            ? 'settings.proxy.binarySource.bundledDesc'
                            : source === 'path'
                              ? 'settings.proxy.binarySource.pathDesc'
                              : 'settings.proxy.binarySource.customDesc'
                        const showResolved = !resolvingProxyBinary && !!resolvedProxyBinaryPath
                        const showError = !resolvingProxyBinary && !resolvedProxyBinaryPath
                        const errorText =
                          source === 'bundled'
                            ? t('settings.binaryNotFound.bundled')
                            : source === 'path'
                              ? t('settings.binaryNotFound.path')
                              : t('settings.binaryNotFound.custom')
                        return (
                          <SelectionCard
                            key={`proxy-${source}`}
                            name="settings-proxy-binary-source"
                            value={source}
                            active={active}
                            onSelect={() => setProxyBinarySource(source)}
                            title={t(titleKey)}
                            description={t(descKey)}
                            resolvedBadge={
                              showResolved ? <ResolvedPill label={t('settings.binaryResolved')} /> : undefined
                            }
                            errorHint={showError ? errorText : undefined}
                            extra={
                              source === 'custom' ? (
                                <InputWithAction
                                  mono
                                  value={proxyBinaryPath}
                                  onChange={(e) => setProxyBinaryPath(e.target.value)}
                                  placeholder={t('settings.proxy.binaryPlaceholder')}
                                  onInputClick={(e) => e.stopPropagation()}
                                  actionIcon={<FolderIcon size={16} />}
                                  actionLabel={t('settings.binaryBrowse')}
                                  onAction={(e) => {
                                    e.stopPropagation()
                                    void handlePickProxyBinary()
                                  }}
                                />
                              ) : undefined
                            }
                          />
                        )
                      })}
                      {resolvingProxyBinary && (
                        <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', lineHeight: 1.5 }}>
                          {t('settings.binaryResolving')}
                        </div>
                      )}
                    </div>
                  </SettingsRow>
                </SettingsGroup>

                <SettingsGroup
                  title={t('settings.proxy.oauthTitle')}
                  description={t('settings.proxy.oauthHint')}
                  style={{
                    opacity: proxyEnabled ? 1 : 0.55,
                    pointerEvents: proxyEnabled ? 'auto' : 'none'
                  }}
                >
                  {PROXY_OAUTH_PROVIDERS.map((provider) => {
                    const status = proxyProviderStatus[provider]
                    const statusLabel =
                      status === 'ok'
                        ? t('settings.proxy.oauthStatusOk')
                        : status === 'checking'
                          ? t('settings.proxy.oauthStatusChecking')
                        : status === 'pending'
                          ? t('settings.proxy.oauthStatusPending')
                          : status === 'error'
                            ? t('settings.proxy.oauthStatusErrorShort')
                            : t('settings.proxy.oauthStatusIdle')
                    return (
                      <SettingsRow
                        key={provider}
                        label={t(`settings.proxy.provider.${provider}` as MessageKey)}
                        description={
                          <>
                            {t(`settings.proxy.provider.${provider}Desc` as MessageKey)}
                            {proxyProviderError[provider] && (
                              <div style={{ fontSize: '11px', color: 'var(--error)', marginTop: '4px' }}>
                                {proxyProviderError[provider]}
                              </div>
                            )}
                          </>
                        }
                        control={
                          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                            <ProxyOAuthStatusPill status={status} label={statusLabel} />
                            <button
                              type="button"
                              onClick={() => void handleStartProxyOAuth(provider)}
                              disabled={proxyProviderLoading[provider] || !proxyEnabled}
                              style={secondaryButtonStyle(proxyProviderLoading[provider] || !proxyEnabled)}
                            >
                              {proxyProviderLoading[provider]
                                ? t('settings.proxy.oauthLoading')
                                : t('settings.proxy.oauthLogin')}
                            </button>
                          </div>
                        }
                      />
                    )
                  })}
                </SettingsGroup>

                <div style={{ ...cardStyle(), opacity: proxyEnabled ? 1 : 0.6 }}>
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      gap: '12px',
                      marginBottom: '10px'
                    }}
                  >
                    <div>
                      <div style={{ fontSize: '15px', fontWeight: 700, color: 'var(--text-primary)' }}>
                        {t('settings.proxy.usageTitle')}
                      </div>
                      <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '4px', lineHeight: 1.5 }}>
                        {t('settings.usage.dataSourceHint')}
                      </div>
                    </div>
                    <IconButton
                      icon={<RefreshIcon size={16} style={proxyUsageLoading ? { animation: 'spin 0.8s linear infinite' } : undefined} />}
                      label={proxyUsageLoading ? t('settings.proxy.usageLoading') : t('settings.proxy.refreshUsage')}
                      onClick={() => {
                        void handleRefreshProxyUsage()
                      }}
                      disabled={proxyUsageLoading || !proxyEnabled}
                    />
                  </div>
                  {proxyStatusText !== 'running' && (
                    <div style={{ fontSize: '12px', color: 'var(--text-dimmed)' }}>
                      {t('settings.proxy.status')}: {t(`settings.proxy.status.${proxyStatusText}`)}
                    </div>
                  )}
                  {proxyUsage ? (
                    <div
                      style={{
                        marginTop: '10px',
                        display: 'grid',
                        gridTemplateColumns: 'repeat(4, minmax(0, 1fr))',
                        gap: '8px'
                      }}
                    >
                      {[
                        { key: 'settings.proxy.stat.requests', value: formatCompactNumber(proxyUsage.totalRequests) },
                        { key: 'settings.proxy.stat.success', value: formatCompactNumber(proxyUsage.successCount) },
                        { key: 'settings.proxy.stat.failures', value: formatCompactNumber(proxyUsage.failureCount) },
                        { key: 'settings.proxy.stat.tokens', value: formatCompactNumber(proxyUsage.totalTokens) }
                      ].map((item) => (
                        <div
                          key={item.key}
                          style={{
                            border: '1px solid var(--border-default)',
                            borderRadius: '10px',
                            background: 'var(--bg-primary)',
                            padding: '12px'
                          }}
                        >
                          <div style={{ fontSize: '11px', color: 'var(--text-dimmed)' }}>{t(item.key as MessageKey)}</div>
                          <div style={{ marginTop: '6px', fontSize: '18px', fontWeight: 700, color: 'var(--text-primary)' }}>
                            {item.value}
                          </div>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '10px' }}>
                      {t('settings.usage.empty')}
                    </div>
                  )}
                </div>
              </div>
            )}

            {activeSettingsTab === 'usage' && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                <div style={cardStyle()}>
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      gap: '12px'
                    }}
                  >
                    <div>
                      <div style={{ fontSize: '15px', fontWeight: 700, color: 'var(--text-primary)' }}>
                        {t('settings.usage.dashboardTitle')}
                      </div>
                      <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '4px', lineHeight: 1.5 }}>
                        {t('settings.usage.dashboardHint')}
                      </div>
                    </div>
                    <IconButton
                      icon={<OpenInBrowserIcon size={16} />}
                      label={t('settings.openDashboard')}
                      onClick={() => {
                        if (dashboardUrl) void window.api.shell.openExternal(dashboardUrl)
                      }}
                      disabled={!dashboardUrl}
                    />
                  </div>
                </div>
              </div>
            )}

            {activeSettingsTab === 'channels' && (
              <SettingsGroup
                title={t('settings.crossChannelVisibility')}
                description={t('settings.crossChannelHint')}
              >
                {serverChannels === null && (
                  <SettingsRow>
                    <div style={{ fontSize: '12px', color: 'var(--text-dimmed)' }}>
                      {t('settings.channelListLoading')}
                    </div>
                  </SettingsRow>
                )}
                {serverChannels !== null && channelListError && (
                  <SettingsRow>
                    <div style={{ fontSize: '12px', color: 'var(--text-dimmed)' }}>
                      {t('settings.channelListUnavailable')}
                    </div>
                  </SettingsRow>
                )}
                {serverChannels !== null &&
                  !channelListError &&
                  CATEGORY_ORDER.map((cat) => {
                    const items = channelsByCategory.get(cat)
                    if (!items?.length) return null
                    const labelKey = CATEGORY_LABEL_KEY[cat]
                    return (
                      <SettingsRow
                        key={cat}
                        orientation="block"
                        label={
                          <span
                            style={{
                              fontSize: '10px',
                              fontWeight: 600,
                              textTransform: 'uppercase',
                              letterSpacing: '0.04em',
                              color: 'var(--text-dimmed)'
                            }}
                          >
                            {labelKey ? t(labelKey) : cat}
                          </span>
                        }
                      >
                        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
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
                                  width: '42px',
                                  height: '42px',
                                  display: 'inline-flex',
                                  alignItems: 'center',
                                  justifyContent: 'center',
                                  padding: 0,
                                  borderRadius: '13px',
                                  cursor: 'pointer',
                                  border: selected ? '1px solid var(--accent)' : '1px solid var(--border-default)',
                                  background: selected
                                    ? 'color-mix(in srgb, var(--accent) 12%, var(--bg-secondary))'
                                    : 'var(--bg-secondary)'
                                }}
                                title={t('settings.channelIconTitle', { name: ch.name })}
                                aria-label={t('settings.channelIconTitle', { name: ch.name })}
                              >
                                <ChannelIconBadge
                                  channelName={ch.name}
                                  tooltip={t('settings.channelIconTitle', { name: ch.name })}
                                  active={selected}
                                  size={32}
                                  framed={false}
                                />
                              </button>
                            )
                          })}
                        </div>
                      </SettingsRow>
                    )
                  })}
              </SettingsGroup>
            )}

            {activeSettingsTab === 'mcp' && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                {!mcpEnabled && (
                  <div style={cardStyle()}>
                    <div style={{ fontSize: '14px', color: 'var(--text-primary)' }}>
                      {t('settings.mcp.unsupported')}
                    </div>
                  </div>
                )}

                {mcpEnabled && editingServerName === null && (
                  <>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '12px' }}>
                      <div>
                        <div style={{ fontSize: '18px', fontWeight: 600, color: 'var(--text-primary)' }}>
                          {t('settings.mcp.title')}
                        </div>
                        <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
                          {t('settings.mcp.description')}
                        </div>
                      </div>
                      <button type="button" onClick={() => startMcpDraft()} style={primaryButtonStyle(false)}>
                        {t('settings.mcp.addServer')}
                      </button>
                    </div>

                    {mcpLoading && (
                      <div style={cardStyle()}>
                        <div style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>
                          {t('settings.mcp.loading')}
                        </div>
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
                          {t('settings.mcp.empty.title')}
                        </div>
                        <div style={{ fontSize: '12px', color: 'var(--text-dimmed)' }}>
                          {t('settings.mcp.empty.hint')}
                        </div>
                      </div>
                    )}

                    {!mcpLoading &&
                      !mcpError &&
                      mergedMcpServers.map((server) => {
                        const status = mcpStatuses[server.name.trim().toLowerCase()]
                        const tone = getStatusTone(t, status)
                        const transportLabel =
                          server.transport === 'stdio'
                            ? t('settings.mcp.transport.stdio')
                            : t('settings.mcp.transport.http')
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
                                {transportLabel}
                                {!server.enabled ? t('settings.mcp.disabledSuffix') : ''}
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
                              {typeof status?.toolCount === 'number'
                                ? t('settings.mcp.toolsCountSuffix', { count: status.toolCount })
                                : ''}
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
                          {editingServerName === '__new__'
                            ? t('settings.mcp.addTitle')
                            : t('settings.mcp.editTitle')}
                        </div>
                        <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
                          {t('settings.mcp.editIntro')}
                        </div>
                      </div>
                      <button type="button" onClick={cancelMcpEdit} style={secondaryButtonStyle(false)}>
                        {t('settings.mcp.back')}
                      </button>
                    </div>

                    <div style={cardStyle()}>
                      <label style={sectionLabelStyle()}>{t('settings.mcp.field.name')}</label>
                      <input
                        type="text"
                        value={mcpDraft.name}
                        onChange={(e) => setMcpDraft((prev) => ({ ...prev, name: e.target.value }))}
                        placeholder={t('settings.mcp.field.namePlaceholder')}
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
                              {transport === 'stdio'
                                ? t('settings.mcp.transport.stdio')
                                : t('settings.mcp.transport.http')}
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
                        {t('settings.mcp.field.enabled')}
                      </label>
                    </div>

                    {mcpDraft.transport === 'stdio' && (
                      <>
                        <div style={cardStyle()}>
                          <label style={sectionLabelStyle()}>{t('settings.mcp.field.command')}</label>
                          <input
                            type="text"
                            value={mcpDraft.command ?? ''}
                            onChange={(e) => setMcpDraft((prev) => ({ ...prev, command: e.target.value }))}
                            placeholder="npx"
                            style={inputStyle(true)}
                          />
                        </div>

                        <div style={cardStyle()}>
                          <div style={sectionLabelStyle()}>{t('settings.mcp.field.args')}</div>
                          <EditableValueList
                            rows={argRows}
                            setRows={setArgRows}
                            placeholder={t('settings.mcp.field.argsPlaceholder')}
                          />
                        </div>

                        <div style={cardStyle()}>
                          <div style={sectionLabelStyle()}>{t('settings.mcp.field.env')}</div>
                          <EditableKeyValueList
                            rows={envRows}
                            setRows={setEnvRows}
                            keyPlaceholder={t('settings.mcp.keyPlaceholder')}
                            valuePlaceholder={t('settings.mcp.valuePlaceholder')}
                          />
                        </div>

                        <div style={cardStyle()}>
                          <div style={sectionLabelStyle()}>{t('settings.mcp.field.envForwarding')}</div>
                          <EditableValueList
                            rows={envVarRows}
                            setRows={setEnvVarRows}
                            placeholder={t('settings.mcp.field.envForwardingPlaceholder')}
                          />
                        </div>

                        <div style={cardStyle()}>
                          <label style={sectionLabelStyle()}>{t('settings.mcp.field.cwd')}</label>
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
                          <label style={sectionLabelStyle()}>{t('settings.mcp.field.url')}</label>
                          <input
                            type="text"
                            value={mcpDraft.url ?? ''}
                            onChange={(e) => setMcpDraft((prev) => ({ ...prev, url: e.target.value }))}
                            placeholder="https://example.com/mcp"
                            style={inputStyle(true)}
                          />
                        </div>

                        <div style={cardStyle()}>
                          <label style={sectionLabelStyle()}>{t('settings.mcp.field.bearerEnv')}</label>
                          <input
                            type="text"
                            value={mcpDraft.bearerTokenEnvVar ?? ''}
                            onChange={(e) =>
                              setMcpDraft((prev) => ({ ...prev, bearerTokenEnvVar: e.target.value }))
                            }
                            placeholder={t('settings.mcp.field.bearerEnvPlaceholder')}
                            style={inputStyle(true)}
                          />
                        </div>

                        <div style={cardStyle()}>
                          <div style={sectionLabelStyle()}>{t('settings.mcp.field.httpHeaders')}</div>
                          <EditableKeyValueList
                            rows={httpHeaderRows}
                            setRows={setHttpHeaderRows}
                            keyPlaceholder={t('settings.mcp.headerPlaceholder')}
                            valuePlaceholder={t('settings.mcp.valuePlaceholder')}
                          />
                        </div>

                        <div style={cardStyle()}>
                          <div style={sectionLabelStyle()}>{t('settings.mcp.field.envHeaders')}</div>
                          <EditableKeyValueList
                            rows={envHttpHeaderRows}
                            setRows={setEnvHttpHeaderRows}
                            keyPlaceholder={t('settings.mcp.headerPlaceholder')}
                            valuePlaceholder={t('settings.mcp.field.envForwardingPlaceholder')}
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
                          {mcpTestResult.success ? t('settings.mcp.testSuccess') : t('settings.mcp.testFailed')}
                        </div>
                        {typeof mcpTestResult.toolCount === 'number' && (
                          <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
                            {t('settings.mcp.toolsDiscovered', { count: mcpTestResult.toolCount })}
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
                            {deletingMcp ? t('settings.mcp.deleting') : t('settings.mcp.delete')}
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
                          {testingMcp ? t('settings.mcp.testing') : t('settings.mcp.test')}
                        </button>
                        <button
                          type="button"
                          onClick={() => {
                            void handleMcpSave()
                          }}
                          disabled={savingMcp || deletingMcp}
                          style={primaryButtonStyle(savingMcp || deletingMcp)}
                        >
                          {savingMcp ? t('settings.mcp.saving') : t('settings.mcp.save')}
                        </button>
                      </div>
                    </div>
                  </div>
                )}
              </div>
            )}

            {activeSettingsTab === 'archivedThreads' && (
              <ArchivedThreadsSettingsView
                workspacePath={workspacePath}
                onThreadListRefreshRequested={onThreadListRefreshRequested}
              />
            )}
          </div>
        </main>

      <footer
        style={{
          padding: '12px 20px',
          display: 'flex',
          justifyContent: 'flex-end',
          gap: '8px',
          flexShrink: 0
        }}
      >
        <button type="button" onClick={closeSettings} style={secondaryButtonStyle(false)}>
          {t('common.cancel')}
        </button>
        {activeSettingsTab !== 'mcp' && activeSettingsTab !== 'archivedThreads' && activeSettingsTab !== 'usage' && (
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
    </div>
  )
}
