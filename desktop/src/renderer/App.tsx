import { useCallback, useEffect, useRef, useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { translate, type AppLocale } from '../shared/locales'
import { useLocale } from './contexts/LocaleContext'
import { basename } from './utils/path'
import { initConnectionStore, useConnectionStore } from './stores/connectionStore'
import { useThreadStore, type ThreadRuntimeSnapshot } from './stores/threadStore'
import {
  selectLatestCreatePlanTurnId,
  selectStreamingPlanItemId,
  useConversationStore
} from './stores/conversationStore'
import { useUIStore } from './stores/uiStore'
import { useViewerTabStore } from './stores/viewerTabStore'
import { QuickOpenDialog } from './components/detail/QuickOpenDialog'
import { ThreePanel } from './components/layout/ThreePanel'
import { PluginsView } from './components/plugins/PluginsView'
import { AutomationsView } from './components/automations/AutomationsView'
import { useAutomationsStore } from './stores/automationsStore'
import { useCronStore, type CronJobWire } from './stores/cronStore'
import { useReviewPanelStore } from './stores/reviewPanelStore'
import type { AutomationTask } from './stores/automationsStore'
import { useModelCatalogStore } from './stores/modelCatalogStore'
import { useMcpStore, type McpServerStatusWire } from './stores/mcpStore'
import { useSkillsStore } from './stores/skillsStore'
import { usePluginStore } from './stores/pluginStore'
import { usePendingRestartStore } from './stores/pendingRestartStore'
import { CustomMenuBar } from './components/layout/CustomMenuBar'
import { Sidebar } from './components/layout/Sidebar'
import { ConversationPanel } from './components/layout/ConversationPanel'
import { DetailPanel } from './components/layout/DetailPanel'
import { ErrorScreen } from './components/ErrorScreen'
import { WelcomeScreen } from './components/WelcomeScreen'
import { WorkspaceSetupInterstitial } from './components/WorkspaceSetupInterstitial'
import { WorkspaceSetupWizard } from './components/WorkspaceSetupWizard'
import { ConfirmDialogHost } from './components/ui/ConfirmDialog'
import { ToastContainer } from './components/ui/ToastContainer'
import { SettingsView } from './components/settings/SettingsView'
import { ChannelsView } from './components/channels/ChannelsView'
import { addJobResultToast, addToast } from './stores/toastStore'
import type { ContextUsageSnapshotWire, SessionIdentity, Thread, ThreadSummary } from './types/thread'
import { wireTurnToConversationTurn } from './types/conversation'
import type { ConversationItem, ConversationTurn, QueuedTurnInput } from './types/conversation'
import type { SubAgentEntry } from './types/toolCall'
import { applyTheme, resolveTheme } from './utils/theme'
import { ensureVisibleChannelsSeeded } from './utils/visibleChannelsDefaults'
import { buildComposerInputParts } from './utils/composeInputParts'
import { getFallbackThreadName } from './utils/threadFallbackName'
import { handleBrowserEvent } from './utils/browserEventHandler'
import { handleBrowserUseOpen } from './utils/browserUseOpenHandler'
import { isFatalConnectionError, useSlowConnectingHint } from './utils/connectionUi'
import {
  resolveWorkspaceConfigChangedPayload,
  type WorkspaceConfigChangedPayload
} from './utils/workspaceConfigChanged'
import type {
  BrowserUseApprovalRequestPayload,
  BrowserUseApprovalResponseAction,
  DiscoveredModule,
  ModuleStatusMap,
  WorkspaceStatusPayload
} from '../preload/api.d'
import './styles/tokens.css'

function BrowserUseApprovalDialog({
  locale,
  request,
  onRespond
}: {
  locale: AppLocale
  request: BrowserUseApprovalRequestPayload
  onRespond: (action: BrowserUseApprovalResponseAction) => void
}): JSX.Element {
  const session = request.sessionName?.trim()
  const message = session
    ? translate(locale, 'browserUse.approval.messageWithSession', { session, domain: request.domain })
    : translate(locale, 'browserUse.approval.message', { domain: request.domain })
  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="browser-use-approval-title"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 10000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'var(--overlay-scrim)'
      }}
    >
      <div
        style={{
          width: '460px',
          maxWidth: 'calc(100vw - 48px)',
          border: '1px solid var(--border-default)',
          borderRadius: '12px',
          background: 'var(--bg-secondary)',
          boxShadow: 'var(--shadow-level-3)',
          padding: '22px'
        }}
      >
        <h2
          id="browser-use-approval-title"
          style={{ margin: 0, fontSize: '16px', fontWeight: 700, color: 'var(--text-primary)' }}
        >
          {translate(locale, 'browserUse.approval.title')}
        </h2>
        <p style={{ margin: '8px 0 14px', fontSize: '13px', lineHeight: 1.5, color: 'var(--text-secondary)' }}>
          {message}
        </p>
        <div
          style={{
            border: '1px solid var(--border-default)',
            borderRadius: '8px',
            background: 'var(--bg-primary)',
            padding: '10px',
            fontFamily: 'var(--font-mono)',
            fontSize: '12px',
            color: 'var(--text-secondary)',
            overflowWrap: 'anywhere'
          }}
        >
          {request.url}
        </div>
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px', marginTop: '18px', flexWrap: 'wrap' }}>
          <button type="button" onClick={() => onRespond('deny')} style={dialogSecondaryButtonStyle()}>
            {translate(locale, 'browserUse.approval.cancel')}
          </button>
          <button type="button" onClick={() => onRespond('blockDomain')} style={dialogSecondaryButtonStyle(true)}>
            {translate(locale, 'browserUse.approval.blockDomain')}
          </button>
          <button type="button" onClick={() => onRespond('allowOnce')} style={dialogSecondaryButtonStyle()}>
            {translate(locale, 'browserUse.approval.allowOnce')}
          </button>
          <button type="button" onClick={() => onRespond('allowDomain')} style={dialogPrimaryButtonStyle()}>
            {translate(locale, 'browserUse.approval.alwaysAllow')}
          </button>
        </div>
      </div>
    </div>
  )
}

function dialogSecondaryButtonStyle(danger = false): CSSProperties {
  return {
    padding: '8px 12px',
    border: '1px solid var(--border-default)',
    borderRadius: '8px',
    background: 'transparent',
    color: danger ? 'var(--error)' : 'var(--text-primary)',
    fontSize: '13px',
    fontWeight: 600,
    cursor: 'pointer'
  }
}

function dialogPrimaryButtonStyle(): CSSProperties {
  return {
    padding: '8px 12px',
    border: 'none',
    borderRadius: '8px',
    background: 'var(--accent)',
    color: 'var(--on-accent)',
    fontSize: '13px',
    fontWeight: 700,
    cursor: 'pointer'
  }
}

function topBannerSecondaryButtonStyle(disabled = false): CSSProperties {
  return {
    padding: '6px 10px',
    border: '1px solid var(--border-default)',
    borderRadius: '8px',
    background: 'transparent',
    color: 'var(--text-primary)',
    fontSize: '12px',
    fontWeight: 600,
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.6 : 1
  }
}

function topBannerPrimaryButtonStyle(disabled = false): CSSProperties {
  return {
    padding: '6px 10px',
    border: '1px solid transparent',
    borderRadius: '8px',
    background: 'var(--accent)',
    color: 'var(--on-accent)',
    fontSize: '12px',
    fontWeight: 700,
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.6 : 1
  }
}

function AppChrome({ children }: { children: ReactNode }): JSX.Element {
  const showCustomMenu = window.api.platform !== 'darwin'
  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        width: '100%',
        overflow: 'hidden'
      }}
    >
      {showCustomMenu && <CustomMenuBar />}
      <div
        style={{
          flex: 1,
          minHeight: 0,
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column'
        }}
      >
        {children}
      </div>
    </div>
  )
}

