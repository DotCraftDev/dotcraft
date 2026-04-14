import { create } from 'zustand'
import type {
  ConversationTurn,
  ConversationItem,
  TurnStatus,
  ThreadMode,
  ApprovalDecision,
  ApprovalState
} from '../types/conversation'
import { wireTurnToConversationTurn } from '../types/conversation'
import { isShellToolName } from '../utils/shellTools'
import type { FileDiff, SubAgentEntry } from '../types/toolCall'
import {
  mergeFileDiffIncrement,
  computeCumulativeFileDiff,
  computeIncrementalPerItemDiff,
  parseResultPath
} from '../utils/diffExtractor'

// ---------------------------------------------------------------------------
// Plan types
// ---------------------------------------------------------------------------

export type PlanTodoStatus = 'pending' | 'in_progress' | 'completed' | 'cancelled'

export interface PlanTodoItem {
  id: string
  content: string
  status: PlanTodoStatus
}

export interface AgentPlan {
  title: string
  overview: string
  todos: PlanTodoItem[]
}

// ---------------------------------------------------------------------------
// State interface
// ---------------------------------------------------------------------------

export interface PendingApproval {
  /** Bridge ID needed to respond via IPC */
  bridgeId: string
  /** Item ID in the current turn's items list */
  itemId: string
  approvalType: 'shell' | 'file'
  operation: string
  target: string
  reason: string
}

interface ConversationState {
  turns: ConversationTurn[]
  turnStatus: 'idle' | 'running' | 'waitingApproval'
  /** Current active turn id (when running or waitingApproval) */
  activeTurnId: string | null
  /** Non-null when turnStatus === 'waitingApproval' */
  pendingApproval: PendingApproval | null
  /** Currently streaming agent message text */
  streamingMessage: string
  /** Currently streaming reasoning text */
  streamingReasoning: string
  /** Wall-clock ms when streaming reasoning started (for elapsed display) */
  streamingReasoningStartedAt: number | null
  /** ID of the item currently being streamed */
  activeItemId: string | null
  /** Wall-clock ms when the current turn started */
  turnStartedAt: number | null
  inputTokens: number
  outputTokens: number
  /** Transient system label: "Compacting context...", "Consolidating memory..." */
  systemLabel: string | null
  /** Queued follow-up message (sent when current turn completes) */
  pendingMessage: string | null
  /** Current agent operating mode */
  threadMode: ThreadMode
  /** Workspace root path (for cumulative diff disk reads) */
  workspacePath: string
  /** File diffs accumulated for the active thread (cross-turn), keyed by filePath */
  changedFiles: Map<string, FileDiff>
  /** Per tool-call item incremental diff (Detail Panel uses cumulative changedFiles) */
  itemDiffs: Map<string, FileDiff>
  /** Live SubAgent progress entries — replaced wholesale on each notification */
  subAgentEntries: SubAgentEntry[]
  /** Current agent plan from plan/updated events — replaced wholesale */
  plan: AgentPlan | null
}

// ---------------------------------------------------------------------------
// Actions interface
// ---------------------------------------------------------------------------

