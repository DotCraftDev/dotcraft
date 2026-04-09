/**
 * Thread-related TypeScript types matching the AppServer Wire Protocol responses.
 * Reference: specs/appserver-protocol.md §4
 */

export type ThreadStatus = 'active' | 'paused' | 'archived'

export interface ThreadSummary {
  id: string
  displayName: string | null
  status: ThreadStatus
  originChannel: string
  createdAt: string      // ISO 8601 UTC
  lastActiveAt: string   // ISO 8601 UTC
}

/**
 * Minimal Turn stub used in ThreadSummary / Thread for M2 sidebar purposes.
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
  [key: string]: unknown
}

export interface Thread extends ThreadSummary {
  workspacePath: string
  userId: string
  metadata: Record<string, unknown>
  configuration?: ThreadConfigurationWire | null
  turns: Turn[]
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
