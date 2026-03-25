import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { translate } from '../shared/locales'
import { useLocale } from './contexts/LocaleContext'
import { basename } from './utils/path'
import { initConnectionStore, useConnectionStore } from './stores/connectionStore'
import { useThreadStore } from './stores/threadStore'
import { useConversationStore } from './stores/conversationStore'
import { useUIStore } from './stores/uiStore'
import { ThreePanel } from './components/layout/ThreePanel'
import { SkillsView } from './components/skills/SkillsView'
import { AutomationsView } from './components/automations/AutomationsView'
import { useAutomationsStore } from './stores/automationsStore'
import { useCronStore, type CronJobWire } from './stores/cronStore'
import { useReviewPanelStore } from './stores/reviewPanelStore'
import type { AutomationTask } from './stores/automationsStore'
import { CustomMenuBar } from './components/layout/CustomMenuBar'
import { Sidebar } from './components/layout/Sidebar'
import { ConversationPanel } from './components/layout/ConversationPanel'
import { DetailPanel } from './components/layout/DetailPanel'
import { ErrorScreen } from './components/ErrorScreen'
import { WelcomeScreen } from './components/WelcomeScreen'
import { ConfirmDialogHost } from './components/ui/ConfirmDialog'
import { ToastContainer } from './components/ui/ToastContainer'
import { SettingsDialog } from './components/ui/SettingsDialog'
import { addJobResultToast, addToast } from './stores/toastStore'
import type { SessionIdentity, Thread, ThreadSummary } from './types/thread'
import { wireTurnToConversationTurn } from './types/conversation'
import type { ConversationItem, ConversationTurn } from './types/conversation'
import type { SubAgentEntry } from './types/toolCall'
import { applyTheme, resolveTheme } from './utils/theme'
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
 * - Registers Ctrl+N (new thread) and Ctrl+K (focus search) global shortcuts
 * - Spec §9, §12
 */
