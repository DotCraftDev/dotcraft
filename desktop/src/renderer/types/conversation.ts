import type { SubAgentEntry } from './toolCall'

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
  | 'systemNotice'

/**
 * Payload for systemNotice items. Currently only the `compacted` kind is
 * emitted by the backend; the optional fields below are populated for that
 * kind and mirror `SystemNoticePayload` on the wire.
 */
export interface SystemNoticeInfo {
  kind: string
  trigger?: string
  mode?: string
  tokensBefore?: number
  tokensAfter?: number
  percentLeftAfter?: number
  clearedToolResults?: number
}

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
  /** Native user input parts used as the source of truth for history rendering. */
  nativeInputParts?: InputPart[]
  /** Materialized user input parts that were actually sent to the model. */
  materializedInputParts?: InputPart[]
  /** Optimistic-only: data URLs for user-attached images (not persisted by server) */
  imageDataUrls?: string[]
  /** Persisted local image metadata for user messages (rehydrated via thread/read). */
  images?: UserMessageImageRef[]
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
  approvalType?: 'shell' | 'file' | 'remoteResource'
  approvalOperation?: string
  approvalTarget?: string
  approvalReason?: string
  approvalState?: ApprovalState
  /**
   * When set on a userMessage item, indicates the message was synthesized by an
   * automation mechanism (heartbeat, cron, automation) rather than typed by a
   * human. Mirrors UserMessagePayload.TriggerKind on the server.
   */
  triggerKind?: 'heartbeat' | 'cron' | 'automation'
  /** Optional human-readable label for the automation source (e.g. cron job name). */
  triggerLabel?: string
  /** Optional routing id for client-side click-through (e.g. cron job id, task id). */
  triggerRefId?: string
  /** Populated only for systemNotice items (e.g. context-compacted markers). */
  systemNotice?: SystemNoticeInfo
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
  /** Final subagent snapshot for this turn (used by inline summary rendering) */
  subAgentEntries?: SubAgentEntry[]
}

/** Supported input part types for turn/start */
export type InputPart =
  | { type: 'text'; text: string }
  | { type: 'commandRef'; name: string; argsText?: string; rawText?: string }
  | { type: 'skillRef'; name: string }
  | { type: 'fileRef'; path: string; displayPath?: string }
  | { type: 'image'; url: string }
  | { type: 'localImage'; path: string; mimeType?: string; fileName?: string }

export interface ComposerFileAttachment {
  path: string
  fileName: string
}

export interface PendingComposerMessage {
  text: string
  inputParts?: InputPart[]
  files?: ComposerFileAttachment[]
}

export interface QueuedTurnInput {
  id: string
  threadId: string
  nativeInputParts?: InputPart[]
  materializedInputParts?: InputPart[]
  displayText: string
  status: string
  createdAt: string
  readyAfterTurnId?: string | null
}

export interface UserMessageImageRef {
  path: string
  mimeType?: string
  fileName?: string
}

/** User-attached image in the composer (temp file + preview) */
export interface ImageAttachment {
  tempPath: string
  dataUrl: string
  fileName: string
  mimeType: string
}

/** Agent operating mode */
export type ThreadMode = 'agent' | 'plan'

