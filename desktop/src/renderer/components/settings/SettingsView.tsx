import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type JSX } from 'react'
import {
  Archive,
  BarChart3,
  Bot,
  Cable,
  Globe2,
  KeyRound,
  MessageSquare,
  Server,
  Settings as SettingsIcon,
  UserRound,
  type LucideIcon
} from 'lucide-react'
import { addToast } from '../../stores/toastStore'
import { applyTheme, resolveTheme, type ThemeMode } from '../../utils/theme'
import { normalizeLocale, type AppLocale } from '../../../shared/locales'
import { useSetUiLocale, useT } from '../../contexts/LocaleContext'
import type { MessageKey } from '../../../shared/locales'
import { ensureVisibleChannelsSeeded } from '../../utils/visibleChannelsDefaults'
import { mergeAvailableChannels } from '../../utils/availableChannels'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { usePluginStore } from '../../stores/pluginStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { usePendingRestartStore } from '../../stores/pendingRestartStore'
import { useSettingsWorkspaceConfigChangeEffects } from '../../hooks/useSettingsWorkspaceConfigChangeEffects'
import { SecretInput } from '../channels/FormShared'
import { ArchivedThreadsSettingsView } from './ArchivedThreadsSettingsView'
import { FolderIcon, OpenInBrowserIcon, RefreshIcon } from '../ui/AppIcons'
import { ChannelIconBadge } from '../ui/channelMeta'
import { IconButton } from '../ui/IconButton'
import { InputWithAction } from '../ui/InputWithAction'
import { SelectionCard, ResolvedPill } from '../ui/SelectionCard'
import { PillSwitch } from '../ui/PillSwitch'
import { ToggleSwitch } from '../channels/ToggleSwitch'
import { BackToAppButton } from '../ui/BackToAppButton'
import { ActionTooltip } from '../ui/ActionTooltip'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { SettingsGroup, SettingsRow } from './SettingsGroup'
import { SettingsPageHeader } from './SettingsPageHeader'
import { PluginCatalogItem } from '../plugins/PluginCatalogItem'
import { PluginInstallDialog } from '../plugins/PluginInstallDialog'
import {
  EditableKeyValueList,
  EditableValueList,
  normalizeKeyValueRows,
  normalizeValueRows,
  rowsToRecord,
  rowsToValues,
  type KeyValueRow,
  type ValueRow
} from './ui/EditableList'
import { GeneralPanel } from './panels/GeneralPanel'
import { ConnectionPanel } from './panels/ConnectionPanel'
import { ProxyPanel } from './panels/ProxyPanel'
import { ProxyProviderIcon } from './panels/ProxyProviderIcon'
import { UsagePanel } from './panels/UsagePanel'
import { ChannelsPanel } from './panels/ChannelsPanel'
import { McpPanel } from './panels/McpPanel'
import { SubAgentsPanel } from './panels/SubAgentsPanel'
import {
  useMcpStore,
  type McpServerConfigWire,
  type McpServerStatusWire,
  type McpTransport
} from '../../stores/mcpStore'
import type { BinarySource, BrowserUseApprovalMode, ProxyAuthFileSummary, ProxyOAuthProvider } from '../../../preload/api'
import type { WorkspaceConfigChangedPayload } from '../../utils/workspaceConfigChanged'

declare const __APP_VERSION__: string | undefined

interface ChannelInfoWire {
  name: string
  category?: string
}

interface SettingsViewProps {
  workspacePath?: string
  onThreadListRefreshRequested?: () => void
  workspaceConfigChange?: WorkspaceConfigChangedPayload | null
  workspaceConfigChangeSeq?: number
}

interface McpTestResultWire {
  success: boolean
  errorCode?: string
  errorMessage?: string
  toolCount?: number
}

interface WorkspaceCoreConfig {
  apiKey: string | null
  endPoint: string | null
  welcomeSuggestionsEnabled: boolean | null
  skillsSelfLearningEnabled: boolean | null
  memoryAutoConsolidateEnabled: boolean | null
  defaultApprovalPolicy: VisibleApprovalPolicy | null
}

interface WorkspaceCoreConfigResult {
  workspace: WorkspaceCoreConfig
  userDefaults: WorkspaceCoreConfig
}

const EMPTY_WORKSPACE_CORE_CONFIG: WorkspaceCoreConfig = {
  apiKey: null,
  endPoint: null,
  welcomeSuggestionsEnabled: null,
  skillsSelfLearningEnabled: null,
  memoryAutoConsolidateEnabled: null,
  defaultApprovalPolicy: null
}

type VisibleApprovalPolicy = 'default' | 'autoApprove'

function normalizeVisibleApprovalPolicy(value: unknown): VisibleApprovalPolicy | null {
  return value === 'default' || value === 'autoApprove' ? value : null
}

function normalizeWorkspaceCoreConfig(value: unknown): WorkspaceCoreConfig {
  const source = value != null && typeof value === 'object' ? value as Partial<WorkspaceCoreConfig> : {}
  return {
    apiKey: typeof source.apiKey === 'string' ? source.apiKey : null,
    endPoint: typeof source.endPoint === 'string' ? source.endPoint : null,
    welcomeSuggestionsEnabled:
      typeof source.welcomeSuggestionsEnabled === 'boolean'
        ? source.welcomeSuggestionsEnabled
        : null,
    skillsSelfLearningEnabled:
      typeof source.skillsSelfLearningEnabled === 'boolean'
        ? source.skillsSelfLearningEnabled
        : null,
    memoryAutoConsolidateEnabled:
      typeof source.memoryAutoConsolidateEnabled === 'boolean'
        ? source.memoryAutoConsolidateEnabled
        : null,
    defaultApprovalPolicy: normalizeVisibleApprovalPolicy(source.defaultApprovalPolicy)
  }
}

function createEmptyWorkspaceCoreResult(): WorkspaceCoreConfigResult {
  return {
    workspace: { ...EMPTY_WORKSPACE_CORE_CONFIG },
    userDefaults: { ...EMPTY_WORKSPACE_CORE_CONFIG }
  }
}

function normalizeWorkspaceCoreResult(value: unknown): WorkspaceCoreConfigResult {
  if (value == null || typeof value !== 'object') {
    return createEmptyWorkspaceCoreResult()
  }

  const source = value as Partial<WorkspaceCoreConfigResult>
  return {
    workspace: normalizeWorkspaceCoreConfig(source.workspace),
    userDefaults: normalizeWorkspaceCoreConfig(source.userDefaults)
  }
}

type WorkspaceCoreReadApi = {
  workspaceConfig?: {
    getCore?: (() => Promise<unknown>) | undefined
  } | undefined
} | undefined

function getWorkspaceCoreReader(api: WorkspaceCoreReadApi): (() => Promise<unknown>) | null {
  const getCore = api?.workspaceConfig?.getCore
  return typeof getCore === 'function' ? getCore : null
}

export async function readWorkspaceCoreSafeFromApi(
  api: WorkspaceCoreReadApi
): Promise<WorkspaceCoreConfigResult> {
  const getCore = getWorkspaceCoreReader(api)
  if (!getCore) {
    return createEmptyWorkspaceCoreResult()
  }

  try {
    return normalizeWorkspaceCoreResult(await getCore())
  } catch {
    return createEmptyWorkspaceCoreResult()
  }
}

export async function readWorkspaceCoreStrictFromApi(
  api: WorkspaceCoreReadApi
): Promise<WorkspaceCoreConfigResult> {
  const getCore = getWorkspaceCoreReader(api)
  if (!getCore) {
    throw new Error('Workspace core API is unavailable')
  }

  return normalizeWorkspaceCoreResult(await getCore())
}

type ConnectionMode = 'local' | 'remote'
type SettingsTab = 'general' | 'personalization' | 'connection' | 'proxy' | 'browserUse' | 'usage' | 'channels' | 'archivedThreads' | 'mcp' | 'subAgents'
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

function createProxyProviderMap<T>(value: T): Record<ProxyOAuthProvider, T> {
  return {
    codex: value,
    claude: value,
    gemini: value,
    qwen: value,
    iflow: value
  }
}

function isProxyProviderMapAllEqual<T>(
  map: Record<ProxyOAuthProvider, T>,
  value: T
): boolean {
  for (const provider of PROXY_OAUTH_PROVIDERS) {
    if (map[provider] !== value) {
      return false
    }
  }
  return true
}

function isAuthenticatedProxyAuthFile(file: ProxyAuthFileSummary, provider?: ProxyOAuthProvider): boolean {
  return AUTHENTICATED_PROXY_AUTH_STATUSES.has(file.status) &&
    !file.disabled &&
    !file.unavailable &&
    (provider === undefined || file.provider === provider)
}