/**
 * Root application component.
 * - Initializes connection store and thread store
 * - Loads thread list when connected
 * - Wires thread/started + thread/statusChanged notifications
 * - Registers global shortcuts for navigation and thread actions
 * - Spec §9, §12
 */
export function App(): JSX.Element {
  const locale = useLocale()
  const localeRef = useRef(locale)
  localeRef.current = locale

  const [workspacePath, setWorkspacePath] = useState('')
  const [workspaceName, setWorkspaceName] = useState('DotCraft')
  const [workspaceConfigChange, setWorkspaceConfigChange] = useState<WorkspaceConfigChangedPayload | null>(null)
  const [workspaceConfigChangeSeq, setWorkspaceConfigChangeSeq] = useState(0)
  const [workspaceStatus, setWorkspaceStatus] = useState<WorkspaceStatusPayload>({
    status: 'no-workspace',
    workspacePath: '',
    hasUserConfig: false
  })
  const [showSetupWizard, setShowSetupWizard] = useState(false)
  const { status, errorType, errorMessage } = useConnectionStore()
  const isExpectedRestart = useConnectionStore((s) => s.isExpectedRestart)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const showSlowConnectingHint = useSlowConnectingHint(status, workspacePath)
  const [browserUseApprovalRequests, setBrowserUseApprovalRequests] = useState<BrowserUseApprovalRequestPayload[]>([])
  const activeMainView = useUIStore((s) => s.activeMainView)
  const activeDetailTab = useUIStore((s) => s.activeDetailTab)
  const detailPanelVisible = useUIStore((s) => s.detailPanelVisible)
  const quickOpenVisible = useUIStore((s) => s.quickOpenVisible)
  const setQuickOpenVisible = useUIStore((s) => s.setQuickOpenVisible)
  const pendingRestartVisible = usePendingRestartStore((s) => s.visible)
  const pendingRestartApplying = usePendingRestartStore((s) => s.applying)
  const applyPendingRestart = usePendingRestartStore((s) => s.apply)
  const ignorePendingRestart = usePendingRestartStore((s) => s.ignore)
  const {
    setThreadList,
    setLoading
  } = useThreadStore()

  const workspacePathRef = useRef('')
  const workspaceConfigChangedDedupeRef = useRef<Map<string, number>>(new Map())
  const moduleConnectedSnapshotRef = useRef<Map<string, boolean>>(new Map())
  const moduleConnectedSnapshotReadyRef = useRef(false)
  const moduleDisplayNameByIdRef = useRef<Map<string, string>>(new Map())

  const reloadThreadList = useCallback(async () => {
    const path = workspacePathRef.current
    const identity: SessionIdentity = {
      channelName: 'dotcraft-desktop',
      userId: 'local',
      channelContext: `workspace:${path}`,
      workspacePath: path
    }
    setLoading(true)
    try {
      const settings = await window.api.settings.get()
      const crossChannelOrigins = await ensureVisibleChannelsSeeded(settings)
      const params = { identity, crossChannelOrigins }
      const result = await window.api.appServer.sendRequest('thread/list', params)
      const res = result as { data: ThreadSummary[] }
      setThreadList(res.data ?? [])
    } catch (err: unknown) {
      console.error('Failed to load thread list:', err)
      setThreadList([])
    } finally {
      setLoading(false)
    }
  }, [setThreadList, setLoading])

  // -------------------------------------------------------------------------
  // Bootstrap: workspace path + connection store
  // -------------------------------------------------------------------------
  const syncWorkspaceStatus = useCallback((payload: WorkspaceStatusPayload): void => {
    const path = payload.workspacePath ?? ''
    workspacePathRef.current = path
    setWorkspacePath(path)
    setWorkspaceStatus(payload)
    setWorkspaceName(path ? basename(path) : 'DotCraft')
  }, [])

  useEffect(() => {
    performance.mark('app:bootstrap-start')
    const unsubscribe = initConnectionStore()
    const unsubscribeWorkspace = window.api.workspace.onStatusChange((payload) => {
      syncWorkspaceStatus(payload)
    })

    void window.api.workspace.getStatus()
      .then((payload) => {
        syncWorkspaceStatus(payload)
      })
      .catch(() => {})

    return () => {
      unsubscribeWorkspace()
      unsubscribe()
    }
  }, [syncWorkspaceStatus])

  useEffect(() => {
    if (workspacePath) {
      window.api.window.setTitle(
        translate(locale, 'app.titleWithWorkspace', { name: workspaceName })
      )
    }
  }, [workspacePath, workspaceName, locale])

  useEffect(() => {
    window.api.settings
      .get()
      .then((s) => {
        applyTheme(resolveTheme(s.theme))
      })
      .catch(() => {})
  }, [])

  // Keep conversation store workspace path in sync (cumulative diff IPC reads)
  useEffect(() => {
    if (workspacePath) {
      useConversationStore.getState().setWorkspacePath(workspacePath)
    }
  }, [workspacePath])

  // Notify viewerTabStore when workspace changes so all viewer tabs are cleared.
  useEffect(() => {
    useViewerTabStore.getState().onWorkspaceSwitched(workspacePath, {
      onBrowserTabRemoved: (tab) => {
        void window.api.workspace.viewer.browser.destroy({ tabId: tab.id })
      },
      onTerminalTabRemoved: (tab) => {
        void window.api.workspace.viewer.terminal.dispose({ tabId: tab.id })
      }
    })
    useUIStore.getState().resetAutoShowReasons()
  }, [workspacePath])

  useEffect(() => {
    moduleConnectedSnapshotRef.current = new Map()
    moduleConnectedSnapshotReadyRef.current = false
    moduleDisplayNameByIdRef.current = new Map()

    if (!workspacePath) return

    let disposed = false

    const toConnectedSnapshot = (statusMap: ModuleStatusMap): Map<string, boolean> => {
      const snapshot = new Map<string, boolean>()
      for (const [moduleId, entry] of Object.entries(statusMap)) {
        snapshot.set(moduleId, entry?.connected === true)
      }
      return snapshot
    }

    const hydrateModuleMetadata = async (): Promise<void> => {
      try {
        const [modules, statusMap] = await Promise.all([
          window.api.modules.list(),
          window.api.modules.running()
        ])
        if (disposed) return
        moduleDisplayNameByIdRef.current = new Map(
          (modules as DiscoveredModule[]).map((module) => [module.moduleId, module.displayName])
        )
        moduleConnectedSnapshotRef.current = toConnectedSnapshot(statusMap)
        moduleConnectedSnapshotReadyRef.current = true
      } catch {
        if (disposed) return
        moduleConnectedSnapshotRef.current = new Map()
        moduleConnectedSnapshotReadyRef.current = true
      }
    }

    void hydrateModuleMetadata()

    const unsubscribe = window.api.modules.onStatusChanged((statusMap) => {
      if (disposed) return
      const nextSnapshot = toConnectedSnapshot(statusMap)
      if (!moduleConnectedSnapshotReadyRef.current) {
        moduleConnectedSnapshotRef.current = nextSnapshot
        moduleConnectedSnapshotReadyRef.current = true
        return
      }

      const previousSnapshot = moduleConnectedSnapshotRef.current
      for (const [moduleId, connected] of nextSnapshot) {
        const wasConnected = previousSnapshot.get(moduleId) === true
        if (!wasConnected && connected) {
          const displayName = moduleDisplayNameByIdRef.current.get(moduleId) ?? moduleId
          addToast(
            translate(localeRef.current, 'channels.modules.connectedToast', { name: displayName }),
            'success'
          )
        }
      }
      moduleConnectedSnapshotRef.current = nextSnapshot
    })

    return () => {
      disposed = true
      unsubscribe()
    }
  }, [workspacePath])

  useEffect(() => {
    if (workspaceStatus.status !== 'needs-setup') {
      setShowSetupWizard(false)
    }
  }, [workspaceStatus.status])

  // -------------------------------------------------------------------------
  // Load thread list when connection becomes "connected"
  // -------------------------------------------------------------------------
  const prevStatusRef = useRef<string>('')
  useEffect(() => {
    if (status === 'connected' && prevStatusRef.current !== 'connected') {
      performance.mark('app:connected')
      performance.measure('app:startup', 'app:bootstrap-start', 'app:connected')
      void reloadThreadList()

      const caps = useConnectionStore.getState().capabilities
      if (caps?.automations) {
        void useAutomationsStore.getState().fetchTasks()
      }
      if (caps?.cronManagement) {
        void useCronStore.getState().fetchJobs()
      }
      if (caps?.modelCatalogManagement) {
        void useModelCatalogStore.getState().loadIfNeeded(true)
      }
      const hasTasks = caps?.automations === true
      const hasCron = caps?.cronManagement === true
      if (hasCron && !hasTasks) {
        useUIStore.getState().setAutomationsTab('cron')
      } else {
        useUIStore.getState().setAutomationsTab('tasks')
      }
    }
    // Reset all stores when disconnecting (e.g. workspace switch)
    if (status === 'disconnected' || status === 'error') {
      useThreadStore.getState().reset()
      useConversationStore.getState().reset()
      useModelCatalogStore.getState().reset()
      useMcpStore.getState().reset()
      useCronStore.getState().reset()
      useAutomationsStore.getState().selectTask(null)
      useUIStore.getState().setAutomationsTab('tasks')
      useUIStore.getState().setActiveDetailTab('changes', { reveal: false })
      useUIStore.getState().setActiveMainView('conversation')
      useUIStore.getState().setPendingWelcomeTurn(null)
    }

    prevStatusRef.current = status
  }, [status, reloadThreadList])

  useEffect(() => {
    if (status === 'connected' && capabilities?.modelCatalogManagement === true) {
      void useModelCatalogStore.getState().loadIfNeeded()
      return
    }
    if (status === 'disconnected' || status === 'error') {
      useModelCatalogStore.getState().reset()
    }
  }, [capabilities?.modelCatalogManagement, status])

  // -------------------------------------------------------------------------
  // Wire protocol notifications
  // -------------------------------------------------------------------------
  useEffect(() => {
    // Use empty deps so this effect runs exactly once (on mount) and is cleaned
    // up on unmount. Store actions are accessed via .getState() to avoid closure
    // stale-reference issues and to prevent re-subscription on state changes.
    const unsubscribe = window.api.appServer.onNotification(
      (payload: { method: string; params: unknown }) => {
        const method = payload.method
        const p = (payload.params ?? {}) as Record<string, unknown>
        const conv = useConversationStore.getState()
        const { addThread: doAddThread, updateThreadStatus: doUpdateStatus } =
          useThreadStore.getState()
        const shouldUpdateActiveConversation = (threadId: string | null | undefined): boolean => {
          if (!threadId) return true
          return useThreadStore.getState().activeThreadId === threadId
        }
        const shouldUpdateReviewThread = (threadId: string | null | undefined): boolean => {
          if (!threadId) return false
          return useReviewPanelStore.getState().reviewThreadId === threadId
        }

        switch (method) {
          // ── Thread lifecycle ──────────────────────────────────────────
          case 'thread/started': {
            const pp = p as { thread: ThreadSummary }
            doAddThread(pp.thread)
            break
          }

          case 'thread/renamed': {
            const pp = p as { threadId: string; displayName: string }
            if (pp.displayName?.trim()) {
              useThreadStore.getState().renameThread(pp.threadId, pp.displayName)
            }
            break
          }

          case 'thread/deleted': {
            const pp = p as { threadId: string }
            useThreadStore.getState().removeThread(pp.threadId)
            break
          }

          case 'thread/statusChanged': {
            const pp = p as { threadId: string; newStatus: string }
            doUpdateStatus(pp.threadId, pp.newStatus as 'active' | 'paused' | 'archived')
            break
          }

          case 'thread/queue/updated': {
            const pp = p as { threadId?: string; queuedInputs?: unknown[] }
            if (shouldUpdateActiveConversation(pp.threadId)) {
              useConversationStore.getState().setQueuedInputs((pp.queuedInputs ?? []) as QueuedTurnInput[])
            }
            break
          }

          case 'thread/runtimeChanged': {
            const pp = p as {
              threadId?: string
              runtime?: Partial<ThreadRuntimeSnapshot>
            }
            const threadId = typeof pp.threadId === 'string' ? pp.threadId : ''
            if (!threadId) break

            const threadStore = useThreadStore.getState()
            const threadSummary = threadStore.threadList.find((thread) => thread.id === threadId)
            threadStore.applyRuntimeSnapshot(threadId, {
              running: pp.runtime?.running === true,
              waitingOnApproval: pp.runtime?.waitingOnApproval === true,
              waitingOnPlanConfirmation: pp.runtime?.waitingOnPlanConfirmation === true
            }, {
              isActive: threadStore.activeThreadId === threadId,
              isDesktopOrigin: threadSummary?.originChannel?.toLowerCase() === 'dotcraft-desktop'
            })
            break
          }

          case 'thread/error': {
            const tid = (p.threadId as string | undefined) ?? ''
            const reason = (p.reason as string | undefined) ?? (p.message as string | undefined) ?? 'unknown'
            if (reason === 'not-found' || reason.includes('not found')) {
              useThreadStore.getState().removeThread(tid)
              addToast(translate(localeRef.current, 'toast.threadNotFound'), 'warning')
            }
            break
          }

          case 'thread/archived': {
            const pp = p as { threadId: string }
            doUpdateStatus(pp.threadId, 'archived')
            const activeId = useThreadStore.getState().activeThreadId
            if (activeId === pp.threadId) {
              addToast(translate(localeRef.current, 'toast.threadArchived'), 'info')
            }
            break
          }

          // ── Turn lifecycle ────────────────────────────────────────────
          case 'turn/started': {
            const rawTurn = (p.turn ?? p) as Record<string, unknown>
            const startedThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
            if (shouldUpdateActiveConversation(startedThreadId)) {
              conv.onTurnStarted(rawTurn)
            }
            if (shouldUpdateReviewThread(startedThreadId)) {
              const rs = useReviewPanelStore.getState()
              rs.onTurnStarted(rawTurn)
            }
            break
          }

          case 'turn/completed': {
            const rawTurn = (p.turn ?? p) as Record<string, unknown>
            const completedThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
            if (shouldUpdateActiveConversation(completedThreadId)) {
              conv.onTurnCompleted(rawTurn)
            }
            // Fallback: poll thread/read if sidebar still has no displayName (e.g. missed thread/renamed).
            // Primary updates come from thread/renamed broadcast and thread/read on selection.
            if (completedThreadId) {
              const ts = useThreadStore.getState()
              const threadEntry = ts.threadList.find((t) => t.id === completedThreadId)
              if (!threadEntry?.displayName) {
                void window.api.appServer
                  .sendRequest('thread/read', { threadId: completedThreadId })
                  .then((res) => {
                    const r = res as { thread?: { displayName?: string | null } }
                    const name = r?.thread?.displayName
                    if (name) useThreadStore.getState().renameThread(completedThreadId, name)
                  })
                  .catch(() => { /* non-critical — ignore */ })
              }
            }
            if (shouldUpdateReviewThread(completedThreadId)) {
              const rs = useReviewPanelStore.getState()
              rs.onTurnCompleted(rawTurn)
            }
            break
          }

          case 'turn/failed': {
            const rawTurn = (p.turn ?? p) as Record<string, unknown>
            const failedThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
            const error = (p.error as string) ?? (p.message as string) ?? 'Unknown error'
            const errorCode = (p.code as number | undefined)
              ?? ((p.error as Record<string, unknown> | undefined)?.code as number | undefined)
            // -32020 = approval timeout — update the pending approval card
            if (shouldUpdateActiveConversation(failedThreadId)) {
              if (errorCode === -32020 || error.includes('-32020')) {
                conv.onApprovalTimeout()
              }
              conv.onTurnFailed(rawTurn, error)
            }
            if (shouldUpdateReviewThread(failedThreadId)) {
              const rs = useReviewPanelStore.getState()
              rs.onTurnFailed(rawTurn, error)
            }
            break
          }

          case 'turn/cancelled': {
            const rawTurn = (p.turn ?? p) as Record<string, unknown>
            const cancelledThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
            const reason = (p.reason as string) ?? ''
            if (shouldUpdateActiveConversation(cancelledThreadId)) {
              conv.onTurnCancelled(rawTurn, reason)
            }
            if (shouldUpdateReviewThread(cancelledThreadId)) {
              const rs = useReviewPanelStore.getState()
              rs.onTurnCancelled(rawTurn, reason)
            }
            break
          }

          // ── Item lifecycle ────────────────────────────────────────────
          case 'item/started': {
            const tid = (p.threadId as string | undefined) ?? ''
            if (shouldUpdateActiveConversation(tid)) {
              conv.onItemStarted(p)
            }
            if (shouldUpdateReviewThread(tid)) {
              const rs = useReviewPanelStore.getState()
              rs.onItemStarted(p)
            }
            break
          }

          case 'item/agentMessage/delta': {
            const tid = (p.threadId as string | undefined) ?? ''
            const delta = (p.delta as string) ?? ''
            if (shouldUpdateActiveConversation(tid)) {
              conv.onAgentMessageDelta(delta)
            }
            if (shouldUpdateReviewThread(tid)) {
              const rs = useReviewPanelStore.getState()
              rs.onAgentMessageDelta(delta)
            }
            break
          }

          case 'item/reasoning/delta': {
            const tid = (p.threadId as string | undefined) ?? ''
            const delta = (p.delta as string) ?? ''
            if (shouldUpdateActiveConversation(tid)) {
              conv.onReasoningDelta(delta)
            }
            if (shouldUpdateReviewThread(tid)) {
              const rs = useReviewPanelStore.getState()
              rs.onReasoningDelta(delta)
            }
            break
          }

          case 'item/commandExecution/outputDelta': {
            const tid = (p.threadId as string | undefined) ?? ''
            const params = {
              threadId: (p.threadId as string | undefined),
              turnId: (p.turnId as string | undefined),
              itemId: (p.itemId as string | undefined),
              delta: (p.delta as string | undefined)
            }
            if (shouldUpdateActiveConversation(tid)) {
              conv.onCommandExecutionDelta(params)
            }
            if (shouldUpdateReviewThread(tid)) {
              const rs = useReviewPanelStore.getState()
              rs.onCommandExecutionDelta(params)
            }
            break
          }

          case 'item/toolCall/argumentsDelta': {
            const tid = (p.threadId as string | undefined) ?? ''
            if (shouldUpdateActiveConversation(tid)) {
              conv.onToolCallArgumentsDelta({
                threadId: (p.threadId as string | undefined),
                turnId: (p.turnId as string | undefined),
                itemId: (p.itemId as string | undefined),
                toolName: (p.toolName as string | undefined),
                callId: (p.callId as string | undefined),
                delta: (p.delta as string | undefined)
              })
            }
            break
          }

          case 'item/completed': {
            const tid = (p.threadId as string | undefined) ?? ''
            if (shouldUpdateActiveConversation(tid)) {
              conv.onItemCompleted(p)
            }
            if (shouldUpdateReviewThread(tid)) {
              const rs = useReviewPanelStore.getState()
              rs.onItemCompleted(p)
            }
            break
          }

          case 'item/usage/delta': {
            const tid = (p.threadId as string | undefined) ?? ''
            if (!shouldUpdateActiveConversation(tid)) break
            const input = (p.inputTokens as number) ?? 0
            const output = (p.outputTokens as number) ?? 0
            const totalInput = typeof p.totalInputTokens === 'number' ? (p.totalInputTokens as number) : null
            const totalOutput = typeof p.totalOutputTokens === 'number' ? (p.totalOutputTokens as number) : null
            const contextUsage = typeof p.contextUsage === 'object' && p.contextUsage !== null
              ? p.contextUsage as ContextUsageSnapshotWire
              : null
            conv.onUsageDelta(input, output, totalInput, totalOutput, contextUsage)
            break
          }

          // ── SubAgent progress ─────────────────────────────────────────
          case 'subagent/progress': {
            const entries = (p.entries as SubAgentEntry[]) ?? []
            const threadId = (p.threadId as string | undefined) ?? ''
            if (shouldUpdateReviewThread(threadId)) {
              useReviewPanelStore.getState().onSubagentProgress(entries)
            }
            if (shouldUpdateActiveConversation(threadId)) {
              conv.onSubagentProgress(entries)
            }
            break
          }

          // ── System events ─────────────────────────────────────────────
          case 'system/event': {
            const tid = (p.threadId as string | undefined) ?? ''
            if (!shouldUpdateActiveConversation(tid)) break
            const kind = (p.kind as string) ?? ''
            conv.onSystemEvent(kind, {
              tokenCount: typeof p.tokenCount === 'number' ? (p.tokenCount as number) : null,
              percentLeft: typeof p.percentLeft === 'number' ? (p.percentLeft as number) : null
            })
            if (kind === 'consolidationFailed') {
              addToast(
                (p.message as string | undefined) ?? translate(localeRef.current, 'systemNotice.consolidationFailed.message'),
                'warning'
              )
            }
            break
          }

          // ── Plan updates ──────────────────────────────────────────────
          case 'plan/updated': {
            conv.onPlanUpdated(p as Record<string, unknown>)
            // Auto-show detail panel on Plan tab
            useUIStore.getState().setActiveDetailTab('plan')
            break
          }

          // ── Approval resolved ──────────────────────────────────────────
          case 'item/approval/resolved':
            conv.onApprovalResolved()
            break

          // ── Job results ───────────────────────────────────────────────
          case 'system/jobResult': {
            const jobName = (p.jobName as string) ?? (p.name as string) ?? 'Job'
            const resultText = (p.result as string) ?? (p.text as string) ?? ''
            const errText = (p.error as string) ?? ''
            const usage = p.tokenUsage as { input?: number; output?: number } | undefined
            let md = `**${jobName}**`
            if (errText) {
              md += `\n\n**Error**\n\n${errText}`
            } else if (resultText) {
              md += `\n\n${resultText}`
            } else {
              md += `\n\n_Completed._`
            }
            if (usage != null && ((usage.input ?? 0) > 0 || (usage.output ?? 0) > 0)) {
              md += `\n\n_Tokens: ${usage.input ?? 0} in · ${usage.output ?? 0} out_`
            }
            const tid = p.threadId as string | undefined
            if (tid) {
              md += `\n\n_Thread:_ \`${tid}\``
            }
            addJobResultToast(md, true)
            break
          }

          case 'cron/stateChanged': {
            const removed = p.removed === true
            const job = p.job as CronJobWire | undefined
            if (removed && job?.id) {
              useCronStore.getState().removeJobLocal(job.id)
              if (useCronStore.getState().selectedCronJobId === job.id) {
                useCronStore.getState().selectCronJob(null)
              }
            } else if (job) {
              useCronStore.getState().upsertJob(job)
            }
            break
          }

          // ── Automation task updates ────────────────────────────────────
          case 'automation/task/updated': {
            const task = (p.task ?? {}) as AutomationTask
            useAutomationsStore.getState().upsertTask(task)
            {
              const rs = useReviewPanelStore.getState()
              if (rs.openedTaskId === task.id && rs.taskDetail) {
                useReviewPanelStore.setState({
                  taskDetail: { ...rs.taskDetail, ...task }
                })
              }
            }
            break
          }

          case 'mcp/status/updated': {
            const server = (p.server ?? null) as McpServerStatusWire | null
            if (server?.name) {
              useMcpStore.getState().upsertStatus(server)
            }
            break
          }

          case 'workspace/configChanged': {
            const event = resolveWorkspaceConfigChangedPayload(
              payload,
              workspaceConfigChangedDedupeRef.current
            )
            if (!event) break

            if (event.regions.includes('skills')) {
              void useSkillsStore.getState().fetchSkills()
            }
            if (event.regions.includes('plugins')) {
              void usePluginStore.getState().fetchPlugins()
            }

            setWorkspaceConfigChange(event)
            setWorkspaceConfigChangeSeq((seq) => seq + 1)
            break
          }

          default:
            break
        }
      }
    )
    return unsubscribe
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // -------------------------------------------------------------------------
  // Server-initiated requests (approval flow)
  // -------------------------------------------------------------------------
  useEffect(() => {
    const unsubscribe = window.api.appServer.onServerRequest((payload) => {
      const { bridgeId, method, params } = payload
      const p = (params ?? {}) as Record<string, unknown>

        if (method === 'item/approval/request') {
        const threadId = typeof p.threadId === 'string' ? p.threadId : null
        const turnId = typeof p.turnId === 'string' ? p.turnId : null
        const activeThreadId = useThreadStore.getState().activeThreadId
        if (threadId && threadId !== activeThreadId) {
          useThreadStore.getState().parkApproval(threadId, {
            bridgeId,
            turnId,
            rawParams: p
          })
          return
          }
          useConversationStore.getState().onApprovalRequest(bridgeId, p)
          return
        }
        // Unknown server requests: respond with null to unblock AppServer
        // (will be handled by specific cases above in future)
        window.api.appServer.sendServerResponse(bridgeId, {
          error: `Unsupported server request: ${method}`
        })
      })
    return unsubscribe
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // -------------------------------------------------------------------------
  // Auto-show detail panel when first file change is detected in a new turn
  // -------------------------------------------------------------------------
  const changedFilesSize = useConversationStore((s) => s.changedFiles.size)
  const activeTurnIdForAutoShow = useConversationStore((s) => s.activeTurnId)
  useEffect(() => {
    if (changedFilesSize === 0) return
    const uiState = useUIStore.getState()
    const currentTurnId = activeTurnIdForAutoShow
    if (!currentTurnId) return
    // Only auto-show once per turn
    if (uiState.autoShowTriggeredForTurn === currentTurnId) return
    useUIStore.getState().markAutoShowForTurn(currentTurnId)
    useUIStore.getState().setActiveDetailTab('changes')
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [changedFilesSize])

  // -------------------------------------------------------------------------
  // Auto-switch detail panel to Plan tab when CreatePlan starts streaming
  // -------------------------------------------------------------------------
  const streamingPlanItemId = useConversationStore(selectStreamingPlanItemId)
  useEffect(() => {
    if (!streamingPlanItemId) return
    const uiState = useUIStore.getState()
    if (uiState.autoShowPlanForItem === streamingPlanItemId) return
    useUIStore.getState().markAutoShowPlanForItem(streamingPlanItemId)
    useUIStore.getState().setActiveDetailTab('plan')
  }, [streamingPlanItemId])

  // -------------------------------------------------------------------------
  // Global keyboard shortcuts
  // -------------------------------------------------------------------------
  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent): void {
      const ctrl = e.ctrlKey || e.metaKey

      // Escape: cancel running turn
      if (e.key === 'Escape') {
        const convState = useConversationStore.getState()
        if (convState.turnStatus === 'running') {
          const activeId = useThreadStore.getState().activeThreadId
          const turnId = convState.activeTurnId
          // Don't send interrupt if we only have a local optimistic ID (server hasn't confirmed yet)
          if (activeId && turnId && !turnId.startsWith('local-turn-')) {
            void window.api.appServer
              .sendRequest('turn/interrupt', { threadId: activeId, turnId })
              .catch((err: unknown) => console.error('turn/interrupt failed:', err))
          }
        }
        return
      }

      // Ctrl+N: new thread
      if (ctrl && e.key === 'n') {
        e.preventDefault()
        if (useConnectionStore.getState().status !== 'connected') return
        useUIStore.getState().goToNewChat()
      }

      // Ctrl+K: open thread search
      if (ctrl && e.key === 'k') {
        e.preventDefault()
        const focusFn = (window as Window & { __sidebarSearchFocus?: () => void }).__sidebarSearchFocus
        focusFn?.()
        return
      }

      // Ctrl+B: toggle sidebar
      if (ctrl && !e.shiftKey && e.key === 'b') {
        e.preventDefault()
        useUIStore.getState().toggleSidebar()
        return
      }

      // Ctrl+P / Cmd+P: open Quick-Open file finder
      if (ctrl && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 'p') {
        const target = e.target as HTMLElement | null
        if (target?.closest('[role="dialog"], [aria-modal="true"]')) {
          return
        }
        const ui = useUIStore.getState()
        if (ui.quickOpenVisible) {
          e.preventDefault()
          return
        }
        e.preventDefault()
        ui.setQuickOpenVisible(true)
        ui.setDetailPanelVisible(true)
        return
      }

      // Ctrl+Shift+B: toggle detail panel
      if (ctrl && e.shiftKey && e.key === 'B') {
        e.preventDefault()
        useUIStore.getState().toggleDetailPanel()
        return
      }

      // Ctrl+Shift+O: switch workspace
      if (ctrl && e.shiftKey && e.key === 'O') {
        e.preventDefault()
        window.api.workspace.pickFolder().then((picked) => {
          if (picked) void window.api.workspace.switch(picked)
        }).catch((err: unknown) => console.error('Ctrl+Shift+O workspace switch failed:', err))
        return
      }

      // Ctrl+Shift+N: open new window
      if (ctrl && e.shiftKey && e.key === 'N') {
        e.preventDefault()
        void window.api.workspace.openNewWindow()
        return
      }

      // Ctrl+,: open settings
      if (ctrl && e.key === ',') {
        e.preventDefault()
        useUIStore.getState().setActiveMainView('settings')
        return
      }

      // Ctrl+Shift+C: copy last agent message to clipboard
      if (ctrl && e.shiftKey && e.key === 'C') {
        e.preventDefault()
        const convState = useConversationStore.getState()
        const turns = convState.turns
        for (let i = turns.length - 1; i >= 0; i--) {
          const items = turns[i].items
          for (let j = items.length - 1; j >= 0; j--) {
            const item = items[j]
            if (item.type === 'agentMessage' && item.text) {
              navigator.clipboard.writeText(item.text).then(() => {
                addToast(translate(localeRef.current, 'toast.copied'), 'success', 2000)
              }).catch(() => {})
              return
            }
          }
        }
        return
      }
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // -------------------------------------------------------------------------
  // Thread selection: when activeThreadId changes, load full thread + subscribe
  // -------------------------------------------------------------------------
  const prevThreadIdRef = useRef<string | null>(null)
  const browserVisibilitySentRef = useRef<Map<string, boolean>>(new Map())
  const activeBrowserTabSentRef = useRef<string | null>(null)
  /**
   * Tracks the thread we currently hold a server-side subscription for.
   * Used as a guard against React StrictMode's mount->cleanup->remount cycle:
   * the cleanup function does NOT reset this ref, so the remount sees
   * subscribedRef === curr and skips the duplicate subscribe call.
   */
  const subscribedThreadIdRef = useRef<string | null>(null)
  const { activeThreadId } = useThreadStore()

  useEffect(() => {
    const unsubscribe = window.api.workspace.viewer.browserUse.onOpen(handleBrowserUseOpen)
    return () => {
      unsubscribe()
    }
  }, [])

  useEffect(() => {
    const unsubscribe = window.api.workspace.viewer.browser.onEvent((event) => {
      handleBrowserEvent(event, {
        locale: localeRef.current,
        workspacePath: workspacePathRef.current
      })
    })
    return () => {
      unsubscribe()
    }
  }, [])

  useEffect(() => {
    const unsubscribe = window.api.workspace.viewer.browserUse.onApprovalRequest((event) => {
      setBrowserUseApprovalRequests((current) => {
        if (current.some((item) => item.requestId === event.requestId)) return current
        return [...current, event]
      })
    })
    return () => {
      unsubscribe()
    }
  }, [])

  const respondToBrowserUseApproval = useCallback(
    (request: BrowserUseApprovalRequestPayload, action: BrowserUseApprovalResponseAction): void => {
      setBrowserUseApprovalRequests((current) => current.filter((item) => item.requestId !== request.requestId))
      window.api.workspace.viewer.browserUse
        .sendApprovalResponse({ requestId: request.requestId, action })
        .catch((err: unknown) => {
          addToast(
            translate(localeRef.current, 'browserUse.approval.sendFailed', {
              error: err instanceof Error ? err.message : String(err)
            }),
            'error'
          )
        })
    },
    []
  )

  // Keep viewerTabStore in sync with active thread, and restore/fallback
  // uiStore.activeDetailTab according to the incoming thread's viewer state.
  useEffect(() => {
    const viewerStore = useViewerTabStore.getState()
    useUIStore.getState().resetAutoShowReasons()
    const outgoingThreadId = viewerStore.currentThreadId
    if (outgoingThreadId) {
      const outgoingState = viewerStore.getThreadState(outgoingThreadId)
      for (const tab of outgoingState.tabs) {
        if (tab.kind === 'browser') {
          void window.api.workspace.viewer.browser.setVisible({ tabId: tab.id, visible: false })
        }
      }
    }

    viewerStore.onThreadSwitched(activeThreadId)

    if (activeThreadId) {
      const threadState = viewerStore.getThreadState(activeThreadId)
      if (threadState.activeTabId) {
        useUIStore.getState().setActiveViewerTab(threadState.activeTabId, { reveal: false })
        const activeTab = threadState.tabs.find((tab) => tab.id === threadState.activeTabId)
        const uiState = useUIStore.getState()
        if (activeTab?.kind === 'browser' && uiState.detailPanelVisible && uiState.activeMainView === 'conversation') {
          void window.api.workspace.viewer.browser.setActive({ tabId: activeTab.id })
        }
      } else {
        const { activeDetailTab } = useUIStore.getState()
        if (activeDetailTab.kind === 'viewer') {
          useUIStore.getState().closeViewerTab({ reveal: false })
        }
      }
    }
  }, [activeThreadId])

  // Hide native browser views when non-conversation surfaces or overlays are shown.
  useEffect(() => {
    const viewerStore = useViewerTabStore.getState()
    const threadId = viewerStore.currentThreadId
    if (!threadId) return
    const threadState = viewerStore.getThreadState(threadId)
    const desiredVisibility = new Map<string, boolean>()
    for (const tab of threadState.tabs) {
      if (tab.kind === 'browser') {
        desiredVisibility.set(tab.id, false)
      }
    }
    const shouldHideBrowser =
      quickOpenVisible
      || activeMainView !== 'conversation'
      || !detailPanelVisible
      || activeDetailTab.kind !== 'viewer'

    let activeBrowserTabId: string | null = null
    if (!shouldHideBrowser) {
      const activeTab = threadState.tabs.find((tab) => tab.id === activeDetailTab.id)
      if (activeTab?.kind === 'browser') {
        desiredVisibility.set(activeTab.id, true)
        activeBrowserTabId = activeTab.id
      }
    }

    const lastVisibility = browserVisibilitySentRef.current
    for (const [tabId, visible] of desiredVisibility.entries()) {
      if (lastVisibility.get(tabId) === visible) continue
      lastVisibility.set(tabId, visible)
      void window.api.workspace.viewer.browser.setVisible({ tabId, visible })
    }

    for (const tabId of [...lastVisibility.keys()]) {
      if (desiredVisibility.has(tabId)) continue
      lastVisibility.delete(tabId)
    }

    if (activeBrowserTabId && activeBrowserTabSentRef.current !== activeBrowserTabId) {
      activeBrowserTabSentRef.current = activeBrowserTabId
      void window.api.workspace.viewer.browser.setActive({ tabId: activeBrowserTabId })
    } else if (!activeBrowserTabId) {
      activeBrowserTabSentRef.current = null
    }
  }, [activeDetailTab, activeMainView, detailPanelVisible, quickOpenVisible])

  useEffect(() => {
    const prev = prevThreadIdRef.current
    const curr = activeThreadId
    const convBeforeReset = useConversationStore.getState()
    const latestCreatePlanTurnId = selectLatestCreatePlanTurnId(convBeforeReset)
    const planApprovalDismissed = useUIStore.getState().planApprovalDismissed
    const prevHasPendingPlanConfirmation =
      prev != null
      && convBeforeReset.threadMode === 'plan'
      && convBeforeReset.turnStatus === 'idle'
      && convBeforeReset.pendingApproval == null
      && latestCreatePlanTurnId != null
      && planApprovalDismissed[latestCreatePlanTurnId] !== true

    if (prev && prev !== curr && convBeforeReset.pendingApproval != null) {
      const pending = convBeforeReset.pendingApproval
      useThreadStore.getState().parkApproval(prev, {
        bridgeId: pending.bridgeId,
        turnId: convBeforeReset.activeTurnId,
        rawParams: {
          threadId: prev,
          turnId: convBeforeReset.activeTurnId,
          approvalType: pending.approvalType,
          operation: pending.operation,
          target: pending.target,
          reason: pending.reason
        }
      })
      useThreadStore.getState().applyRuntimeSnapshot(prev, {
        running: convBeforeReset.turnStatus === 'running' || convBeforeReset.turnStatus === 'waitingApproval',
        waitingOnApproval: true,
        waitingOnPlanConfirmation: false
      }, {
        isActive: false,
        isDesktopOrigin: true
      })
    }

    if (prev && prev !== curr && prevHasPendingPlanConfirmation) {
      useThreadStore.getState().applyRuntimeSnapshot(prev, {
        running: false,
        waitingOnApproval: false,
        waitingOnPlanConfirmation: true
      }, {
        isActive: false,
        isDesktopOrigin: true
      })
    }

    // Always reset conversation state on thread switch
    useConversationStore.getState().reset()

    // Unsubscribe from previous thread when genuinely switching (not StrictMode remount)
    if (prev && prev !== curr) {
      window.api.appServer
        .sendRequest('thread/unsubscribe', { threadId: prev })
        .catch(() => {
          // Best-effort, ignore errors
        })
      if (subscribedThreadIdRef.current === prev) {
        subscribedThreadIdRef.current = null
      }
    }

    if (curr) {
      const requestedId = curr
      performance.mark(`app:thread-switch-start:${requestedId}`)
      window.api.appServer
        .sendRequest('thread/read', { threadId: curr, includeTurns: true })
        .then(async (result) => {
          // Stale guard: user may have switched threads while we were loading
          if (useThreadStore.getState().activeThreadId !== requestedId) {
            useUIStore.getState().cancelPendingWelcomeTurnForThread(requestedId)
            return
          }
          const res = result as { thread: Thread }
          useThreadStore.getState().setActiveThread(res.thread)
          useThreadStore.getState().applyRuntimeSnapshot(requestedId, {
            running: (res.thread.turns ?? []).some((turn) =>
              turn.status === 'running' || turn.status === 'waitingApproval'
            ),
            waitingOnApproval: (res.thread.turns ?? []).some((turn) =>
              turn.status === 'waitingApproval'
            ),
            waitingOnPlanConfirmation: false
          }, {
            isActive: true,
            isDesktopOrigin: res.thread.originChannel?.toLowerCase() === 'dotcraft-desktop'
          })
          {
            const name = res.thread.displayName?.trim()
            if (name) {
              const entry = useThreadStore.getState().threadList.find((t) => t.id === requestedId)
              if (entry && entry.displayName !== name) {
                useThreadStore.getState().renameThread(requestedId, name)
              }
            }
          }
          // Populate conversationStore with historical turns
          const rawTurns = (res.thread.turns ?? []) as unknown as Array<Record<string, unknown>>
          const convTurns = rawTurns.map(wireTurnToConversationTurn)
          performance.mark(`app:thread-switch-rendered:${requestedId}`)
          performance.measure('app:thread-switch', `app:thread-switch-start:${requestedId}`, `app:thread-switch-rendered:${requestedId}`)
          useConversationStore.getState().setTurns(convTurns)
          {
            const rawMode = res.thread.configuration?.mode ?? res.thread.configuration?.Mode
            const mode = typeof rawMode === 'string' && rawMode.toLowerCase() === 'plan'
              ? 'plan'
              : 'agent'
            useConversationStore.getState().setThreadMode(mode)
          }
          useConversationStore.getState().setQueuedInputs(res.thread.queuedInputs ?? [])
          useConversationStore.getState().setContextUsage(res.thread.contextUsage ?? null)
          const parked = useThreadStore.getState().consumeParkedApproval(requestedId)
          if (parked) {
            useConversationStore.getState().onApprovalRequest(parked.bridgeId, parked.rawParams)
          }

          // Welcome composer: send first turn after historical turns are loaded so reset/setTurns do not drop optimistic UI.
          const pendingWelcome = useUIStore.getState().consumePendingWelcomeTurnIfMatch(requestedId)
          if (pendingWelcome != null) {
            const threadId = requestedId
            const path = workspacePathRef.current
            const pendingText = pendingWelcome.text.trim()
            const pendingInputParts = pendingWelcome.inputParts
              ?? buildComposerInputParts({
                text: pendingText,
                files: pendingWelcome.files ?? [],
                images: pendingWelcome.images ?? []
              }).inputParts
            const pendingImages = pendingWelcome.images
            const pendingFiles = pendingWelcome.files ?? []
            const welcomeMode = pendingWelcome.mode ?? 'agent'
            const rawWelcomeModel =
              typeof pendingWelcome.model === 'string' ? pendingWelcome.model.trim() : ''
            const welcomeModel =
              rawWelcomeModel !== '' && rawWelcomeModel !== 'Default' ? rawWelcomeModel : ''
            const welcomeApprovalPolicy = pendingWelcome.approvalPolicy === 'autoApprove'
              ? 'autoApprove'
              : 'default'
            useConversationStore.getState().setThreadMode(welcomeMode)
            if (welcomeModel.length > 0 || welcomeMode !== 'agent' || welcomeApprovalPolicy === 'autoApprove') {
              const existingConfig =
                res.thread.configuration && typeof res.thread.configuration === 'object'
                  ? { ...(res.thread.configuration as Record<string, unknown>) }
                  : {}
              const setCaseInsensitiveField = (
                target: Record<string, unknown>,
                key: string,
                value: unknown
              ): void => {
                const lower = key.toLowerCase()
                const existingKey = Object.keys(target).find((k) => k.toLowerCase() === lower)
                if (existingKey) target[existingKey] = value
                else target[key] = value
              }
              setCaseInsensitiveField(existingConfig, 'mode', welcomeMode)
              if (welcomeModel.length > 0) {
                setCaseInsensitiveField(existingConfig, 'model', welcomeModel)
              }
              if (welcomeApprovalPolicy === 'autoApprove') {
                setCaseInsensitiveField(existingConfig, 'approvalPolicy', welcomeApprovalPolicy)
              }
              let welcomeConfigApplied = false
              try {
                await window.api.appServer.sendRequest('thread/config/update', { threadId, config: existingConfig })
                welcomeConfigApplied = true
              } catch (configErr: unknown) {
                console.error('thread/config/update (welcome configuration) failed:', configErr)
              }
              if (welcomeConfigApplied) {
                const active = useThreadStore.getState().activeThread
                if (active && active.id === threadId) {
                  const mergedCfg: Record<string, unknown> = { ...(active.configuration ?? {}) }
                  setCaseInsensitiveField(mergedCfg, 'mode', welcomeMode)
                  if (welcomeModel.length > 0) {
                    setCaseInsensitiveField(mergedCfg, 'model', welcomeModel)
                  }
                  if (welcomeApprovalPolicy === 'autoApprove') {
                    setCaseInsensitiveField(mergedCfg, 'approvalPolicy', welcomeApprovalPolicy)
                  }
                  useThreadStore.getState().setActiveThread({
                    ...active,
                    configuration: mergedCfg as typeof active.configuration
                  })
                }
              }
            }
            const threadEntry = useThreadStore.getState().threadList.find((t) => t.id === threadId)
            if (!threadEntry?.displayName) {
              const autoName = getFallbackThreadName({
                visibleText: pendingText,
                imagesCount: pendingImages?.length ?? 0,
                filesCount: pendingFiles.length,
                fallbackThreadName: translate(localeRef.current, 'toast.imageMessage'),
                fileFallbackThreadName: translate(localeRef.current, 'toast.fileReferenceMessage'),
                attachmentFallbackThreadName: translate(localeRef.current, 'toast.attachmentMessage')
              })
              useThreadStore.getState().renameThread(threadId, autoName)
            }
            const optimisticItemId = `local-${Date.now()}`
            const optimisticTurnId = `local-turn-${Date.now()}`
            const optimisticNow = new Date().toISOString()
            const userItem: ConversationItem = {
              id: optimisticItemId,
              type: 'userMessage',
              status: 'completed',
              text: pendingText,
              nativeInputParts: pendingInputParts.filter((part) => part.type !== 'localImage' && part.type !== 'image'),
              imageDataUrls: pendingImages?.map((i) => i.dataUrl),
              images: pendingImages?.map((i) => ({
                path: i.tempPath,
                mimeType: i.mimeType,
                fileName: i.fileName
              })),
              createdAt: optimisticNow,
              completedAt: optimisticNow
            }
            const optimisticTurn: ConversationTurn = {
              id: optimisticTurnId,
              threadId,
              status: 'running',
              items: [userItem],
              startedAt: optimisticNow
            }
            useConversationStore.getState().addOptimisticTurn(optimisticTurn)

            if (pendingInputParts.length === 0) {
              useConversationStore.getState().removeOptimisticTurn(optimisticTurnId)
            } else {
              void window.api.appServer
                .sendRequest('turn/start', {
                  threadId,
                  input: pendingInputParts,
                  identity: {
                    channelName: 'dotcraft-desktop',
                    userId: 'local',
                    channelContext: `workspace:${path}`,
                    workspacePath: path
                  }
                })
              .then((result) => {
                const res = result as { turn?: { id?: string } }
                if (res.turn?.id) {
                  useConversationStore.getState().promoteOptimisticTurn(optimisticTurnId, res.turn.id)
                }
              })
              .catch((turnErr: unknown) => {
                console.error('Welcome screen turn/start failed:', turnErr)
                useConversationStore.getState().removeOptimisticTurn(optimisticTurnId)
              })
            }
          }
        })
        .catch((err: unknown) => {
          console.error('thread/read failed:', err)
          useUIStore.getState().cancelPendingWelcomeTurnForThread(requestedId)
        })

      // Guard: skip subscribe if we already hold a subscription for this thread.
      // React StrictMode runs this effect twice (mount, cleanup, remount) — but the
      // cleanup does NOT reset this ref, so the remount finds subscribedRef === curr
      // and skips the call, preventing a second server-side subscription.
      if (subscribedThreadIdRef.current !== curr) {
        subscribedThreadIdRef.current = curr
        window.api.appServer
          .sendRequest('thread/subscribe', { threadId: curr })
          .catch((err: unknown) => console.error('thread/subscribe failed:', err))
      }
    } else {
      // No active thread: unsubscribe whatever we were subscribed to
      if (subscribedThreadIdRef.current) {
        void window.api.appServer
          .sendRequest('thread/unsubscribe', { threadId: subscribedThreadIdRef.current })
          .catch(() => {})
        subscribedThreadIdRef.current = null
      }
      useThreadStore.getState().setActiveThread(null)
    }

    prevThreadIdRef.current = curr
    // No cleanup return here: a cleanup that resets subscribedThreadIdRef defeats
    // the StrictMode guard above. Thread-switch unsubscription is handled by the
    // prev !== curr block. On window close the connection terminates anyway.
  }, [activeThreadId])

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------
  const isFatalError = isFatalConnectionError(status, errorType)

  // No workspace configured yet (first launch or welcome screen)
  const showWelcome = !workspacePath && !isFatalError
  const showSetupInterstitial =
    workspacePath !== '' &&
    workspaceStatus.status === 'needs-setup' &&
    !showSetupWizard &&
    !isFatalError
  const showSetupFlow =
    workspacePath !== '' &&
    workspaceStatus.status === 'needs-setup' &&
    showSetupWizard &&
    !isFatalError

  if (isFatalError) {
    return (
      <AppChrome>
        <>
          <ConfirmDialogHost />
          <ToastContainer />
          <ErrorScreen onOpenSettings={() => useUIStore.getState().setActiveMainView('settings')} />
        </>
      </AppChrome>
    )
  }

  if (showWelcome) {
    return (
      <AppChrome>
        <>
          <ToastContainer />
          <WelcomeScreen />
        </>
      </AppChrome>
    )
  }

  if (showSetupInterstitial) {
    return (
      <AppChrome>
        <>
          <ToastContainer />
          <WorkspaceSetupInterstitial
            workspacePath={workspacePath}
            onStart={() => {
              void window.api.workspace
                .getStatus()
                .then((payload) => {
                  syncWorkspaceStatus(payload)
                  if (payload.status === 'needs-setup') {
                    setShowSetupWizard(true)
                  }
                })
                .catch(() => {
                  setShowSetupWizard(true)
                })
            }}
            onChooseDifferentWorkspace={() => {
              void window.api.workspace.clearSelection()
            }}
          />
        </>
      </AppChrome>
    )
  }

  if (showSetupFlow) {
    return (
      <AppChrome>
        <>
          <ToastContainer />
          <WorkspaceSetupWizard
            workspacePath={workspacePath}
            workspaceStatus={workspaceStatus}
            onCancel={() => {
              setShowSetupWizard(false)
            }}
          />
        </>
      </AppChrome>
    )
  }

  return (
    <AppChrome>
      <>
        <ConfirmDialogHost />
        <ToastContainer />
        {quickOpenVisible && (
          <QuickOpenDialog
            onClose={() => setQuickOpenVisible(false)}
          />
        )}
        {browserUseApprovalRequests[0] && (
          <BrowserUseApprovalDialog
            locale={locale}
            request={browserUseApprovalRequests[0]}
            onRespond={(action) => respondToBrowserUseApproval(browserUseApprovalRequests[0], action)}
          />
        )}
        {status === 'disconnected' && isExpectedRestart && (
          <div
            role="status"
            aria-live="polite"
            style={{
              padding: '8px 16px',
              backgroundColor: 'rgba(56, 189, 248, 0.12)',
              borderBottom: '1px solid rgba(56, 189, 248, 0.35)',
              color: 'var(--text-primary)',
              fontSize: '12px',
              flexShrink: 0
            }}
          >
            {translate(locale, 'settings.restartingAppServer')}
          </div>
        )}
        {activeMainView === 'settings' && pendingRestartVisible && (
          <div
            role="status"
            aria-live="polite"
            style={{
              padding: '8px 16px',
              backgroundColor: 'rgba(245, 158, 11, 0.12)',
              borderBottom: '1px solid rgba(245, 158, 11, 0.35)',
              color: 'var(--text-primary)',
              fontSize: '12px',
              flexShrink: 0,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: '8px 12px',
              flexWrap: 'wrap'
            }}
          >
            <span style={{ minWidth: '180px', flex: '1 1 220px', overflowWrap: 'anywhere' }}>
              {translate(locale, 'settings.pendingRestart.message')}
            </span>
            <span style={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0 }}>
              <button
                type="button"
                onClick={() => ignorePendingRestart()}
                disabled={pendingRestartApplying}
                style={topBannerSecondaryButtonStyle(pendingRestartApplying)}
              >
                {translate(locale, 'settings.pendingRestart.ignore')}
              </button>
              <button
                type="button"
                onClick={() => {
                  void applyPendingRestart()
                }}
                disabled={pendingRestartApplying}
                style={topBannerPrimaryButtonStyle(pendingRestartApplying)}
              >
                {pendingRestartApplying
                  ? translate(locale, 'settings.action.restarting')
                  : translate(locale, 'settings.pendingRestart.apply')}
              </button>
            </span>
          </div>
        )}
        {showSlowConnectingHint && (
          <div
            role="status"
            aria-live="polite"
            style={{
              padding: '8px 16px',
              backgroundColor: 'rgba(245, 158, 11, 0.12)',
              borderBottom: '1px solid rgba(245, 158, 11, 0.35)',
              color: 'var(--text-primary)',
              fontSize: '12px',
              flexShrink: 0
            }}
          >
            {errorMessage?.trim() || translate(locale, 'connection.startupTakingLong')}
          </div>
        )}
        <ThreePanel
          sidebar={<Sidebar workspaceName={workspaceName} workspacePath={workspacePath} />}
          conversation={
            activeMainView === 'settings' ? (
              <SettingsView
                workspacePath={workspacePath}
                onThreadListRefreshRequested={() => {
                  void reloadThreadList()
                }}
                workspaceConfigChange={workspaceConfigChange}
                workspaceConfigChangeSeq={workspaceConfigChangeSeq}
              />
            ) : activeMainView === 'channels' ? (
              <ChannelsView />
            ) : activeMainView === 'skills' ? (
              <PluginsView />
            ) : activeMainView === 'automations' ? (
              <AutomationsView />
            ) : (
              <ConversationPanel
                workspacePath={workspacePath}
                workspaceConfigChange={workspaceConfigChange}
                workspaceConfigChangeSeq={workspaceConfigChangeSeq}
              />
            )
          }
          detail={<DetailPanel workspacePath={workspacePath} />}
        />
      </>
    </AppChrome>
  )
}
