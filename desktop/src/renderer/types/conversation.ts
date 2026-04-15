/**
 * Conversation-level types for M3.
 * These expand on the minimal Turn/Item stubs used in M2 and map directly
 * to the AppServer Wire Protocol payloads (specs/appserver-protocol.md Section 6).
 */

export type TurnStatus = 'running' | 'completed' | 'failed' | 'cancelled'

/** UI-only extended turn status that includes approval wait state */
export type TurnStatusExtended = TurnStatus | 'waitingApproval'

export type ItemType =
  | 'userMessage'
  | 'agentMessage'
  | 'reasoningContent'
  | 'commandExecution'
  | 'toolCall'
  | 'externalChannelToolCall'
  | 'toolResult'
  | 'error'
  | 'approvalCard'

export type ApprovalDecision =
  | 'accept'
  | 'acceptForSession'
  | 'acceptAlways'
  | 'decline'
  | 'cancel'

export type ApprovalState =
  | 'pending'
  | 'accepted'
  | 'acceptedForSession'
  | 'acceptedAlways'
  | 'declined'
  | 'cancelled'
  | 'timedOut'

export type ItemStatus = 'started' | 'streaming' | 'completed'

/**
 * A single item within a turn.
 * Uses optional discriminated fields rather than a full union to keep
 * rendering code straightforward when mapping wire payloads.
 */
export interface ConversationItem {
  id: string
  type: ItemType
  status: ItemStatus
  /** Primary text content: userMessage text, agentMessage markdown, error message */
  text?: string
  /** Optimistic-only: data URLs for user-attached images (not persisted by server) */
  imageDataUrls?: string[]
  /** Reasoning text for reasoningContent items */
  reasoning?: string
  /** Tool name for toolCall items */
  toolName?: string
  /** Correlation ID between toolCall and toolResult */
  toolCallId?: string
  /** Shell command text for commandExecution items */
  command?: string
  /** Working directory for commandExecution items */
  workingDirectory?: string
  /** Runtime source for commandExecution items */
  commandSource?: 'host' | 'sandbox'
  /** Aggregated command output for commandExecution items */
  aggregatedOutput?: string
  /** Exit code for commandExecution items */
  exitCode?: number | null
  /** Runtime status for commandExecution items */
  executionStatus?: 'inProgress' | 'completed' | 'failed' | 'cancelled'
  /** Tool call arguments from item/started payload for toolCall items */
  arguments?: Record<string, unknown>
  /** Raw incremental tool-call arguments JSON from item/toolCall/argumentsDelta */
  argumentsPreview?: string
  /** Extracted partial file content preview while WriteFile/EditFile is streaming */
  streamingFileContent?: string
  /** External adapter channel name for externalChannelToolCall items */
  toolChannelName?: string
  /** Tool result text updated on item/completed (toolResult) */
  result?: string
  /** Whether the tool succeeded updated on item/completed (toolResult) */
  success?: boolean
  /** Duration in milliseconds from tool start to completion */
  duration?: number
  createdAt: string
  completedAt?: string
  /** Elapsed seconds from createdAt to completedAt (reasoning indicator) */
  elapsedSeconds?: number
  /** Approval card fields for approvalCard items */
  approvalType?: 'shell' | 'file'
  approvalOperation?: string
  approvalTarget?: string
  approvalReason?: string
  approvalState?: ApprovalState
}

export interface ConversationTurn {
  id: string
  threadId: string
  status: TurnStatus
  items: ConversationItem[]
  startedAt: string
  completedAt?: string
  tokenUsage?: { inputTokens: number; outputTokens: number }
  /** Error message set when status === 'failed' */
  error?: string
  /** Reason set when status === 'cancelled' */
  cancelReason?: string
}

/** Supported input part types for turn/start */
export type InputPart =
  | { type: 'text'; text: string }
  | { type: 'localImage'; path: string }

/** User-attached image in the composer (temp file + preview) */
export interface ImageAttachment {
  tempPath: string
  dataUrl: string
  fileName: string
  mimeType: string
}

/** Agent operating mode */
export type ThreadMode = 'agent' | 'plan'

/**
 * Converts a raw wire Turn object (from thread/read or turn/started) into
 * ConversationTurn. Wire items use camelCase property names.
 *
 * The AppServer wraps item content inside a nested `payload` object:
 *   { type: "agentMessage", payload: { text: "..." } }
 * This function falls back to payload fields so that both the flat (legacy/streaming)
 * and nested (thread/read history) shapes are handled correctly.
 */
export function wireItemToConversationItem(raw: Record<string, unknown>): ConversationItem {
  const type = (raw.type as ItemType) ?? 'agentMessage'
  const payload = (raw.payload ?? {}) as Record<string, unknown>
  return {
    id: (raw.id as string) ?? '',
    type,
    status: 'completed',
    text: (raw.text as string | undefined)
      ?? (payload.text as string | undefined)
      ?? (raw.content as string | undefined)
      ?? (payload.message as string | undefined),
    reasoning: (raw.reasoning as string | undefined)
      ?? (type === 'reasoningContent' ? (payload.text as string | undefined) : undefined)
      ?? (raw.content as string | undefined),
    toolName: (raw.toolName as string | undefined)
      ?? (payload.toolName as string | undefined)
      ?? (raw.name as string | undefined),
    toolCallId: (raw.toolCallId as string | undefined)
      ?? (payload.callId as string | undefined)
      ?? (raw.callId as string | undefined),
    command: (raw.command as string | undefined)
      ?? (payload.command as string | undefined),
    workingDirectory: (raw.workingDirectory as string | undefined)
      ?? (payload.workingDirectory as string | undefined),
    commandSource: (raw.commandSource as 'host' | 'sandbox' | undefined)
      ?? (payload.source as 'host' | 'sandbox' | undefined),
    aggregatedOutput: (raw.aggregatedOutput as string | undefined)
      ?? (payload.aggregatedOutput as string | undefined),
    exitCode: (raw.exitCode as number | null | undefined)
      ?? (payload.exitCode as number | null | undefined),
    executionStatus: (raw.executionStatus as ConversationItem['executionStatus'] | undefined)
      ?? (payload.status as ConversationItem['executionStatus'] | undefined),
    arguments: (raw.arguments as Record<string, unknown> | undefined)
      ?? (payload.arguments as Record<string, unknown> | undefined),
    result: (raw.result as string | undefined)
      ?? (payload.result as string | undefined),
    success: (raw.success as boolean | undefined)
      ?? (payload.success as boolean | undefined),
    createdAt: (raw.createdAt as string) ?? new Date().toISOString(),
    completedAt: (raw.completedAt as string | undefined)
  }
}

/** Convert a raw wire Turn into a ConversationTurn */
export function wireTurnToConversationTurn(raw: Record<string, unknown>): ConversationTurn {
  const rawItems = Array.isArray(raw.items) ? (raw.items as Record<string, unknown>[]) : []
  return {
    id: (raw.id as string) ?? '',
    threadId: (raw.threadId as string) ?? '',
    status: (raw.status as TurnStatus) ?? 'completed',
    items: rawItems.map(wireItemToConversationItem),
    startedAt: (raw.startedAt as string) ?? new Date().toISOString(),
    completedAt: (raw.completedAt as string | undefined),
    tokenUsage: raw.tokenUsage as ConversationTurn['tokenUsage'],
    error: (raw.error as string | undefined),
    cancelReason: (raw.reason as string | undefined)
  }
}