function getProxyOAuthCallbackPort(provider: ProxyOAuthProvider): number | null {
  switch (provider) {
    case 'codex':
      return 1455
    case 'claude':
      return 54545
    case 'gemini':
      return 8085
    default:
      return null
  }
}

function appendProxyOAuthDiagnostics(
  message: string,
  provider: ProxyOAuthProvider,
  authDir: string
): string {
  const callbackPort = getProxyOAuthCallbackPort(provider)
  const authDirText = authDir.trim() || 'default app data proxy auths'
  const details = callbackPort == null
    ? `authDir: ${authDirText}`
    : `callbackPort: ${callbackPort}; authDir: ${authDirText}; localhost/IPv6 callback may be unreachable on macOS`
  return `${message} (${details})`
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

function isPluginManagedMcpServer(server: McpServerConfigWire, originsEnabled: boolean): boolean {
  return (originsEnabled && server.origin?.kind === 'plugin') || server.readOnly === true
}

function mcpPluginSourceLabel(server: McpServerConfigWire, t: (key: MessageKey | string, vars?: Record<string, string | number>) => string): string {
  return t('settings.mcp.origin.fromPlugin', {
    plugin: server.origin?.pluginDisplayName || server.origin?.pluginId || 'plugin'
  })
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
    padding: '14px 16px'
  }
}

function settingsMainStyle(): CSSProperties {
  return {
    flex: 1,
    minWidth: 0,
    overflowY: 'auto',
    padding: '20px',
    scrollbarGutter: 'stable'
  }
}

