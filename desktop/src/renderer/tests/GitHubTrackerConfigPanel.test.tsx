import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { GitHubTrackerConfigPanel } from '../components/automations/GitHubTrackerConfigPanel'

const settingsGet = vi.fn()
const getWorkspacePath = vi.fn()
const appServerSendRequest = vi.fn()
const fileExists = vi.fn()
const shellOpenPath = vi.fn()
const onBack = vi.fn()

interface TestGitHubTrackerConfig {
  enabled: boolean
  issuesWorkflowPath: string
  pullRequestWorkflowPath: string
  tracker: {
    endpoint: string | null
    apiKey: string | null
    repository: string | null
    activeStates: string[]
    terminalStates: string[]
    gitHubStateLabelPrefix: string
    assigneeFilter: string | null
    pullRequestActiveStates: string[]
    pullRequestTerminalStates: string[]
  }
  polling: {
    intervalMs: number
  }
  workspace: {
    root: string | null
  }
  agent: {
    maxConcurrentAgents: number
    maxTurns: number
    maxRetryBackoffMs: number
    turnTimeoutMs: number
    stallTimeoutMs: number
    maxConcurrentByState: Record<string, number>
    maxConcurrentPullRequestAgents: number
  }
  hooks: {
    afterCreate: string | null
    beforeRun: string | null
    afterRun: string | null
    beforeRemove: string | null
    timeoutMs: number
  }
}

function createConfig(overrides: Partial<TestGitHubTrackerConfig> = {}): TestGitHubTrackerConfig {
  return {
    enabled: true,
    issuesWorkflowPath: '.craft/WORKFLOW.md',
    pullRequestWorkflowPath: '.craft/PR_WORKFLOW.md',
    tracker: {
      endpoint: null,
      apiKey: 'secret-token',
      repository: 'DotHarness/dotcraft',
      activeStates: ['Todo', 'In Progress'],
      terminalStates: ['Done'],
      gitHubStateLabelPrefix: 'status:',
      assigneeFilter: null,
      pullRequestActiveStates: ['Pending Review'],
      pullRequestTerminalStates: ['Merged']
    },
    polling: {
      intervalMs: 30_000
    },
    workspace: {
      root: null
    },
    agent: {
      maxConcurrentAgents: 3,
      maxTurns: 20,
      maxRetryBackoffMs: 300_000,
      turnTimeoutMs: 3_600_000,
      stallTimeoutMs: 300_000,
      maxConcurrentByState: {},
      maxConcurrentPullRequestAgents: 0
    },
    hooks: {
      afterCreate: null,
      beforeRun: null,
      afterRun: null,
      beforeRemove: null,
      timeoutMs: 60_000
    },
    ...overrides
  }
}

function renderPanel(): void {
  render(
    <LocaleProvider>
      <GitHubTrackerConfigPanel onBack={onBack} />
    </LocaleProvider>
  )
}

function mockApi(config: TestGitHubTrackerConfig): void {
  settingsGet.mockResolvedValue({ locale: 'en' })
  getWorkspacePath.mockResolvedValue('F:/repo')
  appServerSendRequest.mockImplementation(async (method: string, payload?: unknown) => {
    if (method === 'githubTracker/get') return { config }
    if (method === 'githubTracker/update') {
      return { config: (payload as { config: TestGitHubTrackerConfig }).config }
    }
    return {}
  })
  shellOpenPath.mockResolvedValue('')

  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      settings: { get: settingsGet },
      window: { getWorkspacePath },
      appServer: { sendRequest: appServerSendRequest },
      file: { exists: fileExists, writeFile: vi.fn().mockResolvedValue(undefined) },
      shell: { openPath: shellOpenPath }
    }
  })
}

describe('GitHubTrackerConfigPanel', () => {
  afterEach(() => {
    cleanup()
  })

  beforeEach(() => {
    vi.clearAllMocks()
    fileExists.mockResolvedValue(false)
  })

  it('renders the loaded GitHub config and workflow status actions', async () => {
    const config = createConfig()
    mockApi(config)
    fileExists.mockImplementation(async (path: string) => path.endsWith('WORKFLOW.md') && !path.endsWith('PR_WORKFLOW.md'))

    renderPanel()

    expect(await screen.findByRole('heading', { name: 'GitHub Integration' })).toBeInTheDocument()
    expect(screen.getByText('Connection')).toBeInTheDocument()
    expect(screen.getByText('Workflow files')).toBeInTheDocument()
    expect(screen.getByLabelText('Repository')).toHaveValue('DotHarness/dotcraft')
    expect(screen.getByLabelText('API key')).toHaveValue('secret-token')
    expect(screen.getByLabelText('Issues workflow path')).toHaveValue('.craft/WORKFLOW.md')
    expect(screen.getByLabelText('Pull request workflow path')).toHaveValue('.craft/PR_WORKFLOW.md')
    expect(await screen.findByText('Created')).toBeInTheDocument()
    expect(screen.getByText('Not created')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Create template/ })).toBeInTheDocument()
  })

  it('disables fields when GitHub integration is off and re-enables them from the switch', async () => {
    const config = createConfig({ enabled: false })
    mockApi(config)

    renderPanel()

    await screen.findByRole('heading', { name: 'GitHub Integration' })
    const repositoryInput = screen.getByLabelText('Repository')
    const apiKeyInput = screen.getByLabelText('API key')

    expect(repositoryInput).toBeDisabled()
    expect(apiKeyInput).toBeDisabled()

    fireEvent.click(screen.getByRole('switch', { name: 'Enable GitHub integration' }))

    await waitFor(() => {
      expect(repositoryInput).not.toBeDisabled()
      expect(apiKeyInput).not.toBeDisabled()
    })
  })

  it('shows create, open, and replace workflow actions based on file existence', async () => {
    const config = createConfig()
    mockApi(config)
    fileExists.mockImplementation(async (path: string) => path.endsWith('PR_WORKFLOW.md'))

    renderPanel()

    await screen.findByRole('heading', { name: 'GitHub Integration' })
    await screen.findByRole('button', { name: /Open/ })

    expect(screen.getAllByRole('button', { name: /Create template/ })).toHaveLength(1)
    expect(screen.getByRole('button', { name: /Open/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Replace template/ })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Open/ }))

    await waitFor(() => {
      expect(shellOpenPath).toHaveBeenCalledWith('F:/repo/.craft/PR_WORKFLOW.md')
    })
  })

  it('saves the current config through githubTracker/update', async () => {
    const config = createConfig()
    mockApi(config)

    renderPanel()

    const repositoryInput = await screen.findByLabelText('Repository')
    fireEvent.change(repositoryInput, { target: { value: 'DotHarness/next' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('githubTracker/update', {
        config: expect.objectContaining({
          tracker: expect.objectContaining({
            repository: 'DotHarness/next'
          })
        })
      })
    })
  })

  it('keeps the workflow template dialog entry point wired', async () => {
    const config = createConfig()
    mockApi(config)
    fileExists.mockResolvedValue(false)

    renderPanel()

    await screen.findByRole('heading', { name: 'GitHub Integration' })
    fireEvent.click(screen.getAllByRole('button', { name: /Create template/ })[0])

    expect(await screen.findByText('Workflow template')).toBeInTheDocument()
  })
})
