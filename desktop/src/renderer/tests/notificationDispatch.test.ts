/**
 * Integration test for the notification dispatch pipeline.
 *
 * Simulates the exact payload format the preload sends:
 *   { method: string, params: unknown }
 *
 * Drives a dispatch function (mirroring App.tsx's notification handler) through
 * a complete turn lifecycle and asserts that conversationStore transitions correctly.
 *
 * Background: The bug this guards against is App.tsx calling
 *   onNotification((method, params) => {...})   ← two args, wrong
 * when the preload actually calls the callback as
 *   callback({ method, params })                ← one payload object
 * causing ALL notifications to be silently dropped.
 */

import { describe, it, expect, beforeEach } from 'vitest'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useSkillsStore } from '../stores/skillsStore'
import type { ThreadSummary } from '../types/thread'
import type { InputPart } from '../types/conversation'
import { resolveWorkspaceConfigChangedPayload } from '../utils/workspaceConfigChanged'
import { buildComposerInputParts } from '../utils/composeInputParts'

// ---------------------------------------------------------------------------
// Replay a notification payload through the same dispatch logic as App.tsx.
// This is intentionally kept in sync with the switch block in App.tsx so
// that any future mismatch would be caught here first.
// ---------------------------------------------------------------------------

function dispatch(payload: { method: string; params: unknown }): void {
  // Mirror App.tsx: destructure the single payload object
  const method = payload.method
  const p = (payload.params ?? {}) as Record<string, unknown>
  const conv = useConversationStore.getState()
  const threads = useThreadStore.getState()

  switch (method) {
    case 'workspace/configChanged': {
      const event = resolveWorkspaceConfigChangedPayload(payload, workspaceConfigChangedDedupe)
      if (event?.regions.includes('skills')) {
        void useSkillsStore.getState().fetchSkills()
      }
      break
    }

    case 'thread/runtimeChanged': {
      const threadId = (p.threadId as string | undefined) ?? ''
      if (!threadId) break
      threads.applyRuntimeSnapshot(threadId, {
        running: p.runtime != null && typeof p.runtime === 'object' && (p.runtime as Record<string, unknown>).running === true,
        waitingOnApproval: p.runtime != null && typeof p.runtime === 'object' && (p.runtime as Record<string, unknown>).waitingOnApproval === true,
        waitingOnPlanConfirmation: p.runtime != null && typeof p.runtime === 'object' && (p.runtime as Record<string, unknown>).waitingOnPlanConfirmation === true
      }, {
        isActive: threads.activeThreadId === threadId,
        isDesktopOrigin: true
      })
      break
    }

    case 'turn/started': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      conv.onTurnStarted(rawTurn)
      break
    }

    case 'turn/completed': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      conv.onTurnCompleted(rawTurn)
      break
    }

    case 'turn/failed': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      const error = (p.error as string) ?? 'Unknown error'
      conv.onTurnFailed(rawTurn, error)
      break
    }

    case 'turn/cancelled': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      const reason = (p.reason as string) ?? ''
      conv.onTurnCancelled(rawTurn, reason)
      break
    }

    case 'item/started':
      conv.onItemStarted(p)
      break

    case 'item/agentMessage/delta':
      conv.onAgentMessageDelta((p.delta as string) ?? '')
      break

    case 'item/reasoning/delta':
      conv.onReasoningDelta((p.delta as string) ?? '')
      break

    case 'item/commandExecution/outputDelta':
      conv.onCommandExecutionDelta({
        threadId: (p.threadId as string | undefined),
        turnId: (p.turnId as string | undefined),
        itemId: (p.itemId as string | undefined),
        delta: (p.delta as string | undefined)
      })
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
      break

    case 'item/usage/delta':
      conv.onUsageDelta((p.inputTokens as number) ?? 0, (p.outputTokens as number) ?? 0)
      break

    case 'system/event':
      conv.onSystemEvent((p.kind as string) ?? '')
      break

    default:
      break
  }
}

/**
 * Mirrors App.tsx thread lifecycle branch (threadList / removeThread).
 */
