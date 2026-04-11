import { describe, it, expect, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { SettingsView } from '../components/settings/SettingsView'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useToastStore } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const appServerSendRequest = vi.fn()
const appServerRestartManaged = vi.fn()
const appServerOnNotification = vi.fn()

function renderSettingsView(): void {
  render(
    <LocaleProvider>
      <SettingsView workspacePath="F:\\dotcraft" />
    </LocaleProvider>
  )
}

describe('SettingsView restart AppServer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useToastStore.setState({ toasts: [] })
    useUIStore.setState({ activeMainView: 'settings' })

    settingsGet.mockResolvedValue({
      connectionMode: 'stdio',
      webSocket: { host: '127.0.0.1', port: 9100 },
      locale: 'en',
      visibleChannels: []
    })
    settingsSet.mockResolvedValue(undefined)
    appServerSendRequest.mockResolvedValue({ channels: [] })
    appServerRestartManaged.mockResolvedValue(undefined)
    appServerOnNotification.mockReturnValue(() => {})

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: settingsGet,
          set: settingsSet
        },
        appServer: {
          sendRequest: appServerSendRequest,
          restartManaged: appServerRestartManaged,
          onNotification: appServerOnNotification
        }
      }
    })
  })

  it('shows restart button for Desktop-managed connection modes', async () => {
    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))

    expect(await screen.findByRole('button', { name: 'Restart AppServer' })).toBeInTheDocument()
  })

  it('hides restart button for remote mode', async () => {
    settingsGet.mockResolvedValue({
      connectionMode: 'remote',
      remote: { url: 'ws://127.0.0.1:9100/ws' },
      locale: 'en'
    })

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Restart AppServer' })).not.toBeInTheDocument()
    })
  })

  it('keeps restart button visible when saved mode is stdio but form switches to remote without saving', async () => {
    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))
    const connectionModeSelect = await screen.findByLabelText('Connection mode')
    fireEvent.change(connectionModeSelect, { target: { value: 'remote' } })

    expect(screen.getByRole('button', { name: 'Restart AppServer' })).toBeInTheDocument()
  })

  it('keeps restart button hidden when saved mode is remote but form switches to stdio without saving', async () => {
    settingsGet.mockResolvedValue({
      connectionMode: 'remote',
      remote: { url: 'ws://127.0.0.1:9100/ws' },
      locale: 'en'
    })

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))
    const connectionModeSelect = await screen.findByLabelText('Connection mode')
    fireEvent.change(connectionModeSelect, { target: { value: 'stdio' } })

    expect(screen.queryByRole('button', { name: 'Restart AppServer' })).not.toBeInTheDocument()
  })

  it('hides restart button after saving a switch from stdio to remote', async () => {
    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))
    const connectionModeSelect = await screen.findByLabelText('Connection mode')
    fireEvent.change(connectionModeSelect, { target: { value: 'remote' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(settingsSet).toHaveBeenCalled()
      expect(screen.queryByRole('button', { name: 'Restart AppServer' })).not.toBeInTheDocument()
    })
  })

  it('shows restart button after saving a switch from remote to stdio', async () => {
    settingsGet.mockResolvedValue({
      connectionMode: 'remote',
      remote: { url: 'ws://127.0.0.1:9100/ws' },
      locale: 'en'
    })

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))
    const connectionModeSelect = await screen.findByLabelText('Connection mode')
    fireEvent.change(connectionModeSelect, { target: { value: 'stdio' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(settingsSet).toHaveBeenCalled()
      expect(screen.getByRole('button', { name: 'Restart AppServer' })).toBeInTheDocument()
    })
  })

  it('restarts managed AppServer and adds a success toast', async () => {
    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Restart AppServer' }))

    await waitFor(() => {
      expect(appServerRestartManaged).toHaveBeenCalledOnce()
    })
    await waitFor(() => {
      expect(useToastStore.getState().toasts.some((toast) => toast.message === 'AppServer restarted')).toBe(true)
    })
  })

  it('shows an error toast when restart fails', async () => {
    appServerRestartManaged.mockRejectedValue(new Error('boom'))

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Restart AppServer' }))

    await waitFor(() => {
      expect(
        useToastStore
          .getState()
          .toasts.some((toast) => toast.message === 'Failed to restart AppServer: boom')
      ).toBe(true)
    })
  })

  it('shows archived threads tab and hides Save there', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/list') return { data: [] }
      return { channels: [] }
    })

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Archived Threads' }))

    expect(
      await screen.findByText('Browse archived conversations for this workspace and restore them when needed.')
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
  })

  it('loads archived threads with includeArchived and only renders archived rows', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/list') {
        return {
          data: [
            {
              id: 'archived-1',
              displayName: 'Archived thread',
              status: 'archived',
              originChannel: 'dotcraft',
              createdAt: '2026-04-11T06:00:00Z',
              lastActiveAt: '2026-04-11T08:00:00Z'
            },
            {
              id: 'active-1',
              displayName: 'Active thread',
              status: 'active',
              originChannel: 'dotcraft',
              createdAt: '2026-04-11T06:00:00Z',
              lastActiveAt: '2026-04-11T08:00:00Z'
            }
          ]
        }
      }
      return { channels: [] }
    })

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Archived Threads' }))

    await waitFor(() => {
      const threadListCall = appServerSendRequest.mock.calls.find(([method]) => method === 'thread/list')
      expect(threadListCall).toBeTruthy()
      const params = threadListCall?.[1] as {
        includeArchived?: boolean
        crossChannelOrigins?: string[]
        identity?: { workspacePath?: string; channelContext?: string }
      }
      expect(params.includeArchived).toBe(true)
      expect(params.crossChannelOrigins).toEqual([])
      expect(params.identity?.workspacePath).toContain('dotcraft')
      expect(params.identity?.channelContext).toContain('workspace:')
    })
    await waitFor(() => {
      expect(screen.getByText('Archived thread')).toBeInTheDocument()
    })
    await waitFor(() => {
      expect(screen.queryByText('Active thread')).not.toBeInTheDocument()
    })
  })

  it('restores an archived thread and removes it from the list', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/list') {
        return {
          data: [
            {
              id: 'archived-1',
              displayName: 'Archived thread',
              status: 'archived',
              originChannel: 'dotcraft',
              createdAt: '2026-04-11T06:00:00Z',
              lastActiveAt: '2026-04-11T08:00:00Z'
            }
          ]
        }
      }
      if (method === 'thread/unarchive') {
        return {}
      }
      return { channels: [] }
    })

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Archived Threads' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Restore' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/unarchive', { threadId: 'archived-1' })
    })
    await waitFor(() => {
      expect(screen.queryByText('Archived thread')).not.toBeInTheDocument()
    })
  })

  it('keeps archived row visible and shows error toast when restore fails', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/list') {
        return {
          data: [
            {
              id: 'archived-1',
              displayName: 'Archived thread',
              status: 'archived',
              originChannel: 'dotcraft',
              createdAt: '2026-04-11T06:00:00Z',
              lastActiveAt: '2026-04-11T08:00:00Z'
            }
          ]
        }
      }
      if (method === 'thread/unarchive') {
        throw new Error('boom')
      }
      return { channels: [] }
    })

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Archived Threads' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Restore' }))

    await waitFor(() => {
      expect(
        useToastStore
          .getState()
          .toasts.some((toast) => toast.message === 'Failed to restore conversation: boom')
      ).toBe(true)
    })
    expect(screen.getByText('Archived thread')).toBeInTheDocument()
  })
})
