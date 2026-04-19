import { render } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { useConfigChangeSubscription } from '../hooks/useConfigChangeSubscription'

function HookHost(props: {
  onWorkspaceModelChanged?: () => void
  onSkillsChanged?: () => void
  onMcpChanged?: () => void
}): JSX.Element {
  useConfigChangeSubscription({
    onWorkspaceModelChanged: props.onWorkspaceModelChanged,
    onSkillsChanged: props.onSkillsChanged,
    onMcpChanged: props.onMcpChanged
  })
  return <div />
}

describe('useConfigChangeSubscription', () => {
  it('dispatches actions by workspace/configChanged regions', () => {
    let notificationHandler: ((payload: { method: string; params: unknown }) => void) | null = null
    ;(window as unknown as { api: unknown }).api = {
      appServer: {
        onNotification: (callback: (payload: { method: string; params: unknown }) => void) => {
          notificationHandler = callback
          return () => {
            notificationHandler = null
          }
        }
      }
    } as unknown

    const onWorkspaceModelChanged = vi.fn()
    const onSkillsChanged = vi.fn()
    const onMcpChanged = vi.fn()
    render(
      <HookHost
        onWorkspaceModelChanged={onWorkspaceModelChanged}
        onSkillsChanged={onSkillsChanged}
        onMcpChanged={onMcpChanged}
      />
    )

    notificationHandler?.({
      method: 'workspace/configChanged',
      params: {
        source: 'workspace/config/update',
        regions: ['workspace.model', 'skills', 'mcp'],
        changedAt: '2026-04-19T10:15:03Z'
      }
    })

    expect(onWorkspaceModelChanged).toHaveBeenCalledTimes(1)
    expect(onSkillsChanged).toHaveBeenCalledTimes(1)
    expect(onMcpChanged).toHaveBeenCalledTimes(1)
  })

  it('deduplicates repeated source+region notifications in short window', () => {
    let notificationHandler: ((payload: { method: string; params: unknown }) => void) | null = null
    ;(window as unknown as { api: unknown }).api = {
      appServer: {
        onNotification: (callback: (payload: { method: string; params: unknown }) => void) => {
          notificationHandler = callback
          return () => {
            notificationHandler = null
          }
        }
      }
    } as unknown

    const onSkillsChanged = vi.fn()
    render(<HookHost onSkillsChanged={onSkillsChanged} />)

    const changedAt = '2026-04-19T10:15:03Z'
    notificationHandler?.({
      method: 'workspace/configChanged',
      params: {
        source: 'skills/setEnabled',
        regions: ['skills'],
        changedAt
      }
    })
    notificationHandler?.({
      method: 'workspace/configChanged',
      params: {
        source: 'skills/setEnabled',
        regions: ['skills'],
        changedAt
      }
    })

    expect(onSkillsChanged).toHaveBeenCalledTimes(1)
  })
})