function settingsContentContainerStyle(): CSSProperties {
  return {
    width: '100%',
    maxWidth: '760px',
    margin: '0 auto',
    boxSizing: 'border-box'
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

function normalizeBrowserUseDomainInput(input: string): string | null {
  const trimmed = input.trim()
  if (!trimmed || /[\u0000-\u001f]/.test(trimmed)) return null
  const candidate = /^[a-zA-Z][a-zA-Z\d+\-.]*:/.test(trimmed)
    ? trimmed
    : `https://${trimmed}`
  try {
    const domain = new URL(candidate).hostname.trim().toLowerCase().replace(/\.+$/, '')
    return domain || null
  } catch {
    return null
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

function mcpSourcePillStyle(): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    minHeight: 20,
    padding: '2px 7px',
    borderRadius: 999,
    backgroundColor: 'var(--bg-tertiary)',
    color: 'var(--text-secondary)',
    fontSize: 11,
    fontWeight: 600
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

export function SettingsView({
  workspacePath,
  onThreadListRefreshRequested,
  workspaceConfigChange = null,
  workspaceConfigChangeSeq = 0
}: SettingsViewProps): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const setUiLocale = useSetUiLocale()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const setExpectedRestart = useConnectionStore((s) => s.setExpectedRestart)
  const dashboardUrl = useConnectionStore((s) => s.dashboardUrl)
  const plugins = usePluginStore((s) => s.plugins)
  const fetchPlugins = usePluginStore((s) => s.fetchPlugins)
  const installPlugin = usePluginStore((s) => s.installPlugin)
  const fetchSkills = useSkillsStore((s) => s.fetchSkills)
  const mcpStatuses = useMcpStore((s) => s.statuses)
  const setMcpStatuses = useMcpStore((s) => s.setStatuses)
  const [binarySource, setBinarySource] = useState<BinarySource>('bundled')
  const [binaryPath, setBinaryPath] = useState('')
  const [resolvedBinaryPath, setResolvedBinaryPath] = useState<string | null>(null)
  const [resolvingBinary, setResolvingBinary] = useState(false)
  const [connectionMode, setConnectionMode] = useState<ConnectionMode>('local')
  const [, setSavedConnectionMode] = useState<ConnectionMode>('local')
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
  const [browserUseApprovalMode, setBrowserUseApprovalMode] = useState<BrowserUseApprovalMode>('alwaysAsk')
  const [browserUseBlockedDomains, setBrowserUseBlockedDomains] = useState<string[]>([])
  const [browserUseAllowedDomains, setBrowserUseAllowedDomains] = useState<string[]>([])
  const [browserUseDomainDraft, setBrowserUseDomainDraft] = useState('')
  const [browserUseDomainTarget, setBrowserUseDomainTarget] = useState<'blocked' | 'allowed' | null>(null)
  const [browserUseDomainError, setBrowserUseDomainError] = useState('')
  const [clearingBrowserCookies, setClearingBrowserCookies] = useState(false)
  const [browserUseInstallOpen, setBrowserUseInstallOpen] = useState(false)
  const [browserUseInstalling, setBrowserUseInstalling] = useState(false)
  const [baselineConnection, setBaselineConnection] = useState<{
    binarySource: BinarySource
    binaryPath: string
    connectionMode: ConnectionMode
    wsHost: string
    wsPort: string
    remoteUrl: string
    remoteToken: string
  } | null>(null)
  const [baselineProxy, setBaselineProxy] = useState<{
    enabled: boolean
    port: string
    authDir: string
    binarySource: BinarySource
    binaryPath: string
  } | null>(null)
  const [workspaceCoreBaseline, setWorkspaceCoreBaseline] = useState<WorkspaceCoreConfig>({
    apiKey: null,
    endPoint: null,
    welcomeSuggestionsEnabled: null,
    skillsSelfLearningEnabled: null,
    memoryAutoConsolidateEnabled: null,
    defaultApprovalPolicy: null
  })
  const [userDefaultCore, setUserDefaultCore] = useState<WorkspaceCoreConfig>({
    apiKey: null,
    endPoint: null,
    welcomeSuggestionsEnabled: null,
    skillsSelfLearningEnabled: null,
    memoryAutoConsolidateEnabled: null,
    defaultApprovalPolicy: null
  })
  const [apiKeyOverrideActive, setApiKeyOverrideActive] = useState(true)
  const [endPointOverrideActive, setEndPointOverrideActive] = useState(true)
  const [llmApiKey, setLlmApiKey] = useState('')
  const [llmEndPoint, setLlmEndPoint] = useState('')
  const [, setApplyingLlm] = useState(false)
  const [welcomeSuggestionsEnabled, setWelcomeSuggestionsEnabled] = useState(true)
  const [applyingWelcomeSuggestions, setApplyingWelcomeSuggestions] = useState(false)
  const [selfLearningEnabled, setSelfLearningEnabled] = useState(true)
  const [applyingSelfLearning, setApplyingSelfLearning] = useState(false)
  const [selfLearningRestartPending, setSelfLearningRestartPending] = useState(false)
  const [memoryAutoConsolidateEnabled, setMemoryAutoConsolidateEnabled] = useState(true)
  const [applyingMemoryAutoConsolidate, setApplyingMemoryAutoConsolidate] = useState(false)
  const [defaultApprovalPolicy, setDefaultApprovalPolicy] = useState<VisibleApprovalPolicy>('default')
  const [applyingDefaultApprovalPolicy, setApplyingDefaultApprovalPolicy] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)
  const workspaceCoreApiAvailable = getWorkspaceCoreReader(window.api) != null

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
  const [togglingServerName, setTogglingServerName] = useState<string | null>(null)
  const [mcpTestResult, setMcpTestResult] = useState<McpTestResultWire | null>(null)
  const [mcpSavedHint, setMcpSavedHint] = useState('')
  const [subAgentRefreshTick, setSubAgentRefreshTick] = useState(0)

  const mcpEnabled = capabilities?.mcpManagement === true
  const mcpOriginsEnabled = capabilities?.mcpServerOrigins === true
  const subAgentEnabled = capabilities?.subAgentManagement === true
  const pluginManagementEnabled = capabilities?.pluginManagement === true
  const browserUsePlugin = plugins.find((plugin) => plugin.id === 'browser-use') ?? null
  const browserUsePluginReady = !pluginManagementEnabled || browserUsePlugin?.installed === true
  const proxyLockActive = proxyStatusText === 'running'
  const llmApiKeyTrimmed = llmApiKey.trim()
  const llmEndPointTrimmed = llmEndPoint.trim()
  const apiKeyMatchesInheritedDefault =
    apiKeyOverrideActive &&
    (workspaceCoreBaseline.apiKey ?? '') === '' &&
    userDefaultCore.apiKey != null &&
    llmApiKeyTrimmed === userDefaultCore.apiKey
  const endPointMatchesInheritedDefault =
    endPointOverrideActive &&
    (workspaceCoreBaseline.endPoint ?? '') === '' &&
    userDefaultCore.endPoint != null &&
    llmEndPointTrimmed === userDefaultCore.endPoint
  const llmDirty =
    (llmApiKeyTrimmed !== (workspaceCoreBaseline.apiKey ?? '') && !apiKeyMatchesInheritedDefault) ||
    (llmEndPointTrimmed !== (workspaceCoreBaseline.endPoint ?? '') && !endPointMatchesInheritedDefault)
  const showApiKeyInheritedHint = !proxyLockActive && !apiKeyOverrideActive && (userDefaultCore.apiKey ?? '') !== ''
  const showEndPointInheritedHint = !proxyLockActive && !endPointOverrideActive && (userDefaultCore.endPoint ?? '') !== ''
  const connectionDirty =
    baselineConnection != null &&
    (binarySource !== baselineConnection.binarySource ||
      binaryPath.trim() !== baselineConnection.binaryPath.trim() ||
      connectionMode !== baselineConnection.connectionMode ||
      wsHost.trim() !== baselineConnection.wsHost.trim() ||
      wsPort.trim() !== baselineConnection.wsPort.trim() ||
      remoteUrl.trim() !== baselineConnection.remoteUrl.trim() ||
      remoteToken.trim() !== baselineConnection.remoteToken.trim())
  const proxyDirty =
    baselineProxy != null &&
    (proxyEnabled !== baselineProxy.enabled ||
      proxyPort.trim() !== baselineProxy.port.trim() ||
      proxyAuthDir.trim() !== baselineProxy.authDir.trim() ||
      proxyBinarySource !== baselineProxy.binarySource ||
      proxyBinaryPath.trim() !== baselineProxy.binaryPath.trim())

  function applyWorkspaceCoreBaseline(core: WorkspaceCoreConfigResult, keepDraftValues: boolean): void {
    setWorkspaceCoreBaseline(core.workspace)
    setUserDefaultCore(core.userDefaults)

    const resolvedWelcomeSuggestionsEnabled =
      core.workspace.welcomeSuggestionsEnabled ??
      core.userDefaults.welcomeSuggestionsEnabled ??
      true
    setWelcomeSuggestionsEnabled(resolvedWelcomeSuggestionsEnabled)
    const resolvedSelfLearningEnabled =
      core.workspace.skillsSelfLearningEnabled ??
      core.userDefaults.skillsSelfLearningEnabled ??
      true
    setSelfLearningEnabled(resolvedSelfLearningEnabled)
    const resolvedMemoryAutoConsolidateEnabled =
      core.workspace.memoryAutoConsolidateEnabled ??
      core.userDefaults.memoryAutoConsolidateEnabled ??
      true
    setMemoryAutoConsolidateEnabled(resolvedMemoryAutoConsolidateEnabled)
    const resolvedDefaultApprovalPolicy =
      core.workspace.defaultApprovalPolicy ??
      core.userDefaults.defaultApprovalPolicy ??
      'default'
    setDefaultApprovalPolicy(resolvedDefaultApprovalPolicy)

    if (keepDraftValues) {
      return
    }

    const inheritedApiKey = core.userDefaults.apiKey ?? ''
    const inheritedEndPoint = core.userDefaults.endPoint ?? ''
    const hasWorkspaceApiKey = (core.workspace.apiKey ?? '') !== ''
    const hasWorkspaceEndPoint = (core.workspace.endPoint ?? '') !== ''

    setApiKeyOverrideActive(hasWorkspaceApiKey || inheritedApiKey === '')
    setEndPointOverrideActive(hasWorkspaceEndPoint || inheritedEndPoint === '')
    setLlmApiKey(hasWorkspaceApiKey ? (core.workspace.apiKey ?? '') : '')
    setLlmEndPoint(hasWorkspaceEndPoint ? (core.workspace.endPoint ?? '') : '')
  }

  function hasWorkspaceCoreChanged(nextWorkspaceCore: WorkspaceCoreConfig): boolean {
    return (
      (nextWorkspaceCore.apiKey ?? '') !== (workspaceCoreBaseline.apiKey ?? '') ||
      (nextWorkspaceCore.endPoint ?? '') !== (workspaceCoreBaseline.endPoint ?? '')
    )
  }

  async function readWorkspaceCoreSafe(): Promise<WorkspaceCoreConfigResult> {
    return readWorkspaceCoreSafeFromApi(window.api)
  }

  async function readWorkspaceCoreStrict(): Promise<WorkspaceCoreConfigResult> {
    return readWorkspaceCoreStrictFromApi(window.api)
  }

  async function reloadWorkspaceCore(): Promise<void> {
    const core = await readWorkspaceCoreSafe()
    applyWorkspaceCoreBaseline(core, llmDirty)
  }

  const handleWelcomeSuggestionsToggle = useCallback(
    async (checked: boolean): Promise<void> => {
      const previous = welcomeSuggestionsEnabled
      setWelcomeSuggestionsEnabled(checked)
      setApplyingWelcomeSuggestions(true)
      try {
        const result = await window.api.appServer.sendRequest('workspace/config/update', {
          welcomeSuggestionsEnabled: checked
        }) as { welcomeSuggestionsEnabled?: boolean | null }
        const persisted = typeof result?.welcomeSuggestionsEnabled === 'boolean'
          ? result.welcomeSuggestionsEnabled
          : checked
        setWelcomeSuggestionsEnabled(persisted)
        await reloadWorkspaceCore()
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setWelcomeSuggestionsEnabled(previous)
        addToast(t('settings.personalization.welcomeSuggestionsSaveFailed', { error: msg }), 'error')
      } finally {
        setApplyingWelcomeSuggestions(false)
      }
    },
    [reloadWorkspaceCore, t, welcomeSuggestionsEnabled]
  )

  const handleSelfLearningToggle = useCallback(
    async (checked: boolean): Promise<void> => {
      const previous = selfLearningEnabled
      setSelfLearningEnabled(checked)
      setApplyingSelfLearning(true)
      try {
        const result = await window.api.appServer.sendRequest('workspace/config/update', {
          skillsSelfLearningEnabled: checked
        }) as { skillsSelfLearningEnabled?: boolean | null }
        const persisted = typeof result?.skillsSelfLearningEnabled === 'boolean'
          ? result.skillsSelfLearningEnabled
          : checked
        setSelfLearningEnabled(persisted)
        setSelfLearningRestartPending(true)
        await reloadWorkspaceCore()
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setSelfLearningEnabled(previous)
        addToast(t('settings.personalization.selfLearningSaveFailed', { error: msg }), 'error')
      } finally {
        setApplyingSelfLearning(false)
      }
    },
    [reloadWorkspaceCore, selfLearningEnabled, t]
  )

  const handleMemoryAutoConsolidateToggle = useCallback(
    async (checked: boolean): Promise<void> => {
      const previous = memoryAutoConsolidateEnabled
      setMemoryAutoConsolidateEnabled(checked)
      setApplyingMemoryAutoConsolidate(true)
      try {
        const result = await window.api.appServer.sendRequest('workspace/config/update', {
          memoryAutoConsolidateEnabled: checked
        }) as { memoryAutoConsolidateEnabled?: boolean | null }
        const persisted = typeof result?.memoryAutoConsolidateEnabled === 'boolean'
          ? result.memoryAutoConsolidateEnabled
          : checked
        setMemoryAutoConsolidateEnabled(persisted)
        await reloadWorkspaceCore()
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setMemoryAutoConsolidateEnabled(previous)
        addToast(t('settings.personalization.longTermMemorySaveFailed', { error: msg }), 'error')
      } finally {
        setApplyingMemoryAutoConsolidate(false)
      }
    },
    [memoryAutoConsolidateEnabled, reloadWorkspaceCore, t]
  )

  const handleDefaultApprovalPolicyChange = useCallback(
    async (nextPolicy: VisibleApprovalPolicy): Promise<boolean> => {
      if (nextPolicy === defaultApprovalPolicy || applyingDefaultApprovalPolicy) return false

      if (nextPolicy === 'autoApprove') {
        const confirmed = await confirm({
          title: t('settings.permissions.fullAccess.warningTitle'),
          message: t('settings.permissions.fullAccess.warningBody'),
          confirmLabel: t('settings.permissions.fullAccess.warningConfirm'),
          cancelLabel: t('common.cancel'),
          danger: true
        })
        if (!confirmed) return false
      }

      const previous = defaultApprovalPolicy
      setDefaultApprovalPolicy(nextPolicy)
      setApplyingDefaultApprovalPolicy(true)
      try {
        const result = await window.api.appServer.sendRequest('workspace/config/update', {
          defaultApprovalPolicy: nextPolicy
        }) as { defaultApprovalPolicy?: string | null }
        const persisted = normalizeVisibleApprovalPolicy(result?.defaultApprovalPolicy) ?? nextPolicy
        setDefaultApprovalPolicy(persisted)
        await reloadWorkspaceCore()
        return true
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setDefaultApprovalPolicy(previous)
        addToast(t('settings.permissions.saveFailed', { error: msg }), 'error')
        return false
      } finally {
        setApplyingDefaultApprovalPolicy(false)
      }
    },
    [applyingDefaultApprovalPolicy, confirm, defaultApprovalPolicy, reloadWorkspaceCore, t]
  )

  useSettingsWorkspaceConfigChangeEffects({
    change: workspaceConfigChange,
    changeSeq: workspaceConfigChangeSeq,
    llmDirty,
    mcpEnabled,
    subAgentEnabled,
    onExternalLlmChangeNotice: () => {
      addToast(t('settings.llm.externalChangeNotice'), 'info')
    },
    reloadWorkspaceCore,
    reloadMcpData: () => Promise.all([reloadMcpServers(), reloadMcpStatuses()]),
    reloadSubAgentData: () => {
      setSubAgentRefreshTick((current) => current + 1)
    },
    clearServerChannels: () => {
      setServerChannels(null)
    }
  })

  useEffect(() => {
    if (activeSettingsTab === 'mcp' && !mcpEnabled) {
      setActiveSettingsTab('general')
    }
    if (activeSettingsTab === 'subAgents' && !subAgentEnabled) {
      setActiveSettingsTab('general')
    }
  }, [activeSettingsTab, mcpEnabled, subAgentEnabled])

  useEffect(() => {
    if (activeSettingsTab === 'browserUse' && pluginManagementEnabled) {
      void fetchPlugins()
    }
  }, [activeSettingsTab, fetchPlugins, pluginManagementEnabled])

  useEffect(() => {
    inputRef.current?.focus()
    window.api.settings
      .get()
      .then(async (s) => {
        const loadedMode = s.connectionMode === 'remote' ? 'remote' : 'local'
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
        setBrowserUseApprovalMode((s.browserUse?.approvalMode ?? 'alwaysAsk') as BrowserUseApprovalMode)
        setBrowserUseBlockedDomains([...(s.browserUse?.blockedDomains ?? [])])
        setBrowserUseAllowedDomains([...(s.browserUse?.allowedDomains ?? [])])
        setBaselineConnection({
          binarySource: (s.binarySource ?? (s.appServerBinaryPath ? 'custom' : 'bundled')) as BinarySource,
          binaryPath: s.appServerBinaryPath ?? '',
          connectionMode: loadedMode,
          wsHost: s.webSocket?.host ?? DEFAULT_WS_HOST,
          wsPort: String(s.webSocket?.port ?? DEFAULT_WS_PORT),
          remoteUrl: s.remote?.url ?? '',
          remoteToken: s.remote?.token ?? ''
        })
        setBaselineProxy({
          enabled: s.proxy?.enabled === true,
          port: String(s.proxy?.port ?? DEFAULT_PROXY_PORT),
          authDir: s.proxy?.authDir ?? '',
          binarySource: (s.proxy?.binarySource ?? (s.proxy?.binaryPath ? 'custom' : 'bundled')) as BinarySource,
          binaryPath: s.proxy?.binaryPath ?? ''
        })
      })
      .catch(() => {})
    setVersion(typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : '0.1.0')
    readWorkspaceCoreSafe()
      .then((core) => {
        applyWorkspaceCoreBaseline(core, false)
      })
    // `readWorkspaceCoreSafe` already normalizes missing bridge / failed reads.
  }, [])

  useEffect(() => {
    if (!workspaceCoreApiAvailable && activeSettingsTab === 'personalization') {
      setActiveSettingsTab('general')
    }
  }, [activeSettingsTab, workspaceCoreApiAvailable])

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
    if (!proxyLockActive) return
    if (
      llmApiKeyTrimmed !== (workspaceCoreBaseline.apiKey ?? '') ||
      llmEndPointTrimmed !== (workspaceCoreBaseline.endPoint ?? '')
    ) {
      setApiKeyOverrideActive(true)
      setEndPointOverrideActive(true)
      setLlmApiKey(workspaceCoreBaseline.apiKey ?? '')
      setLlmEndPoint(workspaceCoreBaseline.endPoint ?? '')
      addToast(t('settings.llm.lockedDiscardedNotice'), 'info')
    }
  }, [proxyLockActive, llmApiKeyTrimmed, llmEndPointTrimmed, workspaceCoreBaseline.apiKey, workspaceCoreBaseline.endPoint, t])

  useEffect(() => {
    let cancelled = false
    if (!proxyEnabled) {
      setProxyAuthRecoveryAttempt((prev) => (prev === 0 ? prev : 0))
      setProxyAuthRecoverySettled((prev) => (prev ? false : prev))
      setProxyProviderStatus((prev) =>
        isProxyProviderMapAllEqual(prev, 'idle') ? prev : createProxyProviderMap<ProxyProviderStatus>('idle')
      )
      setProxyProviderError((prev) =>
        isProxyProviderMapAllEqual(prev, '') ? prev : createProxyProviderMap('')
      )
      setProxyProviderLoading((prev) =>
        isProxyProviderMapAllEqual(prev, false) ? prev : createProxyProviderMap(false)
      )
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
        const mergedChannels = mergeAvailableChannels(serverList, modules)
        setServerChannels(mergedChannels)
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

  useEffect(() => {
    if (!mcpSavedHint) return
    const timer = window.setTimeout(() => setMcpSavedHint(''), 1500)
    return () => window.clearTimeout(timer)
  }, [mcpSavedHint])

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
    if (connectionDirty || proxyDirty || llmDirty) {
      const shouldDiscard = window.confirm(t('settings.pendingChanges.leaveConfirm'))
      if (!shouldDiscard) return
      if (baselineConnection) {
        setBinarySource(baselineConnection.binarySource)
        setBinaryPath(baselineConnection.binaryPath)
        setConnectionMode(baselineConnection.connectionMode)
        setWsHost(baselineConnection.wsHost)
        setWsPort(baselineConnection.wsPort)
        setRemoteUrl(baselineConnection.remoteUrl)
        setRemoteToken(baselineConnection.remoteToken)
      }
      if (baselineProxy) {
        setProxyEnabled(baselineProxy.enabled)
        setProxyPort(baselineProxy.port)
        setProxyAuthDir(baselineProxy.authDir)
        setProxyBinarySource(baselineProxy.binarySource)
        setProxyBinaryPath(baselineProxy.binaryPath)
      }
      setApiKeyOverrideActive((workspaceCoreBaseline.apiKey ?? '') !== '' || (userDefaultCore.apiKey ?? '') === '')
      setEndPointOverrideActive((workspaceCoreBaseline.endPoint ?? '') !== '' || (userDefaultCore.endPoint ?? '') === '')
      setLlmApiKey((workspaceCoreBaseline.apiKey ?? '') !== '' ? (workspaceCoreBaseline.apiKey ?? '') : '')
      setLlmEndPoint((workspaceCoreBaseline.endPoint ?? '') !== '' ? (workspaceCoreBaseline.endPoint ?? '') : '')
    }
    setActiveMainView('conversation')
  }

  function handleActivateApiKeyOverride(): void {
    if (proxyLockActive) return
    if (apiKeyOverrideActive) return
    setApiKeyOverrideActive(true)
    setLlmApiKey(userDefaultCore.apiKey ?? '')
  }

  function handleActivateEndPointOverride(): void {
    if (proxyLockActive) return
    if (endPointOverrideActive) return
    setEndPointOverrideActive(true)
    setLlmEndPoint(userDefaultCore.endPoint ?? '')
  }

  function startMcpDraft(server?: McpServerConfigWire): void {
    if (server && isPluginManagedMcpServer(server, mcpOriginsEnabled)) return
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
      setMcpSavedHint(t('settings.savedToast'))
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

  async function handleMcpQuickToggle(server: McpServerConfigWire, nextEnabled: boolean): Promise<void> {
    if (isPluginManagedMcpServer(server, mcpOriginsEnabled)) return
    setTogglingServerName(server.name)
    try {
      await window.api.appServer.sendRequest('mcp/upsert', {
        server: {
          ...server,
          enabled: nextEnabled
        }
      })
      await Promise.all([reloadMcpServers(), reloadMcpStatuses()])
    } catch (err) {
      addToast(
        `Failed to ${nextEnabled ? 'enable' : 'disable'} MCP server: ${err instanceof Error ? err.message : String(err)}`,
        'error'
      )
    } finally {
      setTogglingServerName((current) => (current === server.name ? null : current))
    }
  }

  async function handleMcpDelete(): Promise<void> {
    const name = (editingServerName !== '__new__' ? editingServerName?.trim() : mcpDraft.name.trim()) ?? ''
    if (!name) return
    setDeletingMcp(true)
    try {
      await window.api.appServer.sendRequest('mcp/remove', { name })
      await Promise.all([reloadMcpServers(), reloadMcpStatuses()])
      setMcpSavedHint(t('settings.savedToast'))
      cancelMcpEdit()
    } catch (err) {
      addToast(`Failed to remove MCP server: ${err instanceof Error ? err.message : String(err)}`, 'error')
    } finally {
      setDeletingMcp(false)
    }
  }

  async function handleViewPluginMcp(server: McpServerConfigWire): Promise<void> {
    const pluginId = server.origin?.pluginId?.trim()
    if (!pluginId) return

    try {
      await usePluginStore.getState().selectPlugin(pluginId)
      setActiveMainView('skills')
    } catch (err) {
      addToast(`Failed to open plugin: ${err instanceof Error ? err.message : String(err)}`, 'error')
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

  async function persistBrowserUseSettings(next: {
    approvalMode?: BrowserUseApprovalMode
    blockedDomains?: string[]
    allowedDomains?: string[]
  }): Promise<void> {
    const browserUse = {
      approvalMode: next.approvalMode ?? browserUseApprovalMode,
      blockedDomains: next.blockedDomains ?? browserUseBlockedDomains,
      allowedDomains: next.allowedDomains ?? browserUseAllowedDomains
    }
    await window.api.settings.set({ browserUse })
  }

  async function handleBrowserUseApprovalModeChange(next: BrowserUseApprovalMode): Promise<void> {
    const previous = browserUseApprovalMode
    setBrowserUseApprovalMode(next)
    try {
      await persistBrowserUseSettings({ approvalMode: next })
    } catch (err) {
      setBrowserUseApprovalMode(previous)
      addToast(t('settings.saveFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }

  function openBrowserUseDomainDialog(target: 'blocked' | 'allowed'): void {
    setBrowserUseDomainTarget(target)
    setBrowserUseDomainDraft('')
    setBrowserUseDomainError('')
  }

  async function handleAddBrowserUseDomain(): Promise<void> {
    if (!browserUseDomainTarget) return
    const domain = normalizeBrowserUseDomainInput(browserUseDomainDraft)
    if (!domain) {
      setBrowserUseDomainError(t('settings.browserUse.domainInvalid'))
      return
    }
    const blocked = browserUseDomainTarget === 'blocked'
      ? Array.from(new Set([...browserUseBlockedDomains, domain]))
      : browserUseBlockedDomains.filter((item) => item !== domain)
    const allowed = browserUseDomainTarget === 'allowed'
      ? Array.from(new Set([...browserUseAllowedDomains, domain]))
      : browserUseAllowedDomains.filter((item) => item !== domain)
    setBrowserUseBlockedDomains(blocked)
    setBrowserUseAllowedDomains(allowed)
    setBrowserUseDomainTarget(null)
    setBrowserUseDomainDraft('')
    setBrowserUseDomainError('')
    try {
      await persistBrowserUseSettings({ blockedDomains: blocked, allowedDomains: allowed })
    } catch (err) {
      addToast(t('settings.saveFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }

  async function handleRemoveBrowserUseDomain(target: 'blocked' | 'allowed', domain: string): Promise<void> {
    const blocked = target === 'blocked'
      ? browserUseBlockedDomains.filter((item) => item !== domain)
      : browserUseBlockedDomains
    const allowed = target === 'allowed'
      ? browserUseAllowedDomains.filter((item) => item !== domain)
      : browserUseAllowedDomains
    setBrowserUseBlockedDomains(blocked)
    setBrowserUseAllowedDomains(allowed)
    try {
      await persistBrowserUseSettings({ blockedDomains: blocked, allowedDomains: allowed })
    } catch (err) {
      addToast(t('settings.saveFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }

  async function handleClearBrowserUseCookies(): Promise<void> {
    setClearingBrowserCookies(true)
    try {
      await window.api.workspace.viewer.browserUse.clearCookies()
      addToast(t('settings.browserUse.cookiesCleared'), 'success')
    } catch (err) {
      addToast(t('settings.browserUse.cookiesClearFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    } finally {
      setClearingBrowserCookies(false)
    }
  }

  async function handleInstallBrowserUsePlugin(): Promise<void> {
    if (!browserUsePlugin) return
    setBrowserUseInstalling(true)
    try {
      await installPlugin(browserUsePlugin.id)
      await fetchPlugins()
      await fetchSkills()
      setBrowserUseInstallOpen(false)
      addToast(t('plugins.installSuccess'), 'success')
    } catch {
      addToast(t('plugins.installFailed'), 'error')
    } finally {
      setBrowserUseInstalling(false)
    }
  }

  function handleTryBrowserUseInChat(): void {
    const prompt = browserUsePlugin?.interface?.defaultPrompt || ''
    const text = `$browser-use${prompt ? ` ${prompt}` : ''}`
    const ui = useUIStore.getState()
    const existing = ui.welcomeDraft
    ui.setWelcomeDraft({
      text,
      segments: [{ type: 'skill', skillName: 'browser-use' }],
      selectionStart: text.length,
      selectionEnd: text.length,
      images: [],
      files: [],
      mode: existing?.mode ?? 'agent',
      model: existing?.model || 'Default',
      approvalPolicy: existing?.approvalPolicy ?? 'default'
    })
    ui.goToNewChat()
  }

  function normalizePortOrDefault(raw: string, defaultPort: number): number {
    const parsed = Number.parseInt(raw.trim(), 10)
    return Number.isInteger(parsed) && parsed > 0 && parsed <= 65535 ? parsed : defaultPort
  }

  async function persistConnectionSettings(): Promise<void> {
    const normalizedPort = normalizePortOrDefault(wsPort, DEFAULT_WS_PORT)
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
      }
    })
    setSavedConnectionMode(connectionMode)
    setBaselineConnection({
      binarySource,
      binaryPath,
      connectionMode,
      wsHost,
      wsPort: String(normalizedPort),
      remoteUrl,
      remoteToken
    })
  }

  async function persistProxySettings(): Promise<void> {
    const normalizedProxyPort = normalizePortOrDefault(proxyPort, DEFAULT_PROXY_PORT)
    await window.api.settings.set({
      proxy: {
        enabled: proxyEnabled,
        port: normalizedProxyPort,
        binarySource: proxyBinarySource,
        binaryPath: proxyBinaryPath.trim() || undefined,
        authDir: proxyAuthDir.trim() || undefined
      }
    })
    setBaselineProxy({
      enabled: proxyEnabled,
      port: String(normalizedProxyPort),
      authDir: proxyAuthDir,
      binarySource: proxyBinarySource,
      binaryPath: proxyBinaryPath
    })
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

    const timeoutMessage = appendProxyOAuthDiagnostics(
      t('settings.proxy.oauthStatusTimeout'),
      provider,
      proxyAuthDir
    )
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

  async function handleApplyAndRestartAll(): Promise<void> {
    let needsAppServerRestart = connectionDirty || llmDirty || selfLearningRestartPending
    let appServerRestartAttempted = false
    let proxyRestartAttempted = false
    let proxyApplied = false
    let proxyEnabledAfterApply = proxyEnabled
    let latestCore: WorkspaceCoreConfigResult | null = null
    setSaving(true)
    setRestartingAppServer(needsAppServerRestart || proxyDirty)
    setRestartingProxy(proxyDirty)
    setApplyingLlm(llmDirty)
    try {
      if (connectionDirty) {
        await persistConnectionSettings()
      }

      const apiKey = llmApiKey.trim()
      const endPoint = llmEndPoint.trim()
      const payload: Record<string, string | null> = {}
      if (!proxyLockActive && apiKey !== (workspaceCoreBaseline.apiKey ?? '')) payload.apiKey = apiKey || null
      if (!proxyLockActive && endPoint !== (workspaceCoreBaseline.endPoint ?? '')) payload.endPoint = endPoint || null
      if (Object.keys(payload).length > 0) {
        await window.api.appServer.sendRequest('workspace/config/update', payload)
      }

      if (proxyDirty) {
        proxyApplied = true
        proxyEnabledAfterApply = proxyEnabled
        await persistProxySettings()
        proxyRestartAttempted = true
        setExpectedRestart(true)
        await window.api.proxy.restartManaged()
        const status = await window.api.proxy.getStatus()
        setProxyStatusText(status.status)
        setProxyStatusError(status.status === 'error' ? status.errorMessage ?? '' : '')

        latestCore = await readWorkspaceCoreStrict()
        // The proxy refresh path already restarts the Hub-managed AppServer with
        // the current settings, including the disabled-proxy case, so suppress a
        // second explicit AppServer restart after the proxy restart succeeds.
        needsAppServerRestart = false
      }

      if (needsAppServerRestart) {
        appServerRestartAttempted = true
        setExpectedRestart(true)
        await window.api.appServer.restartManaged()
        if (!latestCore) {
          latestCore = await readWorkspaceCoreStrict()
        }
        applyWorkspaceCoreBaseline(latestCore, false)
        setSelfLearningRestartPending(false)
        addToast(t('settings.restartAppServerSuccess'), 'success')
      } else if (proxyApplied) {
        if (latestCore) {
          applyWorkspaceCoreBaseline(latestCore, false)
        }
        setSelfLearningRestartPending(false)
        addToast(proxyEnabledAfterApply ? t('settings.proxy.restartSuccess') : t('settings.proxy.stopSuccess'), 'success')
      }
      usePendingRestartStore.getState().clear()
    } catch (err) {
      if (appServerRestartAttempted) {
        setExpectedRestart(false)
      }
      if (proxyRestartAttempted) {
        setExpectedRestart(false)
      }
      addToast(
        t(appServerRestartAttempted ? 'settings.restartAppServerFailed' : 'settings.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setRestartingAppServer(false)
      setRestartingProxy(false)
      setApplyingLlm(false)
      setSaving(false)
    }
  }

  const pendingRestartSignature = useMemo(() => {
    const parts: string[] = []
    if (connectionDirty) {
      parts.push([
        'connection',
        binarySource,
        binaryPath.trim(),
        connectionMode,
        wsHost.trim(),
        wsPort.trim(),
        remoteUrl.trim(),
        remoteToken.trim()
      ].join(':'))
    }
    if (llmDirty) {
      parts.push([
        'llm',
        apiKeyOverrideActive,
        llmApiKeyTrimmed,
        endPointOverrideActive,
        llmEndPointTrimmed
      ].join(':'))
    }
    if (proxyDirty) {
      parts.push([
        'proxy',
        proxyEnabled,
        proxyPort.trim(),
        proxyAuthDir.trim(),
        proxyBinarySource,
        proxyBinaryPath.trim()
      ].join(':'))
    }
    if (selfLearningRestartPending) {
      parts.push(`selfLearning:${selfLearningEnabled}`)
    }
    return parts.join('|')
  }, [
    apiKeyOverrideActive,
    binaryPath,
    binarySource,
    connectionDirty,
    connectionMode,
    endPointOverrideActive,
    llmApiKeyTrimmed,
    llmDirty,
    llmEndPointTrimmed,
    proxyAuthDir,
    proxyBinaryPath,
    proxyBinarySource,
    proxyDirty,
    proxyEnabled,
    proxyPort,
    remoteToken,
    remoteUrl,
    selfLearningEnabled,
    selfLearningRestartPending,
    wsHost,
    wsPort
  ])

  useEffect(() => {
    if (pendingRestartSignature) {
      usePendingRestartStore.getState().setPending(pendingRestartSignature, handleApplyAndRestartAll)
    } else {
      usePendingRestartStore.getState().clear()
    }
  })

  useEffect(() => {
    return () => {
      usePendingRestartStore.getState().clear()
    }
  }, [])

  const tabs: Array<{ id: SettingsTab; label: string; icon: LucideIcon }> = [
    { id: 'general', label: t('settings.tab.general'), icon: SettingsIcon },
    { id: 'connection', label: t('settings.tab.connection'), icon: Cable },
    { id: 'proxy', label: t('settings.tab.proxy'), icon: KeyRound },
    { id: 'browserUse', label: t('settings.tab.browserUse'), icon: Globe2 },
    { id: 'usage', label: t('settings.tab.usage'), icon: BarChart3 },
    { id: 'channels', label: t('settings.tab.channels'), icon: MessageSquare }
  ]
  if (workspaceCoreApiAvailable) {
    tabs.splice(1, 0, { id: 'personalization', label: t('settings.tab.personalization'), icon: UserRound })
  }
  if (mcpEnabled) {
    tabs.push({ id: 'mcp', label: 'MCP', icon: Server })
  }
  if (subAgentEnabled) {
    tabs.push({ id: 'subAgents', label: t('settings.tab.subAgents'), icon: Bot })
  }
  tabs.push({ id: 'archivedThreads', label: t('settings.tab.archivedThreads'), icon: Archive })

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
          const TabIcon = tab.icon
          return (
            <button
              key={tab.id}
              type="button"
              onClick={() => setActiveSettingsTab(tab.id)}
              style={{
                width: '100%',
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
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
              <TabIcon size={16} strokeWidth={1.8} aria-hidden="true" style={{ flexShrink: 0 }} />
              <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                {tab.label}
              </span>
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

        <main style={settingsMainStyle()}>
          <div style={settingsContentContainerStyle()}>
            {activeSettingsTab === 'general' && (
              <GeneralPanel>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
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

                <SettingsGroup
                  title={t('settings.group.permissions')}
                  description={t('settings.permissions.description')}
                >
                  <SettingsRow
                    label={t('settings.permissions.workspaceDefault.label')}
                    description={t('settings.permissions.workspaceDefault.description')}
                    htmlFor="settings-default-approval-policy"
                    control={
                      <select
                        id="settings-default-approval-policy"
                        value={defaultApprovalPolicy}
                        disabled={applyingDefaultApprovalPolicy}
                        aria-label={t('settings.permissions.workspaceDefault.label')}
                        onChange={(event) => {
                          const select = event.currentTarget
                          const previousPolicy = defaultApprovalPolicy
                          const nextPolicy = event.target.value as VisibleApprovalPolicy
                          void handleDefaultApprovalPolicyChange(nextPolicy).then((applied) => {
                            if (!applied) select.value = previousPolicy
                          })
                        }}
                        style={{ ...inputStyle(), width: '180px', cursor: applyingDefaultApprovalPolicy ? 'default' : 'pointer' }}
                      >
                        <option value="default">{t('settings.permissions.default.label')}</option>
                        <option value="autoApprove">{t('settings.permissions.fullAccess.label')}</option>
                      </select>
                    }
                  />
                </SettingsGroup>

                <SettingsGroup title={t('settings.llm.title')}>
                  {proxyLockActive && (
                    <SettingsRow>
                      <div
                        style={{
                          width: '100%',
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'space-between',
                          gap: '10px'
                        }}
                      >
                        <span style={{ fontSize: '12px', color: 'var(--text-dimmed)' }}>{t('settings.llm.proxyLock.banner')}</span>
                        <button
                          type="button"
                          onClick={() => setActiveSettingsTab('proxy')}
                          style={{ ...secondaryButtonStyle(false), flexShrink: 0 }}
                        >
                          {t('settings.llm.proxyLock.openProxyTab')}
                        </button>
                      </div>
                    </SettingsRow>
                  )}
                  <SettingsRow
                    label={t('settings.llm.apiKey')}
                    description={
                      showApiKeyInheritedHint ? (
                        <span>
                          {t('settings.llm.inheritingUserDefault')}
                          {' '}
                          <button
                            type="button"
                            onClick={handleActivateApiKeyOverride}
                            style={{
                              border: 'none',
                              background: 'transparent',
                              color: 'var(--link-text)',
                              cursor: 'pointer',
                              fontSize: '11px',
                              padding: 0
                            }}
                          >
                            {t('settings.llm.viewOrEdit')}
                          </button>
                        </span>
                      ) : undefined
                    }
                    controlMinWidth={280}
                    control={
                      proxyLockActive ? (
                        <input
                          type="password"
                          value={llmApiKey}
                          readOnly
                          style={{ ...inputStyle(true), opacity: 0.7 }}
                        />
                      ) : !apiKeyOverrideActive ? (
                        <input
                          type="password"
                          value=""
                          readOnly
                          style={{ ...inputStyle(true), opacity: 0.55 }}
                          placeholder={t('settings.llm.inheritingUserDefault')}
                        />
                      ) : (
                        <SecretInput
                          value={llmApiKey}
                          onChange={setLlmApiKey}
                          style={inputStyle(true)}
                          placeholder={t('settings.llm.apiKey')}
                        />
                      )
                    }
                  />
                  <SettingsRow
                    label={t('settings.llm.endPoint')}
                    description={
                      showEndPointInheritedHint ? (
                        <span>
                          {t('settings.llm.inheritingUserDefault')}
                          {' '}
                          <button
                            type="button"
                            onClick={handleActivateEndPointOverride}
                            style={{
                              border: 'none',
                              background: 'transparent',
                              color: 'var(--link-text)',
                              cursor: 'pointer',
                              fontSize: '11px',
                              padding: 0
                            }}
                          >
                            {t('settings.llm.viewOrEdit')}
                          </button>
                        </span>
                      ) : undefined
                    }
                    controlMinWidth={280}
                    control={
                      <input
                        type="url"
                        value={llmEndPoint}
                        onChange={(e) => setLlmEndPoint(e.target.value)}
                        style={{
                          ...inputStyle(true),
                          opacity: proxyLockActive || !endPointOverrideActive ? 0.55 : 1
                        }}
                        placeholder="https://api.openai.com/v1"
                        disabled={proxyLockActive || !endPointOverrideActive}
                      />
                    }
                  />
                </SettingsGroup>
              </div>
              </GeneralPanel>
            )}

            {workspaceCoreApiAvailable && activeSettingsTab === 'personalization' && (
              <GeneralPanel>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                <SettingsGroup
                  title={t('settings.group.personalization')}
                >
                  <SettingsRow
                    label={t('settings.personalization.welcomeSuggestions')}
                    description={t('settings.personalization.welcomeSuggestionsHint')}
                    control={
                      <PillSwitch
                        checked={welcomeSuggestionsEnabled}
                        disabled={applyingWelcomeSuggestions}
                        aria-label={t('settings.personalization.welcomeSuggestions')}
                        onChange={(checked) => {
                          void handleWelcomeSuggestionsToggle(checked)
                        }}
                      />
                    }
                  />
                  <SettingsRow
                    label={t('settings.personalization.selfLearning')}
                    description={t('settings.personalization.selfLearningHint')}
                    control={
                      <PillSwitch
                        checked={selfLearningEnabled}
                        disabled={applyingSelfLearning}
                        aria-label={t('settings.personalization.selfLearning')}
                        onChange={(checked) => {
                          void handleSelfLearningToggle(checked)
                        }}
                      />
                    }
                  />
                  <SettingsRow
                    label={t('settings.personalization.longTermMemory')}
                    description={t('settings.personalization.longTermMemoryHint')}
                    control={
                      <PillSwitch
                        checked={memoryAutoConsolidateEnabled}
                        disabled={applyingMemoryAutoConsolidate}
                        aria-label={t('settings.personalization.longTermMemory')}
                        onChange={(checked) => {
                          void handleMemoryAutoConsolidateToggle(checked)
                        }}
                      />
                    }
                  />
                </SettingsGroup>
              </div>
              </GeneralPanel>
            )}

            {activeSettingsTab === 'connection' && (
              <ConnectionPanel>
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
                      <option value="local">{t('settings.connectionMode.local')}</option>
                      <option value="remote">{t('settings.connectionMode.remote')}</option>
                    </select>
                  </SettingsRow>

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

                  {connectionDirty && (
                    <SettingsRow
                      description={t('settings.pendingChanges.connection')}
                      control={
                        <button
                          type="button"
                          onClick={() => {
                            if (!baselineConnection) return
                            setBinarySource(baselineConnection.binarySource)
                            setBinaryPath(baselineConnection.binaryPath)
                            setConnectionMode(baselineConnection.connectionMode)
                            setWsHost(baselineConnection.wsHost)
                            setWsPort(baselineConnection.wsPort)
                            setRemoteUrl(baselineConnection.remoteUrl)
                            setRemoteToken(baselineConnection.remoteToken)
                          }}
                          disabled={restartingAppServer || saving}
                          style={secondaryActionButtonStyle(restartingAppServer || saving)}
                        >
                          {t('settings.llm.revert')}
                        </button>
                      }
                    />
                  )}
                </SettingsGroup>
              </div>
              </ConnectionPanel>
            )}

            {activeSettingsTab === 'proxy' && (
              <ProxyPanel>
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
                        <span>{t('settings.proxy.status')}</span>
                        <ProxyRuntimeStatusPill
                          status={proxyStatusText}
                          label={t(`settings.proxy.status.${proxyStatusText}`)}
                        />
                      </span>
                    }
                    description={
                      proxyStatusError ? (
                        <div style={{ fontSize: '12px', color: 'var(--error)', marginTop: '6px' }}>
                          {proxyStatusError}
                        </div>
                      ) : undefined
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
                            ? t('settings.proxy.binaryNotFound.bundled')
                            : source === 'path'
                              ? t('settings.proxy.binaryNotFound.path')
                              : t('settings.proxy.binaryNotFound.custom')
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
                        label={(
                          <div style={{ display: 'flex', alignItems: 'center', gap: '12px', minWidth: 0 }}>
                            <ProxyProviderIcon provider={provider} />
                            <div style={{ minWidth: 0 }}>
                              <div style={{ fontSize: '13px', fontWeight: 600, color: 'var(--text-primary)' }}>
                                {t(`settings.proxy.provider.${provider}` as MessageKey)}
                              </div>
                              <div
                                style={{
                                  fontSize: '11px',
                                  color: 'var(--text-dimmed)',
                                  lineHeight: 1.5,
                                  marginTop: '4px'
                                }}
                              >
                                {t(`settings.proxy.provider.${provider}Desc` as MessageKey)}
                                {proxyProviderError[provider] && (
                                  <div style={{ fontSize: '11px', color: 'var(--error)', marginTop: '4px' }}>
                                    {proxyProviderError[provider]}
                                  </div>
                                )}
                              </div>
                            </div>
                          </div>
                        )}
                        description={
                          undefined
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
              </ProxyPanel>
            )}

            {activeSettingsTab === 'browserUse' && (
              <GeneralPanel>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                {pluginManagementEnabled && browserUsePlugin && (
                  <SettingsGroup title={t('settings.browserUse.plugin')}>
                    <SettingsRow orientation="block">
                      <PluginCatalogItem
                        plugin={browserUsePlugin}
                        tryLabel={t('plugins.tryInChat')}
                        installLabel={t('plugins.install')}
                        onTryInChat={handleTryBrowserUseInChat}
                        onInstall={() => setBrowserUseInstallOpen(true)}
                        style={{ height: 54, padding: '0 4px' }}
                      />
                    </SettingsRow>
                  </SettingsGroup>
                )}

                <SettingsGroup title={t('settings.browserUse.browsingData')}>
                  <SettingsRow
                    label={t('settings.browserUse.cookies')}
                    description={t('settings.browserUse.cookiesHint')}
                    control={
                      <button
                        type="button"
                        onClick={() => void handleClearBrowserUseCookies()}
                        disabled={clearingBrowserCookies}
                        style={secondaryActionButtonStyle(clearingBrowserCookies)}
                      >
                        {clearingBrowserCookies ? t('settings.saving') : t('settings.browserUse.clearCookies')}
                      </button>
                    }
                  />
                </SettingsGroup>

                {browserUsePluginReady && (
                  <>
                    <SettingsGroup title={t('settings.browserUse.permissions')}>
                      <SettingsRow
                        label={t('settings.browserUse.approval')}
                        description={t('settings.browserUse.approvalHint')}
                        control={
                          <select
                            value={browserUseApprovalMode}
                            onChange={(e) => void handleBrowserUseApprovalModeChange(e.target.value as BrowserUseApprovalMode)}
                            style={{ ...inputStyle(), width: '180px', cursor: 'pointer' }}
                          >
                            <option value="alwaysAsk">{t('settings.browserUse.approval.alwaysAsk')}</option>
                            <option value="askUnknown">{t('settings.browserUse.approval.askUnknown')}</option>
                            <option value="neverAsk">{t('settings.browserUse.approval.neverAsk')}</option>
                          </select>
                        }
                      />
                    </SettingsGroup>

                    <SettingsGroup
                      title={t('settings.browserUse.blockedDomains')}
                      description={t('settings.browserUse.blockedDomainsHint')}
                      headerAction={
                        <button type="button" onClick={() => openBrowserUseDomainDialog('blocked')} style={secondaryActionButtonStyle(false)}>
                          {t('settings.browserUse.add')}
                        </button>
                      }
                    >
                      {browserUseBlockedDomains.length === 0 ? (
                        <SettingsRow>
                          <div style={{ width: '100%', textAlign: 'center', fontSize: '12px', color: 'var(--text-dimmed)' }}>
                            {t('settings.browserUse.noBlockedDomains')}
                          </div>
                        </SettingsRow>
                      ) : browserUseBlockedDomains.map((domain) => (
                        <SettingsRow
                          key={domain}
                          label={domain}
                          control={
                            <button type="button" onClick={() => void handleRemoveBrowserUseDomain('blocked', domain)} style={secondaryButtonStyle(false)}>
                              {t('settings.browserUse.remove')}
                            </button>
                          }
                        />
                      ))}
                    </SettingsGroup>

                    <SettingsGroup
                      title={t('settings.browserUse.allowedDomains')}
                      description={t('settings.browserUse.allowedDomainsHint')}
                      headerAction={
                        <button type="button" onClick={() => openBrowserUseDomainDialog('allowed')} style={secondaryActionButtonStyle(false)}>
                          {t('settings.browserUse.add')}
                        </button>
                      }
                    >
                      {browserUseAllowedDomains.length === 0 ? (
                        <SettingsRow>
                          <div style={{ width: '100%', textAlign: 'center', fontSize: '12px', color: 'var(--text-dimmed)' }}>
                            {t('settings.browserUse.noAllowedDomains')}
                          </div>
                        </SettingsRow>
                      ) : browserUseAllowedDomains.map((domain) => (
                        <SettingsRow
                          key={domain}
                          label={domain}
                          control={
                            <button type="button" onClick={() => void handleRemoveBrowserUseDomain('allowed', domain)} style={secondaryButtonStyle(false)}>
                              {t('settings.browserUse.remove')}
                            </button>
                          }
                        />
                      ))}
                    </SettingsGroup>
                  </>
                )}
              </div>
              {browserUsePlugin && browserUseInstallOpen && (
                <PluginInstallDialog
                  plugin={browserUsePlugin}
                  installing={browserUseInstalling}
                  onClose={() => setBrowserUseInstallOpen(false)}
                  onInstall={() => void handleInstallBrowserUsePlugin()}
                />
              )}
              </GeneralPanel>
            )}

            {activeSettingsTab === 'usage' && (
              <UsagePanel>
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
              </UsagePanel>
            )}

            {activeSettingsTab === 'channels' && (
              <ChannelsPanel>
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
                              <ActionTooltip
                                key={ch.name}
                                label={t('settings.channelIconTitle', { name: ch.name })}
                                placement="top"
                              >
                              <button
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
                              </ActionTooltip>
                            )
                          })}
                        </div>
                      </SettingsRow>
                    )
                  })}
              </SettingsGroup>
              </ChannelsPanel>
            )}

            {activeSettingsTab === 'mcp' && (
              <McpPanel>
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
                    <SettingsPageHeader
                      title={t('settings.mcp.title')}
                      description={t('settings.mcp.description')}
                      action={
                        <button type="button" onClick={() => startMcpDraft()} style={primaryButtonStyle(false)}>
                          {t('settings.mcp.addServer')}
                        </button>
                      }
                    >
                      {mcpSavedHint && (
                        <div style={{ fontSize: '12px', color: 'var(--success)', marginTop: '6px' }}>
                          {mcpSavedHint}
                        </div>
                      )}
                    </SettingsPageHeader>

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
                        const isToggling = togglingServerName === server.name
                        const isPluginManaged = isPluginManagedMcpServer(server, mcpOriginsEnabled)
                        const transportLabel =
                          server.transport === 'stdio'
                            ? t('settings.mcp.transport.stdio')
                            : t('settings.mcp.transport.http')
                        const toolCountLabel =
                          typeof status?.toolCount === 'number'
                            ? t('settings.mcp.toolsCountSuffix', { count: status.toolCount }).replace(/^ · /, '')
                            : null
                        return (
                          <div
                            key={server.name}
                            role={isPluginManaged ? undefined : 'button'}
                            tabIndex={isPluginManaged ? undefined : 0}
                            aria-label={isPluginManaged ? `MCP server ${server.name}` : `Edit MCP server ${server.name}`}
                            onClick={isPluginManaged ? undefined : () => startMcpDraft(server)}
                            onKeyDown={(event) => {
                              if (isPluginManaged) return
                              if (event.key === 'Enter' || event.key === ' ') {
                                event.preventDefault()
                                startMcpDraft(server)
                              }
                            }}
                            style={{
                              ...cardStyle(),
                              display: 'flex',
                              alignItems: 'center',
                              justifyContent: 'space-between',
                              gap: '16px',
                              cursor: isPluginManaged ? 'default' : 'pointer',
                              textAlign: 'left',
                              opacity: isToggling ? 0.7 : 1
                            }}
                          >
                            <div style={{ flex: 1, minWidth: 0 }}>
                              <div style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)' }}>
                                {server.name}
                              </div>
                              <div
                                style={{
                                  marginTop: '4px',
                                  fontSize: '12px',
                                  color: 'var(--text-dimmed)',
                                  display: 'flex',
                                  flexWrap: 'wrap',
                                  alignItems: 'center',
                                  gap: '6px'
                                }}
                              >
                                <span>{transportLabel}</span>
                                {isPluginManaged && (
                                  <>
                                    <span aria-hidden>·</span>
                                    <span style={mcpSourcePillStyle()}>
                                      {mcpPluginSourceLabel(server, t)}
                                    </span>
                                  </>
                                )}
                                <span aria-hidden>·</span>
                                <span style={{ color: tone.color, fontWeight: 500 }}>{tone.label}</span>
                                {toolCountLabel && (
                                  <>
                                    <span aria-hidden>·</span>
                                    <span>{toolCountLabel}</span>
                                  </>
                                )}
                              </div>
                              {status?.lastError && (
                                <div style={{ fontSize: '12px', color: '#f85149', marginTop: '8px' }}>
                                  {status.lastError}
                                </div>
                              )}
                            </div>
                            {isPluginManaged ? (
                              <button
                                type="button"
                                onClick={(event) => {
                                  event.stopPropagation()
                                  void handleViewPluginMcp(server)
                                }}
                                style={secondaryButtonStyle(false)}
                              >
                                {t('settings.mcp.viewPlugin')}
                              </button>
                            ) : (
                              <span
                                onClick={(event) => event.stopPropagation()}
                                onKeyDown={(event) => event.stopPropagation()}
                                style={{ flexShrink: 0, display: 'inline-flex' }}
                              >
                                <PillSwitch
                                  checked={server.enabled}
                                  disabled={isToggling}
                                  onChange={(checked) => {
                                    void handleMcpQuickToggle(server, checked)
                                  }}
                                  aria-label={`Toggle MCP server ${server.name}`}
                                />
                              </span>
                            )}
                          </div>
                        )
                      })}
                  </>
                )}

                {mcpEnabled && editingServerName !== null && (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                    <SettingsPageHeader
                      title={
                        editingServerName === '__new__'
                          ? t('settings.mcp.addTitle')
                          : t('settings.mcp.editTitle')
                      }
                      description={t('settings.mcp.editIntro')}
                      action={
                        <button type="button" onClick={cancelMcpEdit} style={secondaryButtonStyle(false)}>
                          {t('settings.mcp.back')}
                        </button>
                      }
                    />

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
                    </div>

                    <div style={cardStyle()}>
                      <ToggleSwitch
                        checked={mcpDraft.enabled}
                        onChange={(checked) =>
                          setMcpDraft((prev) => ({
                            ...prev,
                            enabled: checked
                          }))
                        }
                        label={t('settings.mcp.field.enabled')}
                        description={t('settings.mcp.field.enabledDescription')}
                      />
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
              </McpPanel>
            )}

            {activeSettingsTab === 'archivedThreads' && (
              <ArchivedThreadsSettingsView
                workspacePath={workspacePath}
                onThreadListRefreshRequested={onThreadListRefreshRequested}
              />
            )}

            {activeSettingsTab === 'subAgents' && (
              <SubAgentsPanel
                enabled={subAgentEnabled}
                refreshTick={subAgentRefreshTick}
              />
            )}
          </div>
        </main>

      </div>
      {browserUseDomainTarget && (
        <div
          role="dialog"
          aria-modal="true"
          style={{
            position: 'fixed',
            inset: 0,
            zIndex: 10000,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            background: 'var(--overlay-scrim)'
          }}
        >
          <div
            style={{
              width: '420px',
              maxWidth: 'calc(100vw - 48px)',
              border: '1px solid var(--border-default)',
              borderRadius: '12px',
              background: 'var(--bg-secondary)',
              boxShadow: 'var(--shadow-level-3)',
              padding: '22px'
            }}
          >
            <h2 style={{ margin: 0, fontSize: '16px', fontWeight: 700, color: 'var(--text-primary)' }}>
              {browserUseDomainTarget === 'blocked'
                ? t('settings.browserUse.addBlockedDomain')
                : t('settings.browserUse.addAllowedDomain')}
            </h2>
            <p style={{ margin: '8px 0 14px', fontSize: '13px', lineHeight: 1.5, color: 'var(--text-secondary)' }}>
              {browserUseDomainTarget === 'blocked'
                ? t('settings.browserUse.addBlockedDomainHint')
                : t('settings.browserUse.addAllowedDomainHint')}
            </p>
            <input
              type="text"
              value={browserUseDomainDraft}
              onChange={(e) => {
                setBrowserUseDomainDraft(e.target.value)
                setBrowserUseDomainError('')
              }}
              onKeyDown={(e) => {
                if (e.key === 'Enter') void handleAddBrowserUseDomain()
                if (e.key === 'Escape') setBrowserUseDomainTarget(null)
              }}
              placeholder="example.com"
              autoFocus
              style={inputStyle(true)}
            />
            {browserUseDomainError && (
              <div style={{ marginTop: '8px', fontSize: '12px', color: 'var(--error)' }}>
                {browserUseDomainError}
              </div>
            )}
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px', marginTop: '18px' }}>
              <button type="button" onClick={() => setBrowserUseDomainTarget(null)} style={secondaryButtonStyle(false)}>
                {t('settings.browserUse.cancel')}
              </button>
              <button type="button" onClick={() => void handleAddBrowserUseDomain()} style={primaryButtonStyle(false)}>
                {t('settings.browserUse.add')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