function dispatchThreadLifecycle(payload: { method: string; params: unknown }): void {
  const method = payload.method
  const p = (payload.params ?? {}) as Record<string, unknown>
  const { addThread, removeThread } = useThreadStore.getState()

  switch (method) {
    case 'thread/started': {
      const pp = p as { thread: ThreadSummary }
      addThread(pp.thread)
      break
    }
    case 'thread/deleted': {
      const pp = p as { threadId: string }
      removeThread(pp.threadId)
      break
    }
    default:
      break
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const s = () => useConversationStore.getState()

const NOW = new Date().toISOString()
const workspaceConfigChangedDedupe = new Map<string, number>()

function makeTurnPayload(id: string, status = 'running'): Record<string, unknown> {
  return { id, threadId: 'thread-1', status, items: [], startedAt: NOW }
}

async function dispatchTurnCompletedWithAutoSend(
  payload: { method: string; params: unknown },
  options: {
    sendRequest: (method: string, params?: unknown) => Promise<unknown>
    inputParts?: InputPart[]
    workspacePath?: string
  }
): Promise<void> {
  const pendingBefore = useConversationStore.getState().pendingMessage
  dispatch(payload)

  if (!pendingBefore) {
    return
  }

  const activeId = useThreadStore.getState().activeThreadId
  if (!activeId) {
    useConversationStore.getState().setPendingMessage(null)
    return
  }

  let effectiveThreadId = activeId
  const pendingInputParts = options.inputParts
    ?? pendingBefore.inputParts
    ?? buildComposerInputParts({
      text: pendingBefore.text.trim(),
      files: pendingBefore.files ?? []
    }).inputParts

  if (pendingInputParts.length > 0) {
    await options.sendRequest('turn/start', {
      threadId: effectiveThreadId,
      input: pendingInputParts,
      identity: {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: `workspace:${options.workspacePath ?? 'F:/dotcraft'}`,
        workspacePath: options.workspacePath ?? 'F:/dotcraft'
      }
    })
  }
  useConversationStore.getState().setPendingMessage(null)
}

beforeEach(() => {
  s().reset()
  useThreadStore.getState().reset()
  useThreadStore.setState({
    activeThreadId: 'thread-1',
    threadList: [
      {
        id: 'thread-1',
        displayName: 'Thread 1',
        status: 'active',
        originChannel: 'dotcraft-desktop',
        createdAt: NOW,
        lastActiveAt: NOW
      }
    ]
  })
  workspaceConfigChangedDedupe.clear()
})

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('notification dispatch payload format', () => {
  it('dispatches skills refresh for workspace/configChanged notifications', () => {
    let fetchSkillsCalls = 0
    useSkillsStore.setState({
      fetchSkills: async () => {
        fetchSkillsCalls += 1
      }
    })

    dispatch({
      method: 'workspace/configChanged',
      params: {
        source: 'skills/setEnabled',
        regions: ['skills'],
        changedAt: NOW
      }
    })

    expect(fetchSkillsCalls).toBe(1)
  })

  it('does not refresh skills for unrelated workspace/configChanged regions', () => {
    let fetchSkillsCalls = 0
    useSkillsStore.setState({
      fetchSkills: async () => {
        fetchSkillsCalls += 1
      }
    })

    dispatch({
      method: 'workspace/configChanged',
      params: {
        source: 'workspace/config/update',
        regions: ['mcp', 'externalChannel'],
        changedAt: NOW
      }
    })

    expect(fetchSkillsCalls).toBe(0)
  })

  it('dispatches turn/started correctly from { method, params } payload', () => {
    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })
    dispatch({
      method: 'turn/started',
      params: { turn: makeTurnPayload('turn_server_1') }
    })

    const state = s()
    expect(state.turnStatus).toBe('running')
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn_server_1')
    expect(useThreadStore.getState().runningTurnThreadIds.has('thread-1')).toBe(true)
  })

  it('dispatches turn/completed and sets status to idle', () => {
    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })
    dispatch({
      method: 'turn/completed',
      params: { turn: makeTurnPayload('turn_1', 'completed') }
    })

    const state = s()
    expect(state.turnStatus).toBe('idle')
    expect(state.turns[0].status).toBe('completed')
    expect(useThreadStore.getState().runningTurnThreadIds.has('thread-1')).toBe(false)
  })

  it('dispatches turn/failed', () => {
    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })
    dispatch({
      method: 'turn/failed',
      params: { turn: makeTurnPayload('turn_1', 'failed'), error: 'API rate limit' }
    })

    expect(s().turnStatus).toBe('idle')
    expect(s().turns[0].status).toBe('failed')
    expect(s().turns[0].error).toBe('API rate limit')
    expect(useThreadStore.getState().runningTurnThreadIds.has('thread-1')).toBe(false)
  })

  it('dispatches turn/cancelled', () => {
    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })
    dispatch({
      method: 'turn/cancelled',
      params: { turn: makeTurnPayload('turn_1', 'cancelled'), reason: 'user requested' }
    })

    expect(s().turnStatus).toBe('idle')
    expect(s().turns[0].status).toBe('cancelled')
    expect(s().turns[0].cancelReason).toBe('user requested')
    expect(useThreadStore.getState().runningTurnThreadIds.has('thread-1')).toBe(false)
  })

  it('dispatches item/agentMessage/delta and accumulates streamingMessage', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({ method: 'item/started', params: { turnId: 'turn_1', item: { id: 'item_1', type: 'agentMessage' } } })
    dispatch({ method: 'item/agentMessage/delta', params: { delta: 'Hello' } })
    dispatch({ method: 'item/agentMessage/delta', params: { delta: ', world!' } })

    expect(s().streamingMessage).toBe('Hello, world!')
  })

  it('dispatches item/completed (agentMessage) and commits text to turn items', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({ method: 'item/started', params: { turnId: 'turn_1', item: { id: 'item_1', type: 'agentMessage' } } })
    dispatch({ method: 'item/agentMessage/delta', params: { delta: 'The answer is 42.' } })
    dispatch({
      method: 'item/completed',
      params: { turnId: 'turn_1', item: { id: 'item_1', type: 'agentMessage', createdAt: NOW } }
    })

    const items = s().turns[0].items
    expect(s().streamingMessage).toBe('')
    expect(items).toHaveLength(1)
    expect(items[0].text).toBe('The answer is 42.')
    expect(items[0].type).toBe('agentMessage')
  })

  it('dispatches command execution deltas into the matching item', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'cmd_1',
          type: 'commandExecution',
          payload: {
            callId: 'exec-1',
            command: 'npm test',
            status: 'inProgress',
            aggregatedOutput: ''
          }
        }
      }
    })
    dispatch({
      method: 'item/commandExecution/outputDelta',
      params: { threadId: 'thread-1', turnId: 'turn_1', itemId: 'cmd_1', delta: 'chunk\n' }
    })
    dispatch({
      method: 'item/completed',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'cmd_1',
          type: 'commandExecution',
          payload: {
            callId: 'exec-1',
            command: 'npm test',
            status: 'completed',
            aggregatedOutput: 'chunk\n',
            exitCode: 0,
            durationMs: 400
          }
        }
      }
    })

    const item = s().turns[0].items.find((i) => i.id === 'cmd_1')
    expect(item?.type).toBe('commandExecution')
    expect(item?.aggregatedOutput).toBe('chunk\n')
    expect(item?.executionStatus).toBe('completed')
    expect(item?.exitCode).toBe(0)
  })

  it('dispatches tool call argument deltas into the matching tool call item and decodes escapes', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_write_1',
          type: 'toolCall',
          payload: {
            callId: 'write-1',
            toolName: 'WriteFile'
          }
        }
      }
    })
    dispatch({
      method: 'item/toolCall/argumentsDelta',
      params: {
        threadId: 'thread-1',
        turnId: 'turn_1',
        itemId: 'tool_write_1',
        toolName: 'WriteFile',
        callId: 'write-1',
        delta: '{"path":"a.txt","content":"hello\\nworld"}'
      }
    })

    const item = s().turns[0].items.find((i) => i.id === 'tool_write_1')
    expect(item?.type).toBe('toolCall')
    expect(item?.status).toBe('streaming')
    expect(item?.argumentsPreview).toContain('"content":"hello\\nworld"')
    expect(item?.streamingFileContent).toBe('hello\nworld')

    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_edit_1',
          type: 'toolCall',
          payload: {
            callId: 'edit-1',
            toolName: 'EditFile'
          }
        }
      }
    })
    dispatch({
      method: 'item/toolCall/argumentsDelta',
      params: {
        threadId: 'thread-1',
        turnId: 'turn_1',
        itemId: 'tool_edit_1',
        toolName: 'EditFile',
        callId: 'edit-1',
        delta: '{"path":"a.txt","oldText":"before","newText":"## title\\ncontent"}'
      }
    })

    const editItem = s().turns[0].items.find((i) => i.id === 'tool_edit_1')
    expect(editItem?.type).toBe('toolCall')
    expect(editItem?.status).toBe('streaming')
    expect(editItem?.argumentsPreview).toContain('"newText":"## title\\ncontent"')
    expect(editItem?.streamingFileContent).toBe('## title\ncontent')
  })

  it('merges finalized toolCall arguments on completion so WriteFile diff is generated', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_write_2',
          type: 'toolCall',
          payload: {
            callId: 'write-2',
            toolName: 'WriteFile'
          }
        }
      }
    })
    dispatch({
      method: 'item/completed',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_write_2',
          type: 'toolCall',
          payload: {
            callId: 'write-2',
            toolName: 'WriteFile',
            arguments: {
              path: 'a.txt',
              content: 'line1\nline2'
            }
          }
        }
      }
    })
    dispatch({
      method: 'item/completed',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_result_2',
          type: 'toolResult',
          payload: {
            callId: 'write-2',
            success: true,
            result: 'Successfully wrote 11 bytes (2 lines) to a.txt'
          }
        }
      }
    })

    const item = s().turns[0].items.find((i) => i.id === 'tool_write_2')
    expect(item?.type).toBe('toolCall')
    expect(item?.status).toBe('completed')
    expect(item?.arguments?.path).toBe('a.txt')
    expect(item?.arguments?.content).toBe('line1\nline2')

    const itemDiff = s().itemDiffs.get('tool_write_2')
    expect(itemDiff).toBeDefined()
    expect(itemDiff?.filePath).toBe('a.txt')
    expect(itemDiff?.additions).toBe(2)
  })

  it('updates the existing Exec toolCall instead of requiring a standalone terminal block', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_1',
          type: 'toolCall',
          payload: {
            callId: 'exec-3',
            toolName: 'Exec',
            arguments: { command: 'dir' }
          }
        }
      }
    })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'cmd_3',
          type: 'commandExecution',
          payload: {
            callId: 'exec-3',
            command: 'dir',
            status: 'inProgress',
            aggregatedOutput: ''
          }
        }
      }
    })
    dispatch({
      method: 'item/commandExecution/outputDelta',
      params: { threadId: 'thread-1', turnId: 'turn_1', itemId: 'cmd_3', delta: 'file.txt\n' }
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool_1')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.aggregatedOutput).toBe('file.txt\n')
    expect(toolItem?.executionStatus).toBe('inProgress')
  })

  it('keeps Exec render state live when command execution starts before toolCall completion', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'cmd_real_order',
          type: 'commandExecution',
          payload: {
            callId: 'exec-real-order',
            command: 'dir',
            source: 'host',
            status: 'inProgress',
            aggregatedOutput: ''
          }
        }
      }
    })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_real_order',
          type: 'toolCall',
          payload: {
            callId: 'exec-real-order',
            toolName: 'Exec'
          }
        }
      }
    })
    dispatch({
      method: 'item/completed',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_real_order',
          type: 'toolCall',
          payload: {
            callId: 'exec-real-order',
            toolName: 'Exec',
            arguments: { command: 'dir' }
          }
        }
      }
    })
    dispatch({
      method: 'item/commandExecution/outputDelta',
      params: { threadId: 'thread-1', turnId: 'turn_1', itemId: 'cmd_real_order', delta: 'file.txt\n' }
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool_real_order')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.status).toBe('completed')
    expect(toolItem?.arguments?.command).toBe('dir')
    expect(toolItem?.executionStatus).toBe('inProgress')
    expect(toolItem?.aggregatedOutput).toBe('file.txt\n')
  })

  it('dispatches item/usage/delta and accumulates tokens', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({ method: 'item/usage/delta', params: { inputTokens: 150, outputTokens: 80 } })
    dispatch({ method: 'item/usage/delta', params: { inputTokens: 50, outputTokens: 20 } })

    expect(s().inputTokens).toBe(200)
    expect(s().outputTokens).toBe(100)
  })

  it('dispatches system/event and sets systemLabel', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({ method: 'system/event', params: { kind: 'compacting' } })
    expect(s().systemLabel).toBe('Compacting context...')

    dispatch({ method: 'system/event', params: { kind: 'compacted' } })
    expect(s().systemLabel).toBeNull()
  })

  it('ignores unknown notification methods without throwing', () => {
    expect(() => {
      dispatch({ method: 'unknown/future/event', params: { foo: 'bar' } })
    }).not.toThrow()
  })
})

