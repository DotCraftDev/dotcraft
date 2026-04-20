import { useCallback, useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { translate } from '../shared/locales'
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
import { ThreePanel } from './components/layout/ThreePanel'
import { SkillsView } from './components/skills/SkillsView'
import { AutomationsView } from './components/automations/AutomationsView'
import { useAutomationsStore } from './stores/automationsStore'
import { useCronStore, type CronJobWire } from './stores/cronStore'
import { useReviewPanelStore } from './stores/reviewPanelStore'
import type { AutomationTask } from './stores/automationsStore'
import { useModelCatalogStore } from './stores/modelCatalogStore'
import { useMcpStore, type McpServerStatusWire } from './stores/mcpStore'
import { useSkillsStore } from './stores/skillsStore'
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
import type { SessionIdentity, Thread, ThreadSummary } from './types/thread'
import { wireTurnToConversationTurn } from './types/conversation'
import type { ConversationItem, ConversationTurn } from './types/conversation'
import type { SubAgentEntry } from './types/toolCall'
import { applyTheme, resolveTheme } from './utils/theme'
import { ensureVisibleChannelsSeeded } from './utils/visibleChannelsDefaults'
import { buildComposerInputParts } from './utils/composeInputParts'
import { getFallbackThreadName } from './utils/threadFallbackName'
import {
  resolveWorkspaceConfigChangedPayload,
  type WorkspaceConfigChangedPayload
} from './utils/workspaceConfigChanged'
import type { DiscoveredModule, ModuleStatusMap, WorkspaceStatusPayload } from '../preload/api.d'
import './styles/tokens.css'

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
  const [showSlowConnectingHint, setShowSlowConnectingHint] = useState(false)
  const activeMainView = useUIStore((s) => s.activeMainView)
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

    void window.api.workspace
      .getStatus()
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
    if (!workspacePath || status !== 'connecting') {
      setShowSlowConnectingHint(false)
      return
    }
    const timer = setTimeout(() => {
      setShowSlowConnectingHint(true)
    }, 6000)
    return () => {
      clearTimeout(timer)
      setShowSlowConnectingHint(false)
    }
  }, [status, workspacePath])

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
        useAutomationsStore.getState().fetchTasks()
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
      useUIStore.getState().setActiveDetailTab('changes')
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
            conv.onTurnStarted(rawTurn)
            const startedThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
            {
              const rs = useReviewPanelStore.getState()
              if (startedThreadId && rs.reviewThreadId === startedThreadId) {
                rs.onTurnStarted(rawTurn)
              }
            }
            break
          }

          case 'turn/completed': {
            const rawTurn = (p.turn ?? p) as Record<string, unknown>
            const completedThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
            const pendingBefore = conv.pendingMessage
            conv.onTurnCompleted(rawTurn)
            // Auto-send pending message if any
            const pending = pendingBefore
            if (pending) {
              const activeId = useThreadStore.getState().activeThreadId
              if (activeId) {
                const path = workspacePathRef.current
                void (async () => {
                  const pendingInputParts = pending.inputParts
                    ?? buildComposerInputParts({
                      text: pending.text.trim(),
                      files: pending.files ?? []
                    }).inputParts
                  if (pendingInputParts.length === 0) return
                  await window.api.appServer.sendRequest('turn/start', {
                    threadId: activeId,
                    input: pendingInputParts,
                    identity: {
                      channelName: 'dotcraft-desktop',
                      userId: 'local',
                      channelContext: `workspace:${path}`,
                      workspacePath: path
                    }
                  })
                })().catch((err: unknown) =>
                  console.error('Auto-send pending message failed:', err)
                )
              }
              // Clear the pending message now that we've sent it
              useConversationStore.getState().setPendingMessage(null)
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
            {
              const rs = useReviewPanelStore.getState()
              if (completedThreadId && rs.reviewThreadId === completedThreadId) {
                rs.onTurnCompleted(rawTurn)
              }
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
            if (errorCode === -32020 || error.includes('-32020')) {
              conv.onApprovalTimeout()
            }
            conv.onTurnFailed(rawTurn, error)
            {
              const rs = useReviewPanelStore.getState()
              if (failedThreadId && rs.reviewThreadId === failedThreadId) {
                rs.onTurnFailed(rawTurn, error)
              }
            }
            break
          }

          case 'turn/cancelled': {
            const rawTurn = (p.turn ?? p) as Record<string, unknown>
            const cancelledThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
            const reason = (p.reason as string) ?? ''
            conv.onTurnCancelled(rawTurn, reason)
            {
              const rs = useReviewPanelStore.getState()
              if (cancelledThreadId && rs.reviewThreadId === cancelledThreadId) {
                rs.onTurnCancelled(rawTurn, reason)
              }
            }
            break
          }

          // ── Item lifecycle ────────────────────────────────────────────
          case 'item/started':
            conv.onItemStarted(p)
            {
              const tid = (p.threadId as string | undefined) ?? ''
              const rs = useReviewPanelStore.getState()
              if (tid && rs.reviewThreadId === tid) {
                rs.onItemStarted(p)
              }
            }
            break

          case 'item/agentMessage/delta':
            conv.onAgentMessageDelta((p.delta as string) ?? '')
            {
              const tid = (p.threadId as string | undefined) ?? ''
              const rs = useReviewPanelStore.getState()
              if (tid && rs.reviewThreadId === tid) {
                rs.onAgentMessageDelta((p.delta as string) ?? '')
              }
            }
            break

          case 'item/reasoning/delta':
            conv.onReasoningDelta((p.delta as string) ?? '')
            {
              const tid = (p.threadId as string | undefined) ?? ''
              const rs = useReviewPanelStore.getState()
              if (tid && rs.reviewThreadId === tid) {
                rs.onReasoningDelta((p.delta as string) ?? '')
              }
            }
            break

          case 'item/commandExecution/outputDelta':
            conv.onCommandExecutionDelta({
              threadId: (p.threadId as string | undefined),
              turnId: (p.turnId as string | undefined),
              itemId: (p.itemId as string | undefined),
              delta: (p.delta as string | undefined)
            })
            {
              const tid = (p.threadId as string | undefined) ?? ''
              const rs = useReviewPanelStore.getState()
              if (tid && rs.reviewThreadId === tid) {
                rs.onCommandExecutionDelta({
                  threadId: (p.threadId as string | undefined),
                  turnId: (p.turnId as string | undefined),
                  itemId: (p.itemId as string | undefined),
                  delta: (p.delta as string | undefined)
                })
              }
            }
            break

          case 'item/toolCall/argumentsDelta':
            conv.onToolCallArgumentsDelta({
              threadId: (p.threadId as string | undefined),
              turnId: (p.turnId as string | undefined),
              itemId: (p.itemId as string | undefined),
              toolName: (p.toolName as string | undefined),
              callId: (p.callId as string | undefined),
              delta: (p.delta as string | undefined)
            })
            break

          case 'item/completed':
            conv.onItemCompleted(p)
            {
              const tid = (p.threadId as string | undefined) ?? ''
              const rs = useReviewPanelStore.getState()
              if (tid && rs.reviewThreadId === tid) {
                rs.onItemCompleted(p)
              }
            }
            break

          case 'item/usage/delta': {
            const input = (p.inputTokens as number) ?? 0
            const output = (p.outputTokens as number) ?? 0
            conv.onUsageDelta(input, output)
            break
          }

          // ── SubAgent progress ─────────────────────────────────────────
          case 'subagent/progress': {
            const entries = (p.entries as SubAgentEntry[]) ?? []
            const threadId = (p.threadId as string | undefined) ?? ''
            const activeId = useThreadStore.getState().activeThreadId
            const reviewThreadId = useReviewPanelStore.getState().reviewThreadId
            if (threadId && threadId === reviewThreadId) {
              useReviewPanelStore.getState().onSubagentProgress(entries)
            } else if (!threadId || threadId === activeId) {
              conv.onSubagentProgress(entries)
            }
            break
          }

          // ── System events ─────────────────────────────────────────────
          case 'system/event':
            conv.onSystemEvent((p.kind as string) ?? '')
            break

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
      }
      // Unknown server requests: respond with null to unblock AppServer
      // (will be handled by specific cases above in future)
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

      // Ctrl+K: focus thread search
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
  /**
   * Tracks the thread we currently hold a server-side subscription for.
   * Used as a guard against React StrictMode's mount->cleanup->remount cycle:
   * the cleanup function does NOT reset this ref, so the remount sees
   * subscribedRef === curr and skips the duplicate subscribe call.
   */
  const subscribedThreadIdRef = useRef<string | null>(null)
  const { activeThreadId } = useThreadStore()

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
            useConversationStore.getState().setThreadMode(welcomeMode)
            if (welcomeModel.length > 0) {
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
              setCaseInsensitiveField(existingConfig, 'model', welcomeModel)
              try {
                await window.api.appServer.sendRequest('thread/config/update', { threadId, config: existingConfig })
              } catch (configErr: unknown) {
                console.error('thread/config/update (welcome model) failed:', configErr)
              }
            } else if (welcomeMode !== 'agent') {
              try {
                await window.api.appServer.sendRequest('thread/mode/set', { threadId, mode: welcomeMode })
              } catch (modeErr: unknown) {
                console.error('thread/mode/set (welcome) failed:', modeErr)
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
  const isFatalError =
    status === 'error' &&
    (errorType === 'binary-not-found' || errorType === 'handshake-timeout')

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
              <SkillsView />
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