export function App(): JSX.Element {
  const locale = useLocale()
  const localeRef = useRef(locale)
  localeRef.current = locale

  const [workspacePath, setWorkspacePath] = useState('')
  const [workspaceName, setWorkspaceName] = useState('DotCraft')
  const [showSettings, setShowSettings] = useState(false)
  const { status, errorType } = useConnectionStore()
  const activeMainView = useUIStore((s) => s.activeMainView)
  const {
    setThreadList,
    setLoading
  } = useThreadStore()

  const workspacePathRef = useRef('')

  // -------------------------------------------------------------------------
  // Bootstrap: workspace path + connection store
  // -------------------------------------------------------------------------
  useEffect(() => {
    performance.mark('app:bootstrap-start')
    const unsubscribe = initConnectionStore()

    window.api.window.getWorkspacePath().then((path) => {
      workspacePathRef.current = path
      setWorkspacePath(path)
      if (path) {
        const name = basename(path)
        setWorkspaceName(name)
      }
    })

    return unsubscribe
  }, [])

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

  // -------------------------------------------------------------------------
  // Load thread list when connection becomes "connected"
  // -------------------------------------------------------------------------
  const prevStatusRef = useRef<string>('')
  useEffect(() => {
    if (status === 'connected' && prevStatusRef.current !== 'connected') {
      performance.mark('app:connected')
      performance.measure('app:startup', 'app:bootstrap-start', 'app:connected')
      const path = workspacePathRef.current
      const identity: SessionIdentity = {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: `workspace:${path}`,
        workspacePath: path
      }
      setLoading(true)
      window.api.appServer
        .sendRequest('thread/list', { identity })
        .then((result) => {
          const res = result as { data: ThreadSummary[] }
          setThreadList(res.data ?? [])
        })
        .catch((err: unknown) => {
          console.error('Failed to load thread list:', err)
          setThreadList([])
        })
        .finally(() => setLoading(false))

      const caps = useConnectionStore.getState().capabilities
      if (caps?.automations) {
        useAutomationsStore.getState().fetchTasks()
      }
      if (caps?.cronManagement) {
        void useCronStore.getState().fetchJobs()
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
      useCronStore.getState().reset()
      useAutomationsStore.getState().selectTask(null)
      useUIStore.getState().setAutomationsTab('tasks')
      useUIStore.getState().setActiveDetailTab('changes')
      useUIStore.getState().setActiveMainView('conversation')
      useUIStore.getState().setPendingWelcomeTurn(null)
    }

    // On workspace switch (connecting), update workspace path from Main
    if (status === 'connecting') {
      window.api.window.getWorkspacePath().then((path) => {
        if (path) {
          workspacePathRef.current = path
          setWorkspacePath(path)
          const name = basename(path)
          setWorkspaceName(name)
        }
      }).catch(() => {})
    }
    prevStatusRef.current = status
  }, [status, setThreadList, setLoading])

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

          case 'thread/statusChanged': {
            const pp = p as { threadId: string; newStatus: string }
            doUpdateStatus(pp.threadId, pp.newStatus as 'active' | 'paused' | 'archived')
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
            // Track running turn for background activity indicator
            const startedThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
            if (startedThreadId) useThreadStore.getState().markTurnStarted(startedThreadId)
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
            if (completedThreadId) useThreadStore.getState().markTurnEnded(completedThreadId)
            const pendingBefore = conv.pendingMessage
            conv.onTurnCompleted(rawTurn)
            // Auto-send pending message if any
            const pending = pendingBefore
            if (pending) {
              const activeId = useThreadStore.getState().activeThreadId
              if (activeId) {
                const path = workspacePathRef.current
                void window.api.appServer
                  .sendRequest('turn/start', {
                    threadId: activeId,
                    input: [{ type: 'text', text: pending }],
                    identity: {
                      channelName: 'dotcraft-desktop',
                      userId: 'local',
                      channelContext: `workspace:${path}`,
                      workspacePath: path
                    }
                  })
                  .catch((err: unknown) =>
                    console.error('Auto-send pending message failed:', err)
                  )
              }
              // Clear the pending message now that we've sent it
              useConversationStore.getState().setPendingMessage(null)
            }
            // Refresh thread name: server auto-sets displayName from the first user message.
            // Since there is no thread/renamed notification, we poll once after each completed
            // turn if the thread still has no custom name.
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
            if (failedThreadId) useThreadStore.getState().markTurnEnded(failedThreadId)
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
            if (cancelledThreadId) useThreadStore.getState().markTurnEnded(cancelledThreadId)
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
      const conv = useConversationStore.getState()

      if (method === 'item/approval/request') {
        conv.onApprovalRequest(bridgeId, p)
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

      // Ctrl+Shift+M: toggle agent/plan mode
      if (ctrl && e.shiftKey && e.key === 'M') {
        e.preventDefault()
        const activeId = useThreadStore.getState().activeThreadId
        if (!activeId) return
        const convState = useConversationStore.getState()
        const newMode = convState.threadMode === 'agent' ? 'plan' : 'agent'
        convState.setThreadMode(newMode)
        void window.api.appServer
          .sendRequest('thread/mode/set', { threadId: activeId, mode: newMode })
          .catch((err: unknown) => console.error('thread/mode/set failed:', err))
        return
      }

      // Ctrl+N: new thread
      if (ctrl && e.key === 'n') {
        e.preventDefault()
        const path = workspacePathRef.current
        if (useConnectionStore.getState().status !== 'connected') return
        window.api.appServer
          .sendRequest('thread/start', {
            identity: {
              channelName: 'dotcraft-desktop',
              userId: 'local',
              channelContext: `workspace:${path}`,
              workspacePath: path
            },
            historyMode: 'server'
          })
          .then((result) => {
            const res = result as { thread: ThreadSummary }
            useThreadStore.getState().addThread(res.thread)
            useThreadStore.getState().setActiveThreadId(res.thread.id)
            useUIStore.getState().setActiveMainView('conversation')
          })
          .catch((err: unknown) => console.error('Ctrl+N thread/start failed:', err))
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
        setShowSettings(true)
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
  }, [showSettings])

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
        .then((result) => {
          // Stale guard: user may have switched threads while we were loading
          if (useThreadStore.getState().activeThreadId !== requestedId) {
            useUIStore.getState().cancelPendingWelcomeTurnForThread(requestedId)
            return
          }
          const res = result as { thread: Thread }
          useThreadStore.getState().setActiveThread(res.thread)
          // Populate conversationStore with historical turns
          const rawTurns = (res.thread.turns ?? []) as unknown as Array<Record<string, unknown>>
          const convTurns = rawTurns.map(wireTurnToConversationTurn)
          performance.mark(`app:thread-switch-rendered:${requestedId}`)
          performance.measure('app:thread-switch', `app:thread-switch-start:${requestedId}`, `app:thread-switch-rendered:${requestedId}`)
          useConversationStore.getState().setTurns(convTurns)

          // Welcome composer: send first turn after historical turns are loaded so reset/setTurns do not drop optimistic UI.
          const pendingWelcome = useUIStore.getState().consumePendingWelcomeTurnIfMatch(requestedId)
          if (pendingWelcome != null) {
            const threadId = requestedId
            const path = workspacePathRef.current
            const pendingText = pendingWelcome.text.trim()
            const pendingImages = pendingWelcome.images
            const threadEntry = useThreadStore.getState().threadList.find((t) => t.id === threadId)
            if (!threadEntry?.displayName) {
              const autoName =
                pendingText.length > 50
                  ? pendingText.slice(0, 50) + '...'
                  : pendingText || translate(localeRef.current, 'toast.imageMessage')
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
              imageDataUrls: pendingImages?.map((i) => i.dataUrl),
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

            const inputParts: Array<{ type: string; text?: string; path?: string }> = []
            if (pendingText.length > 0) {
              inputParts.push({ type: 'text', text: pendingText })
            }
            for (const img of pendingImages ?? []) {
              inputParts.push({ type: 'localImage', path: img.tempPath })
            }
            if (inputParts.length === 0) {
              useConversationStore.getState().removeOptimisticTurn(optimisticTurnId)
            } else {
              void window.api.appServer
                .sendRequest('turn/start', {
                  threadId,
                  input: inputParts,
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

  if (isFatalError) {
    return (
      <AppChrome>
        <>
          <ConfirmDialogHost />
          <ToastContainer />
          <ErrorScreen onOpenSettings={() => setShowSettings(true)} />
          {showSettings && <SettingsDialog onClose={() => setShowSettings(false)} />}
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

  return (
    <AppChrome>
      <>
        <ConfirmDialogHost />
        <ToastContainer />
        {showSettings && <SettingsDialog onClose={() => setShowSettings(false)} />}
        <ThreePanel
          sidebar={
            <Sidebar workspaceName={workspaceName} workspacePath={workspacePath} onOpenSettings={() => setShowSettings(true)} />
          }
          conversation={
            activeMainView === 'skills' ? (
              <SkillsView />
            ) : activeMainView === 'automations' ? (
              <AutomationsView />
            ) : (
              <ConversationPanel workspacePath={workspacePath} />
            )
          }
          detail={<DetailPanel workspacePath={workspacePath} />}
        />
      </>
    </AppChrome>
  )
}
