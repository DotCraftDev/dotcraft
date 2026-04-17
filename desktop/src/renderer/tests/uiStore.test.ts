import { beforeEach, describe, expect, it } from 'vitest'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

describe('uiStore goToNewChat', () => {
  beforeEach(() => {
    useThreadStore.getState().reset()
    useUIStore.setState({
      activeMainView: 'settings',
      welcomeDraft: null
    })
  })

  it('clears active thread and routes to conversation view', () => {
    useThreadStore.getState().setActiveThreadId('thread-123')

    useUIStore.getState().goToNewChat()

    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(useUIStore.getState().activeMainView).toBe('conversation')
  })
})