interface ConversationActions {
  /** Load full turn history from thread/read */
  setTurns(turns: ConversationTurn[] | Array<Record<string, unknown>>): void
  /** turn/started notification */
  onTurnStarted(rawTurn: Record<string, unknown>): void
  /** turn/completed notification */
  onTurnCompleted(rawTurn: Record<string, unknown>): void
  /** turn/failed notification */
  onTurnFailed(rawTurn: Record<string, unknown>, error: string): void
  /** turn/cancelled notification */
  onTurnCancelled(rawTurn: Record<string, unknown>, reason: string): void
  /** item/started notification */
  onItemStarted(params: Record<string, unknown>): void
  /** item/agentMessage/delta notification */
  onAgentMessageDelta(delta: string): void
  /** item/reasoning/delta notification */
  onReasoningDelta(delta: string): void
  /** item/commandExecution/outputDelta notification */
  onCommandExecutionDelta(params: { threadId?: string; turnId?: string; itemId?: string; delta?: string }): void
  /** item/completed notification */
  onItemCompleted(params: Record<string, unknown>): void
  /** item/usage/delta notification */
  onUsageDelta(inputTokens: number, outputTokens: number): void
  /** system/event notification */
  onSystemEvent(kind: string): void
  setPendingMessage(msg: string | null): void
  setThreadMode(mode: ThreadMode): void
  /** Add an optimistic (locally-created) turn before server confirms */
  addOptimisticTurn(turn: ConversationTurn): void
  /** Remove an optimistic turn on RPC failure */
  removeOptimisticTurn(turnId: string): void
  /**
   * Replace the optimistic client-only turn ID with the real server turn ID.
   * Called as soon as turn/start returns its response (before turn/started arrives).
   */
  promoteOptimisticTurn(localId: string, serverId: string): void
  /** Replace subagent entries snapshot from subagent/progress notification */
  onSubagentProgress(entries: SubAgentEntry[]): void
  /** Add or update a file diff entry in changedFiles */
  upsertChangedFile(diff: FileDiff): void
  /** Store incremental diff for one toolCall item (keyed by ConversationItem.id) */
  upsertItemDiff(itemId: string, diff: FileDiff): void
  /** Mark all files in a turn as reverted (M4: state-only, actual revert in M6) */
  revertFilesForTurn(turnId: string): void
  /** Mark a single file as reverted (state only; caller must write disk via IPC) */
  revertFile(filePath: string): void
  /** Mark a single file as written/re-applied (state only; caller must write disk via IPC) */
  reapplyFile(filePath: string): void
  /** Replace entire plan state from plan/updated notification */
  onPlanUpdated(plan: Partial<AgentPlan>): void
  /**
   * Called when AppServer sends item/approval/request (M5).
   * Adds an approvalCard item to the current turn and sets waitingApproval state.
   */
  onApprovalRequest(bridgeId: string, params: Record<string, unknown>): void
  /**
   * Called when the user makes a decision (M5).
   * Updates the approval item state locally; IPC response is sent by the caller.
   */
  onApprovalDecision(decision: ApprovalDecision): void
  /**
   * Called when item/approval/resolved notification arrives (M5).
   * Clears pendingApproval and restores turnStatus to 'running'.
   */
  onApprovalResolved(): void
  /**
   * Called when approval timeout error (-32020) is received (M5).
   * Updates the approval item to 'timedOut' state.
   */
  onApprovalTimeout(): void
  /** Set workspace path for file read IPC (call from App when path is known) */
  setWorkspacePath(path: string): void
  reset(): void
}

export interface ConversationStore extends ConversationState, ConversationActions {}

// ---------------------------------------------------------------------------
// Initial state
// ---------------------------------------------------------------------------

