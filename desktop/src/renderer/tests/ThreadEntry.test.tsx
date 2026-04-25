import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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
      runningTurnThreadIds: new Set<string>(),
      parkedApprovals: new Map(),
      runtimeSnapshots: new Map(),
      pendingApprovalThreadIds: new Set<string>(),
      pendingPlanConfirmationThreadIds: new Set<string>(),
      unreadCompletedThreadIds: new Set<string>()
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

  it('keeps archive action hidden for active row until hover', async () => {
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    renderThreadEntry(makeThread())

    const row = screen.getByTestId('thread-entry-thread-1')
    const timeLabel = screen.getByText('1h')
    const archiveButton = screen.getByRole('button', { name: 'Archive' })

    expect(timeLabel).toBeVisible()
    expect(archiveButton).not.toBeVisible()

    fireEvent.mouseEnter(row)

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

  it('enters inline confirm on first archive click and archives on second click', async () => {
    const thread = makeThread()
    useThreadStore.setState({ threadList: [thread] })
    renderThreadEntry(thread)

    fireEvent.mouseEnter(await screen.findByTestId('thread-entry-thread-1'))
    fireEvent.click(screen.getByRole('button', { name: 'Archive' }))

    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/archive', { threadId: 'thread-1' })
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeVisible()

    fireEvent.click(screen.getByRole('button', { name: 'Confirm' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/archive', { threadId: 'thread-1' })
      expect(useThreadStore.getState().threadList).toEqual([])
      expect(useThreadStore.getState().activeThreadId).toBeNull()
    })
  })

  it('cancels inline confirm when the pointer leaves the row', async () => {
    renderThreadEntry(makeThread())

    const row = await screen.findByTestId('thread-entry-thread-1')
    fireEvent.mouseEnter(row)
    fireEvent.click(screen.getByRole('button', { name: 'Archive' }))
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeVisible()

    fireEvent.mouseLeave(row)

    await waitFor(() => {
      expect(screen.getByText('1h')).toBeVisible()
      expect(screen.getByRole('button', { name: 'Confirm' })).not.toBeVisible()
    })
  })

  it('supports keyboard focus for inline confirm and cancels when focus leaves', async () => {
    renderThreadEntry(makeThread())

    const archiveButton = screen.getByRole('button', { name: 'Archive' })
    fireEvent.focus(archiveButton)
    fireEvent.click(archiveButton)

    const confirmButton = await screen.findByRole('button', { name: 'Confirm' })
    expect(confirmButton).toBeVisible()

    fireEvent.blur(confirmButton, { relatedTarget: null })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Confirm' })).not.toBeVisible()
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
    const dialog = screen.getByRole('dialog')
    expect(dialog).toBeInTheDocument()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/archive', { threadId: 'thread-1' })
      expect(useThreadStore.getState().threadList).toEqual([])
    })
  })

  it('shows the custom confirm dialog before deleting from the context menu', async () => {
    const thread = makeThread()
    useThreadStore.setState({ threadList: [thread] })
    renderThreadEntry(thread)

    fireEvent.contextMenu(await screen.findByTestId('thread-entry-thread-1'), {
      clientX: 20,
      clientY: 20
    })

    fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }))

    const dialog = screen.getByRole('dialog')
    expect(dialog).toBeInTheDocument()
    expect(within(dialog).getByText('Delete conversation?')).toBeInTheDocument()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/delete', { threadId: 'thread-1' })
  })

  it('keeps the thread in local state when backend delete fails', async () => {
    const thread = makeThread()
    useThreadStore.setState({ threadList: [thread], activeThreadId: 'thread-1' })
    appServerSendRequest.mockRejectedValueOnce(new Error('delete failed'))
    renderThreadEntry(thread)

    fireEvent.contextMenu(await screen.findByTestId('thread-entry-thread-1'), {
      clientX: 20,
      clientY: 20
    })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }))

    const dialog = screen.getByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/delete', { threadId: 'thread-1' })
    })
    expect(useThreadStore.getState().threadList).toEqual([thread])
    expect(useThreadStore.getState().activeThreadId).toBe('thread-1')
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

  it('shows a running spinner for a background thread with an active turn', () => {
    useThreadStore.setState({
      runningTurnThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread())

    expect(screen.getByTestId('thread-running-indicator-thread-1')).toBeInTheDocument()
    expect(screen.getByLabelText('Turn running')).toBeInTheDocument()
  })

  it('shows a running spinner for the active thread with an active turn', () => {
    useThreadStore.setState({
      activeThreadId: 'thread-1',
      runningTurnThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread())

    expect(screen.getByTestId('thread-running-indicator-thread-1')).toBeInTheDocument()
  })

  it('shows paused status when not running', () => {
    renderThreadEntry(makeThread({ status: 'paused' }))

    expect(screen.queryByTestId('thread-running-indicator-thread-1')).not.toBeInTheDocument()
    expect(screen.getByLabelText('paused')).toBeInTheDocument()
  })

  it('prefers the running spinner over paused status when both states are present', () => {
    useThreadStore.setState({
      runningTurnThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread({ status: 'paused' }))

    expect(screen.getByTestId('thread-running-indicator-thread-1')).toBeInTheDocument()
    expect(screen.queryByLabelText('paused')).not.toBeInTheDocument()
  })

  it('renders origin channel as an icon badge with tooltip text', () => {
    renderThreadEntry(makeThread({ originChannel: 'qq' }))

    expect(screen.getByLabelText('Origin channel: qq')).toBeInTheDocument()
    expect(screen.queryByText('qq')).not.toBeInTheDocument()
  })

  it('shows pending approval badge over pending confirmation badge for inactive thread', () => {
    useThreadStore.setState({
      pendingApprovalThreadIds: new Set<string>(['thread-1']),
      pendingPlanConfirmationThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread())

    expect(screen.getByText('Awaiting approval')).toBeInTheDocument()
    expect(screen.queryByText('Awaiting confirmation')).not.toBeInTheDocument()
  })

  it('shows pending confirmation badge when approval is not pending', () => {
    useThreadStore.setState({
      pendingPlanConfirmationThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread())

    expect(screen.getByText('Awaiting confirmation')).toBeInTheDocument()
  })

  it('hides pending badges for active thread', () => {
    useThreadStore.setState({
      activeThreadId: 'thread-1',
      pendingApprovalThreadIds: new Set<string>(['thread-1']),
      pendingPlanConfirmationThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread())

    expect(screen.queryByText('Awaiting approval')).not.toBeInTheDocument()
    expect(screen.queryByText('Awaiting confirmation')).not.toBeInTheDocument()
  })

  it('shows unread completed dot when thread finished in background', () => {
    useThreadStore.setState({
      unreadCompletedThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread())

    expect(screen.getByLabelText('New result')).toBeInTheDocument()
  })

  it('keeps origin channel icon visible during archive confirm state', async () => {
    renderThreadEntry(makeThread({ originChannel: 'qq' }))

    const row = await screen.findByTestId('thread-entry-thread-1')
    fireEvent.mouseEnter(row)
    fireEvent.click(screen.getByRole('button', { name: 'Archive' }))

    expect(screen.getByRole('button', { name: 'Confirm' })).toBeVisible()
    expect(screen.getByLabelText('Origin channel: qq')).toBeVisible()
  })
})