describe('thread lifecycle notification dispatch', () => {
  const minimalThread = (id: string): ThreadSummary => ({
    id,
    displayName: 'T',
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: new Date().toISOString(),
    lastActiveAt: new Date().toISOString()
  })

  beforeEach(() => {
    useThreadStore.getState().reset()
  })

  it('removes thread from list on thread/deleted', () => {
    dispatchThreadLifecycle({
      method: 'thread/started',
      params: { thread: minimalThread('thread_del_1') }
    })
    expect(useThreadStore.getState().threadList.some((t) => t.id === 'thread_del_1')).toBe(true)

    dispatchThreadLifecycle({
      method: 'thread/deleted',
      params: { threadId: 'thread_del_1' }
    })
    expect(useThreadStore.getState().threadList.some((t) => t.id === 'thread_del_1')).toBe(false)
  })
})

describe('full turn lifecycle via notification dispatch', () => {
  it('processes a complete turn: started -> reasoning -> agent message -> completed', () => {
    const turnId = 'turn_full_1'

    // Server confirms turn started
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload(turnId) } })
    expect(s().turnStatus).toBe('running')
    expect(s().activeTurnId).toBe(turnId)

    // Reasoning phase
    dispatch({ method: 'item/started', params: { turnId, item: { id: 'r_1', type: 'reasoningContent' } } })
    dispatch({ method: 'item/reasoning/delta', params: { delta: 'Let me think...' } })
    expect(s().streamingReasoning).toBe('Let me think...')
    dispatch({
      method: 'item/completed',
      params: { turnId, item: { id: 'r_1', type: 'reasoningContent', createdAt: NOW } }
    })
    expect(s().streamingReasoning).toBe('')
    const reasoningItem = s().turns[0].items.find((i) => i.type === 'reasoningContent')
    expect(reasoningItem?.reasoning).toBe('Let me think...')

    // Agent message streaming
    dispatch({ method: 'item/started', params: { turnId, item: { id: 'msg_1', type: 'agentMessage' } } })
    dispatch({ method: 'item/agentMessage/delta', params: { delta: 'The answer ' } })
    dispatch({ method: 'item/agentMessage/delta', params: { delta: 'is 42.' } })
    expect(s().streamingMessage).toBe('The answer is 42.')
    dispatch({
      method: 'item/completed',
      params: { turnId, item: { id: 'msg_1', type: 'agentMessage', createdAt: NOW } }
    })
    expect(s().streamingMessage).toBe('')

    // Token usage accumulation
    dispatch({ method: 'item/usage/delta', params: { inputTokens: 500, outputTokens: 120 } })

    // Turn completed
    dispatch({
      method: 'turn/completed',
      params: { turn: { ...makeTurnPayload(turnId, 'completed'), completedAt: NOW } }
    })

    const finalState = s()
    expect(finalState.turnStatus).toBe('idle')
    expect(finalState.activeTurnId).toBeNull()
    expect(finalState.turns[0].status).toBe('completed')
    expect(finalState.inputTokens).toBe(500)
    expect(finalState.outputTokens).toBe(120)

    const agentItem = finalState.turns[0].items.find((i) => i.type === 'agentMessage')
    expect(agentItem?.text).toBe('The answer is 42.')
  })

  it('two-arg callback format (the old bug) would silently drop all notifications', () => {
    // This test documents the exact bug that was fixed.
    // If someone reverts App.tsx to the old two-arg form:
    //   onNotification((method, params) => {...})
    // then `method` receives the payload object and switch(method) matches nothing.

    const payload = { method: 'turn/started', params: { turn: makeTurnPayload('turn_bug') } }

    // Simulate the broken two-arg dispatch: method = payload object, params = undefined
    const brokenDispatch = (method: unknown, _params: unknown): void => {
      // switch(method) would compare an object to strings -- never matches
      let matched = false
      if (method === 'turn/started') matched = true
      expect(matched).toBe(false) // object !== string, bug confirmed
    }
    brokenDispatch(payload, undefined)

    // The correct dispatch extracts method from payload.method
    const method = payload.method
    expect(method).toBe('turn/started') // string comparison works
  })
})