const initialState: ConversationState = {
  turns: [],
  turnStatus: 'idle',
  activeTurnId: null,
  pendingApproval: null,
  streamingMessage: '',
  streamingReasoning: '',
  streamingReasoningStartedAt: null,
  activeItemId: null,
  turnStartedAt: null,
  inputTokens: 0,
  outputTokens: 0,
  systemLabel: null,
  pendingMessage: null,
  threadMode: 'agent',
  workspacePath: '',
  changedFiles: new Map<string, FileDiff>(),
  itemDiffs: new Map<string, FileDiff>(),
  subAgentEntries: [],
  plan: null
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function toTurnStatus(raw: string): TurnStatus {
  if (raw === 'running' || raw === 'completed' || raw === 'failed' || raw === 'cancelled') {
    return raw
  }
  return 'completed'
}

/** Stable chronological order for turn items (Wire Protocol may interleave events). */
function sortItemsByCreatedAt(items: ConversationItem[]): ConversationItem[] {
  return [...items].sort(
    (a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()
  )
}

function mergeCommandExecutionIntoToolCall(
  item: ConversationItem,
  commandExecution: Partial<ConversationItem>
): ConversationItem {
  if (item.type !== 'toolCall') return item
  if (!isShellToolName(item.toolName)) return item
  if (!commandExecution.toolCallId || item.toolCallId !== commandExecution.toolCallId) return item

  return {
    ...item,
    aggregatedOutput: commandExecution.aggregatedOutput ?? item.aggregatedOutput,
    executionStatus: commandExecution.executionStatus ?? item.executionStatus,
    exitCode: commandExecution.exitCode ?? item.exitCode,
    commandSource: commandExecution.commandSource ?? item.commandSource,
    duration: commandExecution.duration ?? item.duration
  }
}

function mergeCommandExecutionAcrossItems(
  items: ConversationItem[],
  commandExecution: Partial<ConversationItem>
): ConversationItem[] {
  return items.map((i) => mergeCommandExecutionIntoToolCall(i, commandExecution))
}

const SYSTEM_LABELS: Record<string, string | null> = {
  compacting: 'Compacting context...',
  consolidating: 'Consolidating memory...',
  compacted: null,
  compactSkipped: null,
  consolidated: null
}

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------

export const useConversationStore = create<ConversationStore>((set, get) => ({
  ...initialState,

  setTurns(turns) {
    const converted = (turns as Array<Record<string, unknown>>).map((t) => {
      if (typeof (t as ConversationTurn).items !== 'undefined' && !Array.isArray((t as ConversationTurn).items)) {
        return wireTurnToConversationTurn(t)
      }
      // Already a ConversationTurn or has ConversationItem[] items
      if (Array.isArray((t as ConversationTurn).items) && (t as ConversationTurn).id) {
        return t as ConversationTurn
      }
      return wireTurnToConversationTurn(t)
    })

    // Rehydrate changedFiles from historical turns.
    // When a thread is loaded from history, the live wire events (onItemCompleted for
    // toolResult) never fire, so changedFiles is never populated. We reconstruct it
    // here by matching ToolResult items to their paired ToolCall items via callId,
    // merging result/success/duration back into the ToolCall, and extracting diffs
    // for WriteFile/EditFile calls.
    const rehydratedChangedFiles = new Map<string, FileDiff>()
    const rehydratedItemDiffs = new Map<string, FileDiff>()
    const rehydratedTurns = converted.map((turn) => {
      // Build a callId -> toolResult lookup for this turn
      const resultByCallId = new Map<string, ConversationItem>()
      const commandExecutionByCallId = new Map<string, ConversationItem>()
      for (const item of turn.items) {
        if (item.type === 'toolResult' && item.toolCallId) {
          resultByCallId.set(item.toolCallId, item)
        }
        if (item.type === 'commandExecution' && item.toolCallId) {
          commandExecutionByCallId.set(item.toolCallId, item)
        }
      }
      if (resultByCallId.size === 0 && commandExecutionByCallId.size === 0) return turn

      // Merge result data into toolCall items and extract diffs
      const mergedItems = turn.items.map((item) => {
        if (item.type !== 'toolCall') return item
        const resultItem = resultByCallId.get(item.toolCallId ?? '')
        const commandExecution = commandExecutionByCallId.get(item.toolCallId ?? '')
        if (!resultItem) {
          return commandExecution
            ? mergeCommandExecutionIntoToolCall(item, commandExecution)
            : item
        }

        const resultText = resultItem.result ?? ''
        const success = resultItem.success !== false
        const startMs = item.createdAt ? new Date(item.createdAt).getTime() : 0
        const endMs = resultItem.completedAt ? new Date(resultItem.completedAt).getTime() : startMs

        const merged: ConversationItem = {
          ...item,
          result: resultText,
          success,
          duration: endMs - startMs,
          completedAt: resultItem.completedAt
        }
        const mergedWithCommandExecution = commandExecution
          ? mergeCommandExecutionIntoToolCall(merged, commandExecution)
          : merged

        // Accumulate diffs for file-writing tools (same path may appear multiple times)
        if (item.arguments && (item.toolName === 'WriteFile' || item.toolName === 'EditFile')) {
          const fp =
            (item.arguments.path as string | undefined) ?? parseResultPath(resultText) ?? ''
          if (fp) {
            const existingBefore = rehydratedChangedFiles.get(fp)
            const perItem = computeIncrementalPerItemDiff(
              item.toolName as 'WriteFile' | 'EditFile',
              item.arguments,
              resultText,
              turn.id,
              existingBefore
            )
            if (perItem) {
              rehydratedItemDiffs.set(item.id, perItem)
            }
            const mergedDiff = mergeFileDiffIncrement(
              rehydratedChangedFiles.get(fp),
              item.toolName as 'WriteFile' | 'EditFile',
              item.arguments,
              resultText,
              turn.id
            )
            if (mergedDiff) {
              rehydratedChangedFiles.set(mergedDiff.filePath, mergedDiff)
            }
          }
        }

        return mergedWithCommandExecution
      })

      return { ...turn, items: mergedItems }
    })

    // If the loaded history contains a still-running turn (e.g. switching back to a thread
    // that had an active turn), restore running state so ESC and the Stop button still work.
    const runningTurn = rehydratedTurns.find((t) => t.status === 'running')
    set({
      turns: rehydratedTurns,
      turnStatus: runningTurn ? 'running' : 'idle',
      activeTurnId: runningTurn ? runningTurn.id : null,
      turnStartedAt: runningTurn
        ? (runningTurn.startedAt ? new Date(runningTurn.startedAt).getTime() : Date.now())
        : null,
      changedFiles: rehydratedChangedFiles,
      itemDiffs: rehydratedItemDiffs
    })
  },

  onTurnStarted(rawTurn) {
    const turn = wireTurnToConversationTurn(rawTurn)
    set((state) => {
      // Guard: if this turn ID already exists (was promoted via promoteOptimisticTurn before
      // the notification arrived), update in-place to avoid creating a duplicate key.
      const alreadyExists = state.turns.find((t) => t.id === turn.id)
      if (alreadyExists) {
        return {
          turns: state.turns.map((t) =>
            t.id === turn.id ? { ...t, status: 'running', startedAt: turn.startedAt } : t
          ),
          turnStatus: 'running',
          activeTurnId: turn.id,
          pendingApproval: null,
          streamingMessage: '',
          streamingReasoning: '',
          streamingReasoningStartedAt: null,
          activeItemId: null,
          turnStartedAt: Date.now(),
          inputTokens: 0,
          outputTokens: 0,
          systemLabel: null,
          subAgentEntries: []
        }
      }

      // Replace the most recent optimistic turn (local-turn-*) with the real turn,
      // preserving the user message items that were added optimistically.
      const lastOptimistic = [...state.turns].reverse().find((t) => t.id.startsWith('local-turn-'))
      let nextTurns: ConversationTurn[]
      if (lastOptimistic) {
        // Merge: keep user message items from the optimistic turn, add server items if any
        const mergedItems = [
          ...lastOptimistic.items.filter((i) => i.type === 'userMessage'),
          ...turn.items.filter((i) => i.type !== 'userMessage')
        ]
        nextTurns = state.turns.map((t) =>
          t.id === lastOptimistic.id ? { ...turn, items: mergedItems } : t
        )
      } else {
        nextTurns = [...state.turns, turn]
      }
      return {
        turns: nextTurns,
        turnStatus: 'running',
        activeTurnId: turn.id,
        pendingApproval: null,
        streamingMessage: '',
        streamingReasoning: '',
        streamingReasoningStartedAt: null,
        activeItemId: null,
        turnStartedAt: Date.now(),
        inputTokens: 0,
        outputTokens: 0,
        systemLabel: null,
        subAgentEntries: []
      }
    })
  },

  onTurnCompleted(rawTurn) {
    const turn = wireTurnToConversationTurn(rawTurn)
    set((state) => {
      const pending = state.pendingMessage
      return {
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
        turnStartedAt: null,
        systemLabel: null,
        // Auto-send pending message after clearing it
        pendingMessage: null
      }
    })
    // pendingMessage was already cleared in the set() above.
    // App.tsx reads conv.pendingMessage BEFORE calling onTurnCompleted,
    // so pending message auto-send is handled there, not here.
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
      turnStartedAt: null,
      systemLabel: null
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
      turnStartedAt: null,
      systemLabel: null
    }))
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
    } else if (type === 'toolCall' || type === 'externalChannelToolCall') {
      // Extract nested payload for toolCall items (wire protocol: item.payload.{toolName,callId,arguments})
      const itemPayload = (item?.payload ?? {}) as Record<string, unknown>
      // Add a started tool call item to the active turn, storing arguments for diff extraction
      const newItem: ConversationItem = {
        id: itemId ?? '',
        type: type as 'toolCall' | 'externalChannelToolCall',
        status: 'started',
        toolName: (item?.toolName as string) ?? (itemPayload.toolName as string) ?? (item?.name as string) ?? 'tool',
        toolCallId: (item?.toolCallId as string) ?? (itemPayload.callId as string) ?? (item?.callId as string) ?? itemId,
        arguments: (item?.arguments as Record<string, unknown> | undefined)
          ?? (itemPayload.arguments as Record<string, unknown> | undefined),
        toolChannelName: (itemPayload.channelName as string | undefined),
        createdAt: (item?.createdAt as string) ?? new Date().toISOString()
      }
      set((state) => ({
        turns: state.turns.map((t) =>
          t.id === turnId ? { ...t, items: sortItemsByCreatedAt([...t.items, newItem]) } : t
        )
      }))
    } else if (type === 'commandExecution') {
      const itemPayload = (item?.payload ?? {}) as Record<string, unknown>
      const newItem: ConversationItem = {
        id: itemId ?? '',
        type: 'commandExecution',
        status: 'started',
        command: (item?.command as string | undefined) ?? (itemPayload.command as string | undefined) ?? '',
        workingDirectory: (item?.workingDirectory as string | undefined)
          ?? (itemPayload.workingDirectory as string | undefined),
        commandSource: (item?.source as 'host' | 'sandbox' | undefined)
          ?? (itemPayload.source as 'host' | 'sandbox' | undefined),
        aggregatedOutput: (item?.aggregatedOutput as string | undefined)
          ?? (itemPayload.aggregatedOutput as string | undefined)
          ?? '',
        exitCode: (item?.exitCode as number | null | undefined)
          ?? (itemPayload.exitCode as number | null | undefined),
        // Wire item.status is item lifecycle (started/completed), not shell execution state.
        // Prefer payload.status (inProgress/completed/failed/cancelled).
        executionStatus: (itemPayload.status as ConversationItem['executionStatus'] | undefined)
          ?? 'inProgress',
        toolCallId: (item?.callId as string | undefined)
          ?? (itemPayload.callId as string | undefined),
        createdAt: (item?.createdAt as string) ?? new Date().toISOString()
      }
      set((state) => ({
        turns: state.turns.map((t) =>
          t.id !== turnId
            ? t
            : {
                ...t,
                items: sortItemsByCreatedAt(
                  mergeCommandExecutionAcrossItems([...t.items, newItem], newItem)
                )
              }
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

  onCommandExecutionDelta(params) {
    const turnId = params.turnId ?? ''
    const itemId = params.itemId ?? ''
    const delta = params.delta ?? ''
    if (!turnId || !itemId || !delta) return

    set((state) => ({
      turns: state.turns.map((t) =>
        t.id !== turnId
          ? t
          : {
              ...t,
              items: sortItemsByCreatedAt((() => {
                const updatedItems = t.items.map((i) =>
                  i.id === itemId && i.type === 'commandExecution'
                    ? {
                        ...i,
                        aggregatedOutput: (i.aggregatedOutput ?? '') + delta
                      }
                    : i
                )
                const commandExecution = updatedItems.find((i) => i.id === itemId && i.type === 'commandExecution')
                return commandExecution
                  ? mergeCommandExecutionAcrossItems(updatedItems, commandExecution)
                  : updatedItems
              })())
            }
      )
    }))
  },

  onItemCompleted(params) {
    const item = params.item as Record<string, unknown>
    const type = item?.type as string
    const turnId = params.turnId as string
    const state = get()

    if (type === 'agentMessage') {
      const itemId = (item?.id as string) ?? ''
      // Deduplicate: skip if this item was already completed (streaming placeholder shares the same id)
      const alreadyCommitted = state.turns
        .find((t) => t.id === turnId)
        ?.items.some((i) => i.id === itemId && i.type === 'agentMessage' && i.status === 'completed')
      if (alreadyCommitted) {
        // Still clear streaming state even if we skip the item
        set({ streamingMessage: '', activeItemId: null })
        return
      }
      const finalText = state.streamingMessage || ((item?.text as string) ?? (item?.content as string) ?? '')
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
      const finalText = state.streamingReasoning || ((item?.text as string) ?? (item?.content as string) ?? '')
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
      // Mark the tool call item itself as completed
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
    } else if (type === 'commandExecution') {
      const itemPayload = (item?.payload ?? {}) as Record<string, unknown>
      set((s) => ({
        turns: s.turns.map((t) =>
          t.id !== turnId
            ? t
            : {
                ...t,
                items: sortItemsByCreatedAt((() => {
                  const updatedItems = t.items.map((i) => {
                    if (i.id !== (item?.id as string) || i.type !== 'commandExecution') return i
                    const startMs = i.createdAt ? new Date(i.createdAt).getTime() : Date.now()
                    const endMs = (item?.completedAt as string)
                      ? new Date(item.completedAt as string).getTime()
                      : Date.now()
                    return {
                      ...i,
                      status: 'completed' as const,
                      command: (item?.command as string | undefined)
                        ?? (itemPayload.command as string | undefined)
                        ?? i.command,
                      workingDirectory: (item?.workingDirectory as string | undefined)
                        ?? (itemPayload.workingDirectory as string | undefined)
                        ?? i.workingDirectory,
                      commandSource: (item?.source as 'host' | 'sandbox' | undefined)
                        ?? (itemPayload.source as 'host' | 'sandbox' | undefined)
                        ?? i.commandSource,
                      aggregatedOutput: (item?.aggregatedOutput as string | undefined)
                        ?? (itemPayload.aggregatedOutput as string | undefined)
                        ?? i.aggregatedOutput
                        ?? '',
                      exitCode: (item?.exitCode as number | null | undefined)
                        ?? (itemPayload.exitCode as number | null | undefined)
                        ?? i.exitCode,
                      executionStatus: (itemPayload.status as ConversationItem['executionStatus'] | undefined)
                        ?? i.executionStatus
                        ?? 'completed',
                      toolCallId: (item?.callId as string | undefined)
                        ?? (itemPayload.callId as string | undefined)
                        ?? i.toolCallId,
                      duration: (itemPayload.durationMs as number | undefined) ?? (endMs - startMs),
                      completedAt: (item?.completedAt as string) ?? new Date().toISOString()
                    }
                  })
                  const commandExecution = updatedItems.find(
                    (i) => i.id === (item?.id as string) && i.type === 'commandExecution'
                  )
                  return commandExecution
                    ? mergeCommandExecutionAcrossItems(updatedItems, commandExecution)
                    : updatedItems
                })())
              }
        )
      }))
    } else if (type === 'externalChannelToolCall') {
      const itemPayload = (item?.payload ?? {}) as Record<string, unknown>
      set((s) => ({
        turns: s.turns.map((t) =>
          t.id !== turnId
            ? t
            : {
                ...t,
                items: sortItemsByCreatedAt(
                  t.items.map((i) => {
                    if (i.id !== (item?.id as string)) return i
                    const startMs = i.createdAt ? new Date(i.createdAt).getTime() : Date.now()
                    const endMs = (item?.completedAt as string)
                      ? new Date(item.completedAt as string).getTime()
                      : Date.now()
                    return {
                      ...i,
                      status: 'completed' as const,
                      result: (itemPayload.result as string | undefined) ?? i.result,
                      success: (itemPayload.success as boolean | undefined) ?? true,
                      toolChannelName: (itemPayload.channelName as string | undefined) ?? i.toolChannelName,
                      duration: endMs - startMs,
                      completedAt: (item?.completedAt as string) ?? new Date().toISOString()
                    }
                  })
                )
              }
        )
      }))
    } else if (type === 'toolResult') {
      // Extract nested payload for toolResult items (wire protocol: item.payload.{callId,result,success})
      const itemPayload = (item?.payload ?? {}) as Record<string, unknown>
      // Find the matching toolCall item by callId to update with result data
      const callId = (item?.callId as string | undefined)
        ?? (itemPayload.callId as string | undefined)
        ?? (item?.toolCallId as string | undefined)
      const resultText = (item?.result as string | undefined)
        ?? (itemPayload.result as string | undefined)
        ?? (item?.text as string | undefined)
        ?? ''
      const success = (item?.success as boolean | undefined)
        ?? (itemPayload.success as boolean | undefined)
        ?? true

      set((s) => {
        const nextTurns = s.turns.map((t) => {
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

        return { turns: nextTurns }
      })

      // Cumulative diff (async — may read disk); requires workspace path for IPC
      const matchedCallItem = get()
        .turns.find((t) => t.id === turnId)
        ?.items.find((i) => i.type === 'toolCall' && i.toolCallId === callId)
      const toolName = matchedCallItem?.toolName ?? ''
      const args = matchedCallItem?.arguments
      const wsPath = get().workspacePath
      if (args && (toolName === 'WriteFile' || toolName === 'EditFile')) {
        const fp = (args.path as string | undefined) ?? parseResultPath(resultText) ?? ''
        if (fp && matchedCallItem?.id) {
          const existingBeforeCumulative = get().changedFiles.get(fp)
          const incremental = computeIncrementalPerItemDiff(
            toolName as 'WriteFile' | 'EditFile',
            args,
            resultText,
            turnId,
            existingBeforeCumulative
          )
          if (incremental) {
            useConversationStore.getState().upsertItemDiff(matchedCallItem.id, incremental)
          }
          if (wsPath) {
            void computeCumulativeFileDiff({
              filePath: fp,
              toolName,
              args,
              resultText,
              turnId,
              existing: existingBeforeCumulative,
              workspacePath: wsPath
            }).then((diff) => {
              if (diff) {
                useConversationStore.getState().upsertChangedFile(diff)
              }
            })
          }
        }
      }
    }
  },

  onUsageDelta(inputTokens, outputTokens) {
    set((state) => ({
      inputTokens: state.inputTokens + inputTokens,
      outputTokens: state.outputTokens + outputTokens
    }))
  },

  onSystemEvent(kind) {
    const label = SYSTEM_LABELS[kind]
    // undefined means unknown event, don't change label; null means clear it
    if (label !== undefined) {
      set({ systemLabel: label })
    }
  },

  setPendingMessage(msg) {
    set({ pendingMessage: msg })
  },

  setThreadMode(mode) {
    set({ threadMode: mode })
  },

  addOptimisticTurn(turn) {
    set((state) => ({
      turns: [...state.turns, turn],
      turnStatus: 'running',
      activeTurnId: turn.id,
      streamingMessage: '',
      streamingReasoning: '',
      streamingReasoningStartedAt: null,
      activeItemId: null,
      turnStartedAt: Date.now(),
      inputTokens: 0,
      outputTokens: 0,
      systemLabel: null
    }))
  },

  removeOptimisticTurn(turnId) {
    set((state) => ({
      turns: state.turns.filter((t) => t.id !== turnId),
      turnStatus: 'idle',
      activeTurnId: null
    }))
  },

  promoteOptimisticTurn(localId, serverId) {
    set((state) => ({
      turns: state.turns.map((t) => (t.id === localId ? { ...t, id: serverId } : t)),
      activeTurnId: state.activeTurnId === localId ? serverId : state.activeTurnId
    }))
  },

  onSubagentProgress(entries) {
    set({ subAgentEntries: entries })
  },

  upsertChangedFile(diff) {
    set((state) => {
      const next = new Map(state.changedFiles)
      next.set(diff.filePath, diff)
      return { changedFiles: next }
    })
  },

  upsertItemDiff(itemId, diff) {
    set((state) => {
      const next = new Map(state.itemDiffs)
      next.set(itemId, diff)
      return { itemDiffs: next }
    })
  },

  revertFilesForTurn(turnId) {
    set((state) => {
      const next = new Map(state.changedFiles)
      for (const [key, entry] of next.entries()) {
        const ids = entry.turnIds?.length ? entry.turnIds : [entry.turnId]
        if (ids.includes(turnId)) {
          next.set(key, { ...entry, status: 'reverted' })
        }
      }
      return { changedFiles: next }
    })
  },

  setWorkspacePath(path) {
    set({ workspacePath: path })
  },

  onApprovalRequest(bridgeId, params) {
    const state = get()
    const turnId = state.activeTurnId
    if (!turnId) return

    const approvalType = (params.approvalType as 'shell' | 'file') ?? 'shell'
    const operation = (params.operation as string) ?? ''
    const target = (params.target as string) ?? ''
    const reason = (params.reason as string) ?? ''
    const itemId = `approval-${bridgeId}`

    const approvalItem: ConversationItem = {
      id: itemId,
      type: 'approvalCard',
      status: 'completed',
      approvalType,
      approvalOperation: operation,
      approvalTarget: target,
      approvalReason: reason,
      approvalState: 'pending',
      createdAt: new Date().toISOString()
    }

    set((s) => ({
      turns: s.turns.map((t) =>
        t.id === turnId ? { ...t, items: sortItemsByCreatedAt([...t.items, approvalItem]) } : t
      ),
      turnStatus: 'waitingApproval',
      pendingApproval: { bridgeId, itemId, approvalType, operation, target, reason }
    }))
  },

  onApprovalDecision(decision) {
    const state = get()
    const pending = state.pendingApproval
    if (!pending) return

    const decisionToState: Record<ApprovalDecision, ApprovalState> = {
      accept: 'accepted',
      acceptForSession: 'acceptedForSession',
      acceptAlways: 'acceptedAlways',
      decline: 'declined',
      cancel: 'cancelled'
    }
    const newState = decisionToState[decision]

    set((s) => ({
      turns: s.turns.map((t) => ({
        ...t,
        items: t.items.map((i) =>
          i.id === pending.itemId ? { ...i, approvalState: newState } : i
        )
      }))
    }))
  },

  onApprovalResolved() {
    set({ pendingApproval: null, turnStatus: 'running' })
  },

  onApprovalTimeout() {
    const state = get()
    const pending = state.pendingApproval
    if (!pending) return

    set((s) => ({
      turns: s.turns.map((t) => ({
        ...t,
        items: t.items.map((i) =>
          i.id === pending.itemId ? { ...i, approvalState: 'timedOut' as ApprovalState } : i
        )
      })),
      pendingApproval: null
    }))
  },

  revertFile(filePath) {
    set((state) => {
      const entry = state.changedFiles.get(filePath)
      if (!entry) return {}
      const next = new Map(state.changedFiles)
      next.set(filePath, { ...entry, status: 'reverted' })
      return { changedFiles: next }
    })
  },

  reapplyFile(filePath) {
    set((state) => {
      const entry = state.changedFiles.get(filePath)
      if (!entry) return {}
      const next = new Map(state.changedFiles)
      next.set(filePath, { ...entry, status: 'written' })
      return { changedFiles: next }
    })
  },

  onPlanUpdated(rawPlan) {
    const plan: AgentPlan = {
      title: (rawPlan.title as string) ?? '',
      overview: (rawPlan.overview as string) ?? '',
      todos: (rawPlan.todos as PlanTodoItem[]) ?? []
    }
    set({ plan })
  },

  reset() {
    set((state) => ({
      ...initialState,
      workspacePath: state.workspacePath,
      changedFiles: new Map<string, FileDiff>(),
      itemDiffs: new Map<string, FileDiff>(),
      subAgentEntries: [],
      pendingApproval: null,
      plan: null
    }))
  }
}))

// Expose store to E2E / debug tooling via a window global (browser only)
if (typeof window !== 'undefined') {
  ;(window as unknown as Record<string, unknown>).__CONVERSATION_STORE_STATE = () =>
    useConversationStore.getState()
}
