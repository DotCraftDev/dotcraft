import { create } from 'zustand'
import type { ConversationTurn, ConversationItem, TurnStatus } from '../types/conversation'
import { wireTurnToConversationTurn } from '../types/conversation'
import type { AutomationTask } from './automationsStore'
import { useAutomationsStore } from './automationsStore'
import type { SubAgentEntry } from '../types/toolCall'

/** Stable chronological order for turn items (Wire Protocol may interleave events). */
function sortItemsByCreatedAt(items: ConversationItem[]): ConversationItem[] {
  return [...items].sort(
    (a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()
  )
}

export interface ReviewPanelState {
  /** Task id for which the panel was opened (used to sync when threadId appears later). */
  openedTaskId: string | null
  taskDetail: AutomationTask | null
  reviewThreadId: string | null
  /** True after thread/subscribe succeeded for live streaming. */
  subscriptionActive: boolean
  turns: ConversationTurn[]
  turnStatus: 'idle' | 'running' | 'waitingApproval'
  activeTurnId: string | null
  streamingMessage: string
  streamingReasoning: string
  streamingReasoningStartedAt: number | null
  activeItemId: string | null
  streamingActive: boolean
  loading: boolean
  loadError: string | null
  approving: boolean
  rejecting: boolean
  actionError: string | null
  /** SubAgent progress rows for the thread being reviewed (isolated from main conversation). */
  subAgentEntries: SubAgentEntry[]

  openReviewPanel(taskId: string): Promise<void>
  /** Unsubscribe and clear review state (does not change sidebar selection). */
  destroyReviewPanel(): void
  closeReviewPanel(): void
  /** When automations list gains a threadId for a previously pending task, load history + subscribe. */
  maybeAdvancePendingThread(): Promise<void>
  loadThreadSnapshot(threadId: string, task: AutomationTask): Promise<void>

  onTurnStarted(rawTurn: Record<string, unknown>): void
  onItemStarted(params: Record<string, unknown>): void
  onAgentMessageDelta(delta: string): void
  onReasoningDelta(delta: string): void
  onItemCompleted(params: Record<string, unknown>): void
  onTurnCompleted(rawTurn: Record<string, unknown>): void
  onTurnFailed(rawTurn: Record<string, unknown>, error: string): void
  onTurnCancelled(rawTurn: Record<string, unknown>, reason: string): void
  onSubagentProgress(entries: SubAgentEntry[]): void
}

function emptyTurnFields() {
  return {
    turns: [] as ConversationTurn[],
    turnStatus: 'idle' as const,
    activeTurnId: null as string | null,
    streamingMessage: '',
    streamingReasoning: '',
    streamingReasoningStartedAt: null as number | null,
    activeItemId: null as string | null,
    streamingActive: false,
    subAgentEntries: [] as SubAgentEntry[]
  }
}

export const useReviewPanelStore = create<ReviewPanelState>((set, get) => ({
  openedTaskId: null,
  taskDetail: null,
  reviewThreadId: null,
  subscriptionActive: false,
  ...emptyTurnFields(),
  loading: false,
  loadError: null,
  approving: false,
  rejecting: false,
  actionError: null,

  async openReviewPanel(taskId: string) {
    const prev = get()
    if (prev.subscriptionActive && prev.reviewThreadId) {
      void window.api.appServer
        .sendRequest('thread/unsubscribe', { threadId: prev.reviewThreadId })
        .catch(() => {})
    }

    set({
      openedTaskId: taskId,
      taskDetail: null,
      reviewThreadId: null,
      subscriptionActive: false,
      ...emptyTurnFields(),
      loading: true,
      loadError: null,
      actionError: null
    })

    const tasks = useAutomationsStore.getState().tasks
    const listTask = tasks.find((t) => t.id === taskId)
    const sourceName = listTask?.sourceName ?? 'local'

    try {
      const readResult = (await window.api.appServer.sendRequest('automation/task/read', {
        taskId,
        sourceName
      })) as Record<string, unknown>

      const task = mapWireTaskToAutomationTask(readResult)
      set({ taskDetail: task })

      if (task.threadId) {
        await get().loadThreadSnapshot(task.threadId, task)
      }
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      set({ loadError: msg, loading: false })
      return
    }

    set({ loading: false })
  },

  async loadThreadSnapshot(threadId: string, task: AutomationTask) {
    set({ reviewThreadId: threadId, ...emptyTurnFields(), subscriptionActive: false })

    try {
      const res = (await window.api.appServer.sendRequest('thread/read', {
        threadId,
        includeTurns: true
      })) as { thread?: { turns?: Array<Record<string, unknown>> } }
      const rawTurns = res.thread?.turns ?? []
      const turns = rawTurns.map((t) => wireTurnToConversationTurn(t))
      const runningTurn = turns.find((t) => t.status === 'running')
      set({
        turns,
        turnStatus: runningTurn ? 'running' : 'idle',
        activeTurnId: runningTurn ? runningTurn.id : null,
        streamingActive: false
      })
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      set({ loadError: msg })
      return
    }

    if (task.status === 'agent_running' || task.status === 'dispatched') {
      try {
        await window.api.appServer.sendRequest('thread/subscribe', { threadId })
        set({ subscriptionActive: true, streamingActive: task.status === 'agent_running' })
      } catch {
        set({ subscriptionActive: false })
      }
    }
  },

  async maybeAdvancePendingThread() {
    const { openedTaskId, reviewThreadId, loading } = get()
    if (!openedTaskId || loading || reviewThreadId) return

    const task = useAutomationsStore.getState().tasks.find((t) => t.id === openedTaskId)
    if (!task?.threadId) return

    set({ taskDetail: { ...task } })
    await get().loadThreadSnapshot(task.threadId, task)
  },

  destroyReviewPanel() {
    const { reviewThreadId, subscriptionActive } = get()
    if (subscriptionActive && reviewThreadId) {
      void window.api.appServer
        .sendRequest('thread/unsubscribe', { threadId: reviewThreadId })
        .catch(() => {})
    }
    set({
      openedTaskId: null,
      taskDetail: null,
      reviewThreadId: null,
      subscriptionActive: false,
      ...emptyTurnFields(),
      loading: false,
      loadError: null,
      actionError: null
    })
  },

  closeReviewPanel() {
    get().destroyReviewPanel()
    useAutomationsStore.getState().selectTask(null)
  },

  onTurnStarted(rawTurn) {
    const turn = wireTurnToConversationTurn(rawTurn)
    set((state) => {
      const alreadyExists = state.turns.find((t) => t.id === turn.id)
      if (alreadyExists) {
        return {
          turns: state.turns.map((t) =>
            t.id === turn.id ? { ...t, status: 'running' as TurnStatus, startedAt: turn.startedAt } : t
          ),
          turnStatus: 'running',
          activeTurnId: turn.id,
          streamingMessage: '',
          streamingReasoning: '',
          streamingReasoningStartedAt: null,
          activeItemId: null,
          streamingActive: true,
          subAgentEntries: [] as SubAgentEntry[]
        }
      }
      return {
        turns: [...state.turns, turn],
        turnStatus: 'running',
        activeTurnId: turn.id,
        streamingMessage: '',
        streamingReasoning: '',
        streamingReasoningStartedAt: null,
        activeItemId: null,
        streamingActive: true,
        subAgentEntries: [] as SubAgentEntry[]
      }
    })
  },

  onItemStarted(params) {
    const item = params.item as Record<string, unknown>
    const type = item?.type as string
    const itemId = item?.id as string
    const turnId = params.turnId as string

    if (type === 'agentMessage') {
      const newItem: ConversationItem = {
        id: itemId ?? '',
        type: 'agentMessage',
        status: 'streaming',
        text: '',
        createdAt: (item?.createdAt as string) ?? new Date().toISOString()
      }
      set((state) => ({
        streamingMessage: '',
        activeItemId: itemId,
        turns: state.turns.map((t) =>
          t.id === turnId ? { ...t, items: sortItemsByCreatedAt([...t.items, newItem]) } : t
        )
      }))
    } else if (type === 'reasoningContent') {
      const newItem: ConversationItem = {
        id: itemId ?? '',
        type: 'reasoningContent',
        status: 'streaming',
        reasoning: '',
        createdAt: (item?.createdAt as string) ?? new Date().toISOString()
      }
      set((state) => ({
        streamingReasoning: '',
        streamingReasoningStartedAt: Date.now(),
        activeItemId: itemId,
        turns: state.turns.map((t) =>
          t.id === turnId ? { ...t, items: sortItemsByCreatedAt([...t.items, newItem]) } : t
        )
      }))
    } else if (type === 'toolCall') {
      const itemPayload = (item?.payload ?? {}) as Record<string, unknown>
      const newItem: ConversationItem = {
        id: itemId ?? '',
        type: 'toolCall',
        status: 'started',
        toolName:
          (item?.toolName as string) ??
          (itemPayload.toolName as string) ??
          (item?.name as string) ??
          'tool',
        toolCallId:
          (item?.toolCallId as string) ??
          (itemPayload.callId as string) ??
          (item?.callId as string) ??
          itemId,
        arguments:
          (item?.arguments as Record<string, unknown> | undefined) ??
          (itemPayload.arguments as Record<string, unknown> | undefined),
        createdAt: (item?.createdAt as string) ?? new Date().toISOString()
      }
      set((state) => ({
        turns: state.turns.map((t) =>
          t.id === turnId ? { ...t, items: sortItemsByCreatedAt([...t.items, newItem]) } : t
        )
      }))
    }
  },

  onAgentMessageDelta(delta) {
    set((state) => ({ streamingMessage: state.streamingMessage + delta }))
  },

  onReasoningDelta(delta) {
    set((state) => ({ streamingReasoning: state.streamingReasoning + delta }))
  },

  onItemCompleted(params) {
    const item = params.item as Record<string, unknown>
    const type = item?.type as string
    const turnId = params.turnId as string
    const state = get()

    if (type === 'agentMessage') {
      const itemId = (item?.id as string) ?? ''
      const alreadyCommitted = state.turns
        .find((t) => t.id === turnId)
        ?.items.some((i) => i.id === itemId && i.type === 'agentMessage' && i.status === 'completed')
      if (alreadyCommitted) {
        set({ streamingMessage: '', activeItemId: null })
        return
      }
      const finalText =
        state.streamingMessage || ((item?.text as string) ?? (item?.content as string) ?? '')
      set((s) => {
        const turn = s.turns.find((t) => t.id === turnId)
        if (!turn) {
          return { streamingMessage: '', activeItemId: null }
        }
        const hasPlaceholder = turn.items.some((i) => i.id === itemId && i.type === 'agentMessage')
        const completedAt = (item?.completedAt as string) ?? new Date().toISOString()
        const nextItems = hasPlaceholder
          ? turn.items.map((i) =>
              i.id === itemId && i.type === 'agentMessage'
                ? {
                    ...i,
                    status: 'completed' as const,
                    text: finalText,
                    completedAt
                  }
                : i
            )
          : sortItemsByCreatedAt([
              ...turn.items,
              {
                id: itemId,
                type: 'agentMessage' as const,
                status: 'completed' as const,
                text: finalText,
                createdAt: (item?.createdAt as string) ?? new Date().toISOString(),
                completedAt
              }
            ])
        return {
          turns: s.turns.map((t) =>
            t.id === turnId ? { ...t, items: sortItemsByCreatedAt(nextItems) } : t
          ),
          streamingMessage: '',
          activeItemId: null
        }
      })
    } else if (type === 'reasoningContent') {
      const itemId = (item?.id as string) ?? ''
      const alreadyCommitted = state.turns
        .find((t) => t.id === turnId)
        ?.items.some((i) => i.id === itemId && i.type === 'reasoningContent' && i.status === 'completed')
      if (alreadyCommitted) {
        set({ streamingReasoning: '', streamingReasoningStartedAt: null, activeItemId: null })
        return
      }
      const finalText =
        state.streamingReasoning || ((item?.text as string) ?? (item?.content as string) ?? '')
      const startedAt = state.streamingReasoningStartedAt
      const elapsed = startedAt ? Math.round((Date.now() - startedAt) / 1000) : 0
      const completedAt = (item?.completedAt as string) ?? new Date().toISOString()
      set((s) => {
        const turn = s.turns.find((t) => t.id === turnId)
        if (!turn) {
          return { streamingReasoning: '', streamingReasoningStartedAt: null, activeItemId: null }
        }
        const hasPlaceholder = turn.items.some((i) => i.id === itemId && i.type === 'reasoningContent')
        const nextItems = hasPlaceholder
          ? turn.items.map((i) =>
              i.id === itemId && i.type === 'reasoningContent'
                ? {
                    ...i,
                    status: 'completed' as const,
                    reasoning: finalText,
                    elapsedSeconds: elapsed,
                    completedAt
                  }
                : i
            )
          : sortItemsByCreatedAt([
              ...turn.items,
              {
                id: itemId,
                type: 'reasoningContent' as const,
                status: 'completed' as const,
                reasoning: finalText,
                elapsedSeconds: elapsed,
                createdAt: (item?.createdAt as string) ?? new Date().toISOString(),
                completedAt
              }
            ])
        return {
          turns: s.turns.map((t) =>
            t.id === turnId ? { ...t, items: sortItemsByCreatedAt(nextItems) } : t
          ),
          streamingReasoning: '',
          streamingReasoningStartedAt: null,
          activeItemId: null
        }
      })
    } else if (type === 'error') {
      const newItem: ConversationItem = {
        id: (item?.id as string) ?? '',
        type: 'error',
        status: 'completed',
        text: (item?.message as string) ?? (item?.text as string) ?? 'Unknown error',
        createdAt: (item?.createdAt as string) ?? new Date().toISOString(),
        completedAt: (item?.completedAt as string) ?? new Date().toISOString()
      }
      set((s) => ({
        turns: s.turns.map((t) =>
          t.id === turnId ? { ...t, items: sortItemsByCreatedAt([...t.items, newItem]) } : t
        )
      }))
    } else if (type === 'toolCall') {
      set((s) => ({
        turns: s.turns.map((t) =>
          t.id === turnId
            ? {
                ...t,
                items: sortItemsByCreatedAt(
                  t.items.map((i) =>
                    i.id === (item?.id as string)
                      ? { ...i, status: 'completed' as const, completedAt: (item?.completedAt as string) }
                      : i
                  )
                )
              }
            : t
        )
      }))
    } else if (type === 'toolResult') {
      const itemPayload = (item?.payload ?? {}) as Record<string, unknown>
      const callId =
        (item?.callId as string | undefined) ??
        (itemPayload.callId as string | undefined) ??
        (item?.toolCallId as string | undefined)
      const resultText =
        (item?.result as string | undefined) ??
        (itemPayload.result as string | undefined) ??
        (item?.text as string | undefined) ??
        ''
      const success = (item?.success as boolean | undefined) ?? (itemPayload.success as boolean | undefined) ?? true

      set((s) => ({
        turns: s.turns.map((t) => {
          if (t.id !== turnId) return t
          return {
            ...t,
            items: sortItemsByCreatedAt(
              t.items.map((i) => {
                if (i.type === 'toolCall' && i.toolCallId === callId) {
                  const startMs = i.createdAt ? new Date(i.createdAt).getTime() : Date.now()
                  const endMs = (item?.completedAt as string)
                    ? new Date(item.completedAt as string).getTime()
                    : Date.now()
                  return {
                    ...i,
                    status: 'completed' as const,
                    result: resultText,
                    success,
                    duration: endMs - startMs,
                    completedAt: (item?.completedAt as string) ?? new Date().toISOString()
                  }
                }
                return i
              })
            )
          }
        })
      }))
    }
  },

  onTurnCompleted(rawTurn) {
    const turn = wireTurnToConversationTurn(rawTurn)
    set((state) => ({
      turns: state.turns.map((t) =>
        t.id === turn.id
          ? {
              ...t,
              status: 'completed' as TurnStatus,
              completedAt: turn.completedAt,
              tokenUsage: turn.tokenUsage
            }
          : t
      ),
      turnStatus: 'idle',
      activeTurnId: null,
      streamingMessage: '',
      streamingReasoning: '',
      streamingReasoningStartedAt: null,
      streamingActive: false
    }))
  },

  onTurnFailed(rawTurn, error) {
    const turn = wireTurnToConversationTurn(rawTurn)
    set((state) => ({
      turns: state.turns.map((t) =>
        t.id === turn.id
          ? { ...t, status: 'failed' as TurnStatus, error, completedAt: turn.completedAt }
          : t
      ),
      turnStatus: 'idle',
      activeTurnId: null,
      streamingMessage: '',
      streamingReasoning: '',
      streamingReasoningStartedAt: null,
      streamingActive: false
    }))
  },

  onTurnCancelled(rawTurn, reason) {
    const turn = wireTurnToConversationTurn(rawTurn)
    set((state) => ({
      turns: state.turns.map((t) =>
        t.id === turn.id
          ? {
              ...t,
              status: 'cancelled' as TurnStatus,
              cancelReason: reason,
              completedAt: turn.completedAt
            }
          : t
      ),
      turnStatus: 'idle',
      activeTurnId: null,
      streamingMessage: '',
      streamingReasoning: '',
      streamingReasoningStartedAt: null,
      streamingActive: false
    }))
  },

  onSubagentProgress(entries) {
    set({ subAgentEntries: entries })
  }
}))

function mapWireTaskToAutomationTask(raw: Record<string, unknown>): AutomationTask {
  const statusRaw = String(raw.status ?? raw.Status ?? 'pending')
  const statusMap: Record<string, AutomationTask['status']> = {
    pending: 'pending',
    dispatched: 'dispatched',
    agent_running: 'agent_running',
    agent_completed: 'agent_completed',
    awaiting_review: 'awaiting_review',
    approved: 'approved',
    rejected: 'rejected',
    failed: 'failed'
  }
  const status = statusMap[statusRaw] ?? 'pending'

  const createdAt = raw.createdAt ?? raw.CreatedAt
  const updatedAt = raw.updatedAt ?? raw.UpdatedAt

  const approvalPolicy =
    (raw.approvalPolicy as string | undefined) ?? (raw.ApprovalPolicy as string | undefined) ?? null

  return {
    id: (raw.id as string) ?? (raw.Id as string) ?? (raw.taskId as string) ?? '',
    title: (raw.title as string) ?? (raw.Title as string) ?? '',
    status,
    sourceName: (raw.sourceName as string) ?? (raw.SourceName as string) ?? 'local',
    threadId: (raw.threadId as string | null) ?? (raw.ThreadId as string | null) ?? null,
    description: (raw.description as string | undefined) ?? (raw.Description as string | undefined),
    agentSummary:
      (raw.agentSummary as string | null) ??
      (raw.AgentSummary as string | null) ??
      null,
    approvalPolicy,
    createdAt: createdAt != null ? String(createdAt) : new Date().toISOString(),
    updatedAt: updatedAt != null ? String(updatedAt) : new Date().toISOString()
  }
}
