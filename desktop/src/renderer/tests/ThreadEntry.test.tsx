import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { ThreadEntry } from '../components/sidebar/ThreadEntry'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { useThreadStore } from '../stores/threadStore'
import type { ThreadSummary } from '../types/thread'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()

function makeThread(overrides: Partial<ThreadSummary> = {}): ThreadSummary {
  const now = Date.now()
  return {
    id: 'thread-1',
    displayName: 'Optimize workspace cleanup',
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: new Date(now - 2 * 60 * 60 * 1000).toISOString(),
    lastActiveAt: new Date(now - 61 * 60 * 1000).toISOString(),
    ...overrides
  }
}

function renderThreadEntry(thread: ThreadSummary): void {
  render(
    <LocaleProvider>
      <ConfirmDialogHost />
      <ThreadEntry thread={thread} />
    </LocaleProvider>
  )
}

describe('ThreadEntry', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    settingsGet.mockResolvedValue({ locale: 'en' })
    appServerSendRequest.mockResolvedValue({})

    useThreadStore.setState({
      threadList: [],
      activeThreadId: null,
      activeThread: null,
      searchQuery: '',
      loading: false,
      runningTurnThreadIds: new Set<string>()
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest }
      }
    })
  })

  it('shows relative time by default and swaps to archive on hover', async () => {
    renderThreadEntry(makeThread())

    const timeLabel = screen.getByText('1h')
    const archiveButton = screen.getByRole('button', { name: 'Archive' })

    expect(timeLabel).toBeVisible()
    expect(archiveButton).not.toBeVisible()

    fireEvent.mouseEnter(screen.getByTestId('thread-entry-thread-1'))

    await waitFor(() => {
      expect(timeLabel).not.toBeVisible()
      expect(archiveButton).toBeVisible()
    })
  })

  it('reveals archive action on focus for keyboard access', async () => {
    renderThreadEntry(makeThread())

    const archiveButton = screen.getByRole('button', { name: 'Archive' })
    expect(archiveButton).not.toBeVisible()

    fireEvent.focus(archiveButton)

    await waitFor(() => {
      expect(archiveButton).toBeVisible()
    })
  })

  it('archives from the hover action without selecting the thread first', async () => {
    const thread = makeThread()
    useThreadStore.setState({ threadList: [thread] })
    renderThreadEntry(thread)

    fireEvent.mouseEnter(await screen.findByTestId('thread-entry-thread-1'))
    fireEvent.click(screen.getByRole('button', { name: 'Archive' }))

    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(screen.getByRole('dialog')).toBeInTheDocument()

    fireEvent.click(screen.getAllByRole('button', { name: 'Archive' }).at(-1)!)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/archive', { threadId: 'thread-1' })
      expect(useThreadStore.getState().threadList).toEqual([])
      expect(useThreadStore.getState().activeThreadId).toBeNull()
    })
  })

  it('reuses the same archive flow from the context menu', async () => {
    const thread = makeThread()
    useThreadStore.setState({ threadList: [thread] })
    renderThreadEntry(thread)

    fireEvent.contextMenu(await screen.findByTestId('thread-entry-thread-1'), {
      clientX: 20,
      clientY: 20
    })

    fireEvent.click(await screen.findByRole('menuitem', { name: 'Archive' }))
    expect(screen.getByRole('dialog')).toBeInTheDocument()

    fireEvent.click(screen.getAllByRole('button', { name: 'Archive' }).at(-1)!)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/archive', { threadId: 'thread-1' })
      expect(useThreadStore.getState().threadList).toEqual([])
    })
  })

  it('hides time and archive action while renaming', async () => {
    renderThreadEntry(makeThread())

    fireEvent.contextMenu(await screen.findByTestId('thread-entry-thread-1'), {
      clientX: 24,
      clientY: 24
    })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Rename' }))

    await waitFor(() => {
      expect(screen.getByDisplayValue('Optimize workspace cleanup')).toBeInTheDocument()
    })
    expect(screen.queryByText('1h')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Archive' })).toBeNull()
  })
})
