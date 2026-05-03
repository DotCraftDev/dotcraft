/**
 * Thread-related TypeScript types matching the AppServer Wire Protocol responses.
 * Reference: specs/appserver-protocol.md §4
 */

import type { QueuedTurnInput } from './conversation'

export type ThreadStatus = 'active' | 'paused' | 'archived'

export type ApprovalPolicyWire = 'default' | 'autoApprove' | 'interrupt'

export interface ThreadRuntimeSnapshot {
  running: boolean
  waitingOnApproval: boolean
  waitingOnPlanConfirmation: boolean
}

export interface SubAgentThreadSourceWire {
  parentThreadId?: string
  parentTurnId?: string
  spawnCallId?: string
  rootThreadId?: string
  depth?: number
  agentNickname?: string
  agentRole?: string
  profileName?: string
  runtimeType?: string
  supportsSendInput?: boolean
  supportsResume?: boolean
  supportsClose?: boolean
}

export interface ThreadSourceWire {
  kind?: string
  subAgent?: SubAgentThreadSourceWire | null
}

export interface ThreadSummary {
  id: string
  userId?: string | null
  workspacePath?: string
  displayName: string | null
  status: ThreadStatus
  originChannel: string
  channelContext?: string | null
  createdAt: string      // ISO 8601 UTC
  lastActiveAt: string   // ISO 8601 UTC
  source?: ThreadSourceWire | null
  metadata?: Record<string, unknown>
  /** Best-effort current runtime snapshot from thread/list. Omitted by older hosts. */
  runtime?: ThreadRuntimeSnapshot
}

/**
 * Minimal Turn stub used in ThreadSummary / Thread sidebar data.
 * The full ConversationTurn (with items, streaming state) lives in types/conversation.ts
 * and is used by the conversation panel.
 */
export interface Turn {
  id: string
  status: string
  createdAt: string
  completedAt?: string
  /** Populated when includeTurns: true is used in thread/read */
  items?: Array<Record<string, unknown>>
  threadId?: string
  tokenUsage?: { inputTokens: number; outputTokens: number }
}

export interface ThreadConfigurationWire {
  mode?: string
  model?: string
  Model?: string
  approvalPolicy?: ApprovalPolicyWire
  [key: string]: unknown
}

/**
 * Per-thread context usage snapshot piggy-backed on thread/read, thread/start,
 * and thread/resume responses. Drives the desktop context-usage ring; optional
 * because older hosts or old threads without persisted context usage state do
 * not emit one. Fields mirror the backend `ContextUsageSnapshot` record.
 */
export interface ContextUsageSnapshotWire {
  tokens: number
  contextWindow: number
  autoCompactThreshold: number
  warningThreshold: number
  errorThreshold: number
  percentLeft: number
}

export interface Thread extends ThreadSummary {
  workspacePath: string
  userId: string
  metadata: Record<string, unknown>
  configuration?: ThreadConfigurationWire | null
  turns: Turn[]
  queuedInputs?: QueuedTurnInput[]
  contextUsage?: ContextUsageSnapshotWire | null
}

/**
 * Identity sent with thread/start and thread/list requests.
 * The desktop client uses channelName "dotcraft-desktop" and userId "local".
 */
export interface SessionIdentity {
  channelName: string
  userId: string
  channelContext: string
  workspacePath: string
}

/** Time-based group label for sidebar thread grouping (spec §7.2) */
export type ThreadGroup = 'Today' | 'Yesterday' | 'Previous 7 Days' | 'Previous 30 Days' | 'Older'

/** Ordered list of all group labels for consistent rendering */
export const THREAD_GROUP_ORDER: ThreadGroup[] = [
  'Today',
  'Yesterday',
  'Previous 7 Days',
  'Previous 30 Days',
  'Older'
]

/** Context menu action IDs for thread entries */
export type ThreadContextAction = 'rename' | 'archive' | 'delete'
