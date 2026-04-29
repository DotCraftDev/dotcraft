import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SettingsView } from '../components/settings/SettingsView'
import { useConnectionStore } from '../stores/connectionStore'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const workspaceConfigGetCore = vi.fn()
const appServerSendRequest = vi.fn()
const appServerRestartManaged = vi.fn()

function renderView(): void {
  render(
    <LocaleProvider>
      <SettingsView workspacePath="E:\\Git\\dotcraft" />
    </LocaleProvider>
  )
}

describe('SettingsView self-learning settings', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    delete (window as Window & { __confirmDialog?: unknown }).__confirmDialog

    const core = {
      workspace: {
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: false,
        memoryAutoConsolidateEnabled: false,
        defaultApprovalPolicy: 'default'
      },
      userDefaults: {
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        memoryAutoConsolidateEnabled: null,
        defaultApprovalPolicy: null
      }
    }

    settingsGet.mockResolvedValue({ locale: 'en', connectionMode: 'stdio', visibleChannels: [] })
    settingsSet.mockResolvedValue(undefined)
    workspaceConfigGetCore.mockImplementation(async () => core)
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'workspace/config/update') {
        if (typeof params?.defaultApprovalPolicy === 'string') {
          core.workspace.defaultApprovalPolicy = params.defaultApprovalPolicy
          return { defaultApprovalPolicy: core.workspace.defaultApprovalPolicy }
        }
        if (typeof params?.memoryAutoConsolidateEnabled === 'boolean') {
          core.workspace.memoryAutoConsolidateEnabled = params.memoryAutoConsolidateEnabled
          return { memoryAutoConsolidateEnabled: core.workspace.memoryAutoConsolidateEnabled }
        }
        core.workspace.skillsSelfLearningEnabled = params?.skillsSelfLearningEnabled === true
        return { skillsSelfLearningEnabled: core.workspace.skillsSelfLearningEnabled }
      }
      if (method === 'channel/list') {
        return { channels: [] }
      }
      return {}
    })
    appServerRestartManaged.mockResolvedValue(undefined)

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet, set: settingsSet },
        workspaceConfig: { getCore: workspaceConfigGetCore },
        appServer: {
          sendRequest: appServerSendRequest,
          restartManaged: appServerRestartManaged,
          getResolvedBinary: vi.fn().mockResolvedValue({ path: null }),
          pickBinary: vi.fn()
        },
        proxy: {
          getResolvedBinary: vi.fn().mockResolvedValue({ path: null }),
          getStatus: vi.fn().mockResolvedValue({ status: 'stopped' }),
          listAuthFiles: vi.fn().mockResolvedValue([]),
          pickBinary: vi.fn(),
          restartManaged: vi.fn(),
          startOAuth: vi.fn(),
          getAuthStatus: vi.fn(),
          getUsageSummary: vi.fn().mockResolvedValue({
            totalRequests: 0,
            successCount: 0,
            failureCount: 0,
            totalTokens: 0,
            failedRequests: 0
          })
        },
        modules: { list: vi.fn().mockResolvedValue([]) },
        workspace: {
          pickFolder: vi.fn(),
          viewer: { browserUse: { clearCookies: vi.fn() } }
        },
        shell: { openExternal: vi.fn() }
      }
    })

    useConnectionStore.getState().reset()
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        workspaceConfigManagement: true
      }
    })
  })

  it('saves self-learning toggle, shows restart banner, and restarts managed AppServer', async () => {
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))
    const toggle = await screen.findByRole('switch', { name: 'Enable self-learning' })
    expect(toggle).toHaveAttribute('aria-checked', 'false')

    fireEvent.click(toggle)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        skillsSelfLearningEnabled: true
      })
    })
    expect(await screen.findByText('Self-learning changes are saved. Restart AppServer to apply them.')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Restart now/ }))

    await waitFor(() => {
      expect(appServerRestartManaged).toHaveBeenCalledOnce()
    })
  })

  it('defaults self-learning on when workspace and user defaults are unset', async () => {
    workspaceConfigGetCore.mockResolvedValueOnce({
      workspace: {
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        memoryAutoConsolidateEnabled: null,
        defaultApprovalPolicy: null
      },
      userDefaults: {
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        memoryAutoConsolidateEnabled: null,
        defaultApprovalPolicy: null
      }
    })

    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))
    const toggle = await screen.findByRole('switch', { name: 'Enable self-learning' })

    expect(toggle).toHaveAttribute('aria-checked', 'true')
  })

  it('saves long-term memory toggle without restart banner', async () => {
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))
    const toggle = await screen.findByRole('switch', { name: 'Enable long-term memory' })
    expect(toggle).toHaveAttribute('aria-checked', 'false')

    fireEvent.click(toggle)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        memoryAutoConsolidateEnabled: true
      })
    })
    expect(screen.queryByText('Self-learning changes are saved. Restart AppServer to apply them.')).not.toBeInTheDocument()
  })

  it('warns and saves full access default approval policy', async () => {
    const confirm = vi.fn().mockResolvedValue(true)
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirm

    renderView()

    const approvalSelect = await screen.findByRole('combobox', { name: 'Workspace default permissions' }) as HTMLSelectElement
    expect(approvalSelect.value).toBe('default')

    fireEvent.change(approvalSelect, { target: { value: 'autoApprove' } })

    await waitFor(() => {
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ danger: true }))
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        defaultApprovalPolicy: 'autoApprove'
      })
    })
  })

  it('keeps default approval policy when full access warning is cancelled', async () => {
    const confirm = vi.fn().mockResolvedValue(false)
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirm

    renderView()

    const approvalSelect = await screen.findByRole('combobox', { name: 'Workspace default permissions' }) as HTMLSelectElement
    expect(approvalSelect.value).toBe('default')

    fireEvent.change(approvalSelect, { target: { value: 'autoApprove' } })

    await waitFor(() => {
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ danger: true }))
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('workspace/config/update', {
      defaultApprovalPolicy: 'autoApprove'
    })
    expect(approvalSelect.value).toBe('default')
  })
})
