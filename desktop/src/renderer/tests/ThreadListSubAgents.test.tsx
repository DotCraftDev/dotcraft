import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ThreadList } from '../components/sidebar/ThreadList'
import { useThreadStore } from '../stores/threadStore'
import type { ThreadSummary } from '../types/thread'

const settingsGet = vi.fn()

function makeThread(overrides: Partial<ThreadSummary> = {}): ThreadSummary {
  const now = new Date().toISOString()
  return {
    id: 'parent-1',
    displayName: 'Create hatch pet',
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: now,
    lastActiveAt: now,
    ...overrides
  }
}

function renderList(): void {
  render(
    <LocaleProvider>
      <ThreadList />
    </LocaleProvider>
  )
}

describe('ThreadList subagent entries', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: vi.fn() }
      }
    })
    useThreadStore.getState().reset()
  })

  it('places subagent children directly after their parent and indents them', () => {
    useThreadStore.getState().setThreadList([
      makeThread({ id: 'other-1', displayName: 'Other conversation' }),
      makeThread({ id: 'parent-1', displayName: 'Create hatch pet' }),
      makeThread({
        id: 'child-1',
        displayName: 'Create hatch pet Lovelace',
        originChannel: 'subagent',
        source: {
          kind: 'subagent',
          subAgent: {
            parentThreadId: 'parent-1',
            depth: 1
          }
        }
      })
    ])

    renderList()

    const rows = screen.getAllByTestId(/thread-entry-/)
    expect(rows.map((row) => row.getAttribute('data-testid'))).toEqual([
      'thread-entry-other-1',
      'thread-entry-parent-1',
      'thread-entry-child-1'
    ])
    expect(screen.getByLabelText('Background agent')).toBeInTheDocument()
    expect(screen.queryByLabelText('Origin channel: subagent')).not.toBeInTheDocument()
    expect(screen.getByTestId('thread-entry-child-1').getAttribute('style')).toContain('padding: 6px 12px 6px 28px')
  })
})