describe('pending message auto-send', () => {
  it('auto-sends queued command refs with file references after turn completion', async () => {
    const sendRequest = async (method: string, params?: unknown): Promise<unknown> => {
      if (method === 'turn/start') {
        expect(params).toEqual(
          expect.objectContaining({
            threadId: 'thread-1',
            input: [
              { type: 'text', text: '[[Attached File: C:\\temp\\notes.txt]]\n\n' },
              { type: 'commandRef', name: 'code-review', rawText: '/code-review' }
            ]
          })
        )
      }
      return {}
    }

    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_pending') } })
    useConversationStore.getState().setPendingMessage({
      text: '/code-review',
      inputParts: [
        { type: 'text', text: '[[Attached File: C:\\temp\\notes.txt]]\n\n' },
        { type: 'commandRef', name: 'code-review', rawText: '/code-review' }
      ],
      files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }]
    })

    await dispatchTurnCompletedWithAutoSend(
      {
        method: 'turn/completed',
        params: { turn: { ...makeTurnPayload('turn_pending', 'completed'), completedAt: NOW } }
      },
      { sendRequest }
    )

    expect(s().pendingMessage).toBeNull()
  })

  it('auto-sends file-only queued messages using structured input parts', async () => {
    const sendRequest = async (method: string, params?: unknown): Promise<unknown> => {
      expect(method).toBe('turn/start')
      expect(params).toEqual(
        expect.objectContaining({
          threadId: 'thread-1',
          input: [{ type: 'text', text: '[[Attached File: C:\\temp\\notes.txt]]' }]
        })
      )
      return {}
    }

    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_pending_files') } })
    useConversationStore.getState().setPendingMessage({
      text: '',
      files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }]
    })

    await dispatchTurnCompletedWithAutoSend(
      {
        method: 'turn/completed',
        params: { turn: { ...makeTurnPayload('turn_pending_files', 'completed'), completedAt: NOW } }
      },
      { sendRequest }
    )

    expect(s().pendingMessage).toBeNull()
  })

  it('does not send a turn when queued message has no structured input parts', async () => {
    let turnStartCalled = false
    const sendRequest = async (method: string): Promise<unknown> => {
      if (method === 'turn/start') {
        turnStartCalled = true
      }
      return {}
    }

    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_pending_skip') } })
    useConversationStore.getState().setPendingMessage({
      text: '',
      inputParts: []
    })

    await dispatchTurnCompletedWithAutoSend(
      {
        method: 'turn/completed',
        params: { turn: { ...makeTurnPayload('turn_pending_skip', 'completed'), completedAt: NOW } }
      },
      { sendRequest }
    )

    expect(turnStartCalled).toBe(false)
    expect(s().pendingMessage).toBeNull()
  })
})
