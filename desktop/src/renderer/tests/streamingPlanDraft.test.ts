import { beforeEach, describe, expect, it } from 'vitest'
import {
  selectStreamingPlanDraft,
  useConversationStore
} from '../stores/conversationStore'

describe('selectStreamingPlanDraft', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
  })

  it('returns null when no tool call is in flight', () => {
    expect(selectStreamingPlanDraft(useConversationStore.getState())).toBeNull()
  })

  it('returns a partial plan draft while CreatePlan arguments stream', () => {
    const store = useConversationStore.getState()

    store.setTurns([
      {
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'running',
        items: [],
        startedAt: new Date().toISOString()
      }
    ])

    // Simulate streaming argument deltas arriving one chunk at a time.
    store.onToolCallArgumentsDelta({
      turnId: 'turn-1',
      itemId: 'item-plan-1',
      delta: '{"title":"Ship feature X",',
      toolName: 'CreatePlan',
      callId: 'call-1'
    })
    store.onToolCallArgumentsDelta({
      turnId: 'turn-1',
      itemId: 'item-plan-1',
      delta: '"overview":"Step',
      toolName: 'CreatePlan',
      callId: 'call-1'
    })

    const draft = selectStreamingPlanDraft(useConversationStore.getState())
    expect(draft).not.toBeNull()
    expect(draft?.itemId).toBe('item-plan-1')
    expect(draft?.title).toBe('Ship feature X')
    expect(draft?.overview).toBe('Step')
  })

  it('extracts closed todo objects from the streaming todos array', () => {
    const store = useConversationStore.getState()
    store.setTurns([
      {
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'running',
        items: [],
        startedAt: new Date().toISOString()
      }
    ])

    store.onToolCallArgumentsDelta({
      turnId: 'turn-1',
      itemId: 'item-plan-2',
      delta: '{"title":"X","todos":[{"id":"t1","content":"do A","status":"pending"},{"id":"t2","content":"do B",',
      toolName: 'CreatePlan',
      callId: 'call-2'
    })

    const draft = selectStreamingPlanDraft(useConversationStore.getState())
    expect(draft?.todos).toHaveLength(1)
    expect(draft?.todos[0]).toEqual({ id: 't1', content: 'do A', status: 'pending' })
  })

  it('stops returning a draft once the tool call completes', () => {
    const store = useConversationStore.getState()
    store.setTurns([
      {
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'running',
        items: [],
        startedAt: new Date().toISOString()
      }
    ])

    store.onToolCallArgumentsDelta({
      turnId: 'turn-1',
      itemId: 'item-plan-3',
      delta: '{"title":"Y"',
      toolName: 'CreatePlan',
      callId: 'call-3'
    })

    expect(selectStreamingPlanDraft(useConversationStore.getState())).not.toBeNull()

    store.onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'item-plan-3',
        type: 'toolCall',
        status: 'completed',
        toolCallId: 'call-3',
        turnId: 'turn-1',
        payload: {
          toolName: 'CreatePlan',
          arguments: { title: 'Y' }
        }
      }
    })

    expect(selectStreamingPlanDraft(useConversationStore.getState())).toBeNull()
  })
})
