import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { SettingsView } from '../components/settings/SettingsView'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useToastStore } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'
import { useConnectionStore } from '../stores/connectionStore'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const appServerSendRequest = vi.fn()
const appServerRestartManaged = vi.fn()
const appServerGetResolvedBinary = vi.fn()
const appServerOnNotification = vi.fn()
const modulesList = vi.fn()
const modulesUserDirectory = vi.fn()
const modulesCheckDirectory = vi.fn()
const modulesRescan = vi.fn()
const proxyGetStatus = vi.fn()
const proxyGetUsageSummary = vi.fn()
const proxyRestartManaged = vi.fn()
const proxyGetResolvedBinary = vi.fn()
const proxyPickBinary = vi.fn()
const proxyGetAuthStatus = vi.fn()
const proxyListAuthFiles = vi.fn()
const proxyStartOAuth = vi.fn()
const workspacePickFolder = vi.fn()
const shellOpenExternal = vi.fn()

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
    useConnectionStore.setState({
      status: 'connected',
      dashboardUrl: 'https://dashboard.example.test',
      capabilities: null
    })

    settingsGet.mockResolvedValue({
      connectionMode: 'stdio',
      webSocket: { host: '127.0.0.1', port: 9100 },
      locale: 'en',
      visibleChannels: [],
      proxy: { enabled: true, port: 8317 }
    })
    settingsSet.mockResolvedValue(undefined)
    appServerSendRequest.mockResolvedValue({ channels: [] })
    appServerRestartManaged.mockResolvedValue(undefined)
    appServerGetResolvedBinary.mockResolvedValue({ source: 'bundled', path: 'dotcraft' })
    appServerOnNotification.mockReturnValue(() => {})
    modulesList.mockResolvedValue([])
    modulesUserDirectory.mockResolvedValue({ path: 'C:\\Users\\Administrator\\.craft\\modules' })
    modulesCheckDirectory.mockResolvedValue({ exists: true })
    modulesRescan.mockResolvedValue(undefined)
    proxyGetStatus.mockResolvedValue({ status: 'stopped', errorMessage: '' })
    proxyGetUsageSummary.mockResolvedValue({
      totalRequests: 10,
      successCount: 8,
      failureCount: 2,
      totalTokens: 3200
    })
    proxyRestartManaged.mockResolvedValue(undefined)
    proxyGetResolvedBinary.mockResolvedValue({ source: 'bundled', path: 'cliproxyapi' })
    proxyPickBinary.mockResolvedValue('C:\\cliproxyapi.exe')
    proxyGetAuthStatus.mockResolvedValue({ status: 'idle' })
    proxyListAuthFiles.mockResolvedValue([])
    proxyStartOAuth.mockResolvedValue({ state: 'oauth-state', url: 'https://auth.example.test' })
    workspacePickFolder.mockResolvedValue('C:\\picked')
    shellOpenExternal.mockResolvedValue(undefined)

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: settingsGet,
          set: settingsSet
        },
        appServer: {
          sendRequest: appServerSendRequest,
          getResolvedBinary: appServerGetResolvedBinary,
          restartManaged: appServerRestartManaged,
          onNotification: appServerOnNotification
        },
        modules: {
          list: modulesList,
          userDirectory: modulesUserDirectory,
          checkDirectory: modulesCheckDirectory,
          rescan: modulesRescan
        },
        proxy: {
          getStatus: proxyGetStatus,
          getUsageSummary: proxyGetUsageSummary,
          restartManaged: proxyRestartManaged,
          getResolvedBinary: proxyGetResolvedBinary,
          pickBinary: proxyPickBinary,
          getAuthStatus: proxyGetAuthStatus,
          listAuthFiles: proxyListAuthFiles,
          startOAuth: proxyStartOAuth
        },
        workspace: {
          pickFolder: workspacePickFolder
        },
        shell: {
          openExternal: shellOpenExternal
        }
      }
    })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('shows restart button for Desktop-managed connection modes', async () => {
    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))

    expect(await screen.findByText('AppServer controls')).toBeInTheDocument()
    expect(screen.getByText('Restart the Desktop-managed AppServer to apply saved connection changes immediately.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Restart AppServer' })).toHaveTextContent('Restart')
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

  it('shows AppServer restart progress in the button label while restarting', async () => {
    appServerRestartManaged.mockImplementation(
      () => new Promise<void>((resolve) => setTimeout(resolve, 50))
    )

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Restart AppServer' }))

    const restartingButton = await screen.findByRole('button', { name: 'Restarting…' })
    expect(restartingButton).toBeDisabled()
    expect(restartingButton).toHaveTextContent('Restarting…')
  })

  it('shows proxy restart as a labeled action row and disables it while restarting', async () => {
    proxyRestartManaged.mockImplementation(
      () => new Promise<void>((resolve) => setTimeout(resolve, 50))
    )

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))

    expect(await screen.findByText('Restart API Proxy')).toBeInTheDocument()
    expect(screen.getByText('Restart the local API proxy to apply the latest runtime configuration immediately.')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Restart API Proxy' }))

    const restartingButton = await screen.findByRole('button', { name: 'Restarting…' })
    expect(restartingButton).toBeDisabled()
    expect(restartingButton).toHaveTextContent('Restarting…')
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

  it('keeps dashboard in usage tab without rendering proxy usage cards there', async () => {
    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Usage' }))

    expect(await screen.findByText('Dashboard')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Open Dashboard' }))

    await waitFor(() => {
      expect(shellOpenExternal).toHaveBeenCalledWith('https://dashboard.example.test')
    })
    expect(screen.queryByRole('button', { name: 'Refresh usage' })).not.toBeInTheDocument()
    expect(screen.queryByText('Requests')).not.toBeInTheDocument()
  })

  it('renders proxy usage cards inside API proxy settings', async () => {
    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Refresh usage' }))

    await waitFor(() => {
      expect(proxyGetUsageSummary).toHaveBeenCalled()
      expect(screen.getByText('Requests')).toBeInTheDocument()
      expect(screen.getByText('10')).toBeInTheDocument()
    })
  })

  it('shows authenticated when auth files already contain a ready codex entry', async () => {
    proxyGetStatus.mockResolvedValue({ status: 'running', errorMessage: '' })
    proxyListAuthFiles.mockResolvedValue([
      {
        provider: 'codex',
        status: 'ready',
        statusMessage: 'ok',
        disabled: false,
        unavailable: false,
        runtimeOnly: false,
        name: 'codex-user.json'
      }
    ])

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))

    expect(await screen.findByText('Authenticated')).toBeInTheDocument()
  })

  it('shows authenticated when auth files contain an active codex entry', async () => {
    proxyGetStatus.mockResolvedValue({ status: 'running', errorMessage: '' })
    proxyListAuthFiles.mockResolvedValue([
      {
        provider: 'codex',
        status: 'active',
        statusMessage: '',
        disabled: false,
        unavailable: false,
        runtimeOnly: false,
        name: 'codex-user.json'
      }
    ])

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))

    expect(await screen.findByText('Authenticated')).toBeInTheDocument()
    expect(screen.queryByText('Not authenticated')).not.toBeInTheDocument()
  })

  it('does not treat disabled active auth files as authenticated', async () => {
    proxyGetStatus.mockResolvedValue({ status: 'running', errorMessage: '' })
    proxyListAuthFiles.mockResolvedValue([
      {
        provider: 'codex',
        status: 'active',
        statusMessage: '',
        disabled: true,
        unavailable: false,
        runtimeOnly: false,
        name: 'codex-user.json'
      }
    ])

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))

    expect(await screen.findByText('Checking authentication')).toBeInTheDocument()
    expect(screen.queryByText('Authenticated')).not.toBeInTheDocument()
  })

  it('shows checking while proxy startup auth state is still being restored', async () => {
    proxyGetStatus.mockResolvedValue({ status: 'running', errorMessage: '' })
    proxyListAuthFiles.mockResolvedValue([])

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))

    expect(await screen.findByText('Checking authentication')).toBeInTheDocument()
    expect(screen.queryByText('Not authenticated')).not.toBeInTheDocument()
  })

  it('retries auth file refresh after proxy status transitions from starting to running', async () => {
    proxyGetStatus
      .mockResolvedValueOnce({ status: 'starting', errorMessage: '' })
      .mockResolvedValueOnce({ status: 'running', errorMessage: '' })
    proxyListAuthFiles.mockResolvedValue([
      {
        provider: 'codex',
        status: 'ready',
        statusMessage: 'ok',
        disabled: false,
        unavailable: false,
        runtimeOnly: false,
        name: 'codex-user.json'
      }
    ])

    renderSettingsView()
    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))

    expect(proxyListAuthFiles).not.toHaveBeenCalled()

    await waitFor(
      () => {
        expect(screen.getByText('Authenticated')).toBeInTheDocument()
        expect(proxyGetStatus).toHaveBeenCalledTimes(2)
        expect(proxyListAuthFiles).toHaveBeenCalledTimes(1)
      },
      { timeout: 2500 }
    )
  }, 8000)

  it('retries auth file refresh after an initial list failure while proxy is running', async () => {
    proxyGetStatus.mockResolvedValue({ status: 'running', errorMessage: '' })
    proxyListAuthFiles
      .mockRejectedValueOnce(new Error('not ready'))
      .mockResolvedValueOnce([
        {
          provider: 'codex',
          status: 'ready',
          statusMessage: 'ok',
          disabled: false,
          unavailable: false,
          runtimeOnly: false,
          name: 'codex-user.json'
        }
      ])

    renderSettingsView()
    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))

    await waitFor(
      () => {
        expect(screen.getByText('Authenticated')).toBeInTheDocument()
        expect(proxyListAuthFiles).toHaveBeenCalledTimes(2)
      },
      { timeout: 2500 }
    )
  }, 8000)

  it('keeps checking until auth files appear after initial empty startup reads', async () => {
    proxyGetStatus.mockResolvedValue({ status: 'running', errorMessage: '' })
    proxyListAuthFiles
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([
        {
          provider: 'codex',
          status: 'ready',
          statusMessage: 'ok',
          disabled: false,
          unavailable: false,
          runtimeOnly: false,
          name: 'codex-user.json'
        }
      ])

    renderSettingsView()
    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))
    expect(await screen.findByText('Checking authentication')).toBeInTheDocument()

    await waitFor(
      () => {
        expect(screen.getByText('Authenticated')).toBeInTheDocument()
        expect(proxyListAuthFiles).toHaveBeenCalledTimes(3)
      },
      { timeout: 2500 }
    )
  }, 8000)

  it('falls back to not authenticated only after the startup recovery window expires', async () => {
    proxyGetStatus.mockResolvedValue({ status: 'running', errorMessage: '' })
    proxyListAuthFiles.mockResolvedValue([])

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))
    expect(await screen.findByText('Checking authentication')).toBeInTheDocument()

    await waitFor(
      () => {
        expect(screen.getByText('Not authenticated')).toBeInTheDocument()
      },
      { timeout: 6000 }
    )
    expect(proxyListAuthFiles).toHaveBeenCalledTimes(5)
  }, 10000)

  it('shows authenticated when auth files come from fallback auth-dir scan metadata', async () => {
    proxyGetStatus.mockResolvedValue({ status: 'running', errorMessage: '' })
    proxyListAuthFiles.mockResolvedValue([
      {
        provider: 'codex',
        status: 'ready',
        statusMessage: 'fallback auth-dir scan',
        disabled: false,
        unavailable: false,
        runtimeOnly: false,
        name: 'codex-user.json'
      }
    ])

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))

    expect(await screen.findByText('Authenticated')).toBeInTheDocument()
  })

  it('keeps authenticated when OAuth polling errors after auth file becomes ready', async () => {
    proxyGetStatus.mockResolvedValue({ status: 'running', errorMessage: '' })
    proxyGetAuthStatus.mockResolvedValue({ status: 'error', error: 'Authentication failed' })
    proxyListAuthFiles
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([
        {
          provider: 'codex',
          status: 'ready',
          statusMessage: 'ok',
          disabled: false,
          unavailable: false,
          runtimeOnly: false,
          name: 'codex-user.json'
        }
      ])

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))
    fireEvent.click((await screen.findAllByRole('button', { name: 'Login' }))[0])

    await waitFor(() => {
      expect(proxyStartOAuth).toHaveBeenCalledWith('codex')
      expect(screen.getByText('Authenticated')).toBeInTheDocument()
    })
    expect(screen.queryByText('Auth failed')).not.toBeInTheDocument()
  })

  it('treats status ok as authenticated even before auth files refresh catches up', async () => {
    proxyGetAuthStatus.mockResolvedValue({ status: 'ok' })
    proxyListAuthFiles.mockResolvedValue([])

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))
    fireEvent.click((await screen.findAllByRole('button', { name: 'Login' }))[0])

    await waitFor(() => {
      expect(screen.getByText('Authenticated')).toBeInTheDocument()
    })
    expect(screen.queryByText('Auth failed')).not.toBeInTheDocument()
    expect(screen.queryByText(/^ok$/)).not.toBeInTheDocument()
  })

  it('treats timeout as success when auth files become ready before the final check', async () => {
    proxyGetStatus.mockResolvedValue({ status: 'running', errorMessage: '' })
    proxyGetAuthStatus.mockResolvedValue({ status: 'wait' })
    proxyListAuthFiles
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([
        {
          provider: 'codex',
          status: 'ready',
          statusMessage: 'ok',
          disabled: false,
          unavailable: false,
          runtimeOnly: false,
          name: 'codex-user.json'
        }
      ])

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))
    const loginButton = (await screen.findAllByRole('button', { name: 'Login' }))[0]
    vi.useFakeTimers()

    try {
      await act(async () => {
        fireEvent.click(loginButton)
        await Promise.resolve()
        await vi.advanceTimersByTimeAsync(180000)
      })

      expect(screen.getByText('Authenticated')).toBeInTheDocument()
      expect(screen.queryByText(/OAuth timed out waiting for the browser callback\./)).not.toBeInTheDocument()
    } finally {
      vi.useRealTimers()
    }
  }, 8000)

  it('surfaces an actionable timeout only when auth files never become ready', async () => {
    proxyGetAuthStatus.mockResolvedValue({ status: 'wait' })
    proxyListAuthFiles.mockResolvedValue([])

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'API Proxy' }))
    const loginButton = (await screen.findAllByRole('button', { name: 'Login' }))[0]
    vi.useFakeTimers()

    try {
      await act(async () => {
        fireEvent.click(loginButton)
        await Promise.resolve()
        await vi.advanceTimersByTimeAsync(180000)
      })

      expect(proxyStartOAuth).toHaveBeenCalledWith('codex')
      expect(proxyGetAuthStatus).toHaveBeenCalledTimes(150)
      expect(screen.getByText(/OAuth timed out waiting for the browser callback\./)).toBeInTheDocument()
    } finally {
      vi.useRealTimers()
    }
  }, 8000)

  it('hides modules directory missing warning and shows compact binary available status', async () => {
    modulesCheckDirectory.mockResolvedValue({ exists: false })

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))

    expect(await screen.findByText('Available')).toBeInTheDocument()
    expect(screen.queryByText('Directory does not exist. Create it or choose another path.')).not.toBeInTheDocument()
    expect(screen.queryByText('Resolved: dotcraft')).not.toBeInTheDocument()
  })
})