function mapInputPart(raw: unknown): InputPart | null {
  if (raw == null || typeof raw !== 'object') return null
  const part = raw as Record<string, unknown>
  const type = typeof part.type === 'string' ? part.type : ''
  switch (type) {
    case 'text': {
      const text = typeof part.text === 'string' ? part.text : ''
      return { type: 'text', text }
    }
    case 'commandRef': {
      const name = typeof part.name === 'string' ? part.name.trim() : ''
      if (!name) return null
      const argsText = typeof part.argsText === 'string' && part.argsText.trim() !== ''
        ? part.argsText
        : undefined
      const rawText = typeof part.rawText === 'string' && part.rawText.trim() !== ''
        ? part.rawText
        : undefined
      return { type: 'commandRef', name, ...(argsText ? { argsText } : {}), ...(rawText ? { rawText } : {}) }
    }
    case 'skillRef': {
      const name = typeof part.name === 'string' ? part.name.trim() : ''
      return name ? { type: 'skillRef', name } : null
    }
    case 'fileRef': {
      const path = typeof part.path === 'string' ? part.path.trim() : ''
      if (!path) return null
      const displayPath = typeof part.displayPath === 'string' && part.displayPath.trim() !== ''
        ? part.displayPath
        : undefined
      return { type: 'fileRef', path, ...(displayPath ? { displayPath } : {}) }
    }
    case 'image': {
      const url = typeof part.url === 'string' ? part.url.trim() : ''
      return url ? { type: 'image', url } : null
    }
    case 'localImage': {
      const path = typeof part.path === 'string' ? part.path.trim() : ''
      if (!path) return null
      const mimeType = typeof part.mimeType === 'string' && part.mimeType.trim() !== ''
        ? part.mimeType
        : undefined
      const fileName = typeof part.fileName === 'string' && part.fileName.trim() !== ''
        ? part.fileName
        : undefined
      return { type: 'localImage', path, ...(mimeType ? { mimeType } : {}), ...(fileName ? { fileName } : {}) }
    }
    default:
      return null
  }
}

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
  const payloadImagesRaw = (raw.images ?? payload.images) as unknown
  const payloadNativeInputPartsRaw = (raw.nativeInputParts ?? payload.nativeInputParts) as unknown
  const payloadMaterializedInputPartsRaw =
    (raw.materializedInputParts ?? payload.materializedInputParts) as unknown
  const payloadImages = Array.isArray(payloadImagesRaw)
    ? payloadImagesRaw
      .map((entry) => {
        if (entry == null || typeof entry !== 'object') return null
        const obj = entry as Record<string, unknown>
        const path = typeof obj.path === 'string' ? obj.path.trim() : ''
        if (!path) return null
        const mimeType = typeof obj.mimeType === 'string' ? obj.mimeType.trim() : ''
        const fileName = typeof obj.fileName === 'string' ? obj.fileName.trim() : ''
        return {
          path,
          ...(mimeType ? { mimeType } : {}),
          ...(fileName ? { fileName } : {})
        } satisfies UserMessageImageRef
      })
      .filter((img): img is UserMessageImageRef => img != null)
    : undefined
  const payloadNativeInputParts = Array.isArray(payloadNativeInputPartsRaw)
    ? payloadNativeInputPartsRaw.map(mapInputPart).filter((part): part is InputPart => part != null)
    : undefined
  const payloadMaterializedInputParts = Array.isArray(payloadMaterializedInputPartsRaw)
    ? payloadMaterializedInputPartsRaw.map(mapInputPart).filter((part): part is InputPart => part != null)
    : undefined
  return {
    id: (raw.id as string) ?? '',
    type,
    status: 'completed',
    text: (raw.text as string | undefined)
      ?? (payload.text as string | undefined)
      ?? (raw.content as string | undefined)
      ?? (payload.message as string | undefined),
    nativeInputParts: payloadNativeInputParts,
    materializedInputParts: payloadMaterializedInputParts,
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
    images: payloadImages,
    triggerKind: normalizeTriggerKind(
      (raw.triggerKind as unknown) ?? (payload.triggerKind as unknown)
    ),
    triggerLabel: (raw.triggerLabel as string | undefined)
      ?? (payload.triggerLabel as string | undefined),
    triggerRefId: (raw.triggerRefId as string | undefined)
      ?? (payload.triggerRefId as string | undefined),
    systemNotice: type === 'systemNotice' ? mapSystemNotice(raw, payload) : undefined,
    createdAt: (raw.createdAt as string) ?? new Date().toISOString(),
    completedAt: (raw.completedAt as string | undefined)
  }
}

function mapSystemNotice(
  raw: Record<string, unknown>,
  payload: Record<string, unknown>
): SystemNoticeInfo {
  const kind =
    (typeof payload.kind === 'string' && payload.kind)
    || (typeof raw.kind === 'string' && raw.kind)
    || ''
  return {
    kind,
    trigger: typeof payload.trigger === 'string' ? payload.trigger : undefined,
    mode: typeof payload.mode === 'string' ? payload.mode : undefined,
    tokensBefore: typeof payload.tokensBefore === 'number' ? payload.tokensBefore : undefined,
    tokensAfter: typeof payload.tokensAfter === 'number' ? payload.tokensAfter : undefined,
    percentLeftAfter:
      typeof payload.percentLeftAfter === 'number' ? payload.percentLeftAfter : undefined,
    clearedToolResults:
      typeof payload.clearedToolResults === 'number' ? payload.clearedToolResults : undefined
  }
}

function normalizeTriggerKind(
  value: unknown
): 'heartbeat' | 'cron' | 'automation' | undefined {
  if (typeof value !== 'string') return undefined
  const normalized = value.trim().toLowerCase()
  if (normalized === 'heartbeat' || normalized === 'cron' || normalized === 'automation') {
    return normalized
  }
  return undefined
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
