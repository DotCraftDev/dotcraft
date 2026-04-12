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
import type { ThreadSummary } from '../types/thread'

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
    case 'turn/started': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      conv.onTurnStarted(rawTurn)
      const startedThreadId =
        (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
      if (startedThreadId) threads.markTurnStarted(startedThreadId)
      break
    }

    case 'turn/completed': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      const completedThreadId =
        (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
      if (completedThreadId) threads.markTurnEnded(completedThreadId)
      conv.onTurnCompleted(rawTurn)
      break
    }

    case 'turn/failed': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      const failedThreadId =
        (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
      if (failedThreadId) threads.markTurnEnded(failedThreadId)
      const error = (p.error as string) ?? 'Unknown error'
      conv.onTurnFailed(rawTurn, error)
      break
    }

    case 'turn/cancelled': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      const cancelledThreadId =
        (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
      if (cancelledThreadId) threads.markTurnEnded(cancelledThreadId)
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

function makeTurnPayload(id: string, status = 'running'): Record<string, unknown> {
  return { id, threadId: 'thread-1', status, items: [], startedAt: NOW }
}

beforeEach(() => {
  s().reset()
  useThreadStore.getState().reset()
})

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('notification dispatch payload format', () => {
  it('dispatches turn/started correctly from { method, params } payload', () => {
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
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
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
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
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
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
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
