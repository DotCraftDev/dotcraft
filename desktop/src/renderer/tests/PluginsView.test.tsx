import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { PluginsView } from '../components/plugins/PluginsView'
import { useConnectionStore } from '../stores/connectionStore'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useSkillsStore, type SkillEntry } from '../stores/skillsStore'
import { useUIStore } from '../stores/uiStore'

const appServerSendRequest = vi.fn()
const settingsGet = vi.fn()
const shellOpenExternal = vi.fn()
const confirmDialog = vi.fn()

const browserUsePlugin: PluginEntry = {
  id: 'browser-use',
  displayName: 'Browser Use',
  description: 'Control the in-app browser with DotCraft',
  version: '1.0.0',
  enabled: false,
  installed: false,
  installable: true,
  removable: false,
  source: 'builtin',
  rootPath: '',
  interface: {
    displayName: 'Browser Use',
    shortDescription: 'Control the in-app browser with DotCraft',
    developerName: 'DotHarness',
    category: 'Coding'
  },
  functions: [{ name: 'NodeReplJs', namespace: 'node_repl', description: 'Evaluate JavaScript.' }],
  skills: [{ name: 'browser-use', description: 'Browser Use', enabled: false }],
  mcpServers: []
}

const localPlugin: PluginEntry = {
  id: 'external-process-echo',
  displayName: 'External Process Echo',
  description: 'Echo text through a plugin-owned local process.',
  version: '0.1.0',
  enabled: true,
  installed: true,
  installable: false,
  removable: true,
  source: 'workspace',
  rootPath: 'F:\\dotcraft\\.craft\\plugins\\external-process-echo',
  interface: {
    displayName: 'External Process Echo',
    shortDescription: 'Run an echo tool in a plugin process',
    developerName: 'Example Labs',
    category: 'Coding',
    websiteUrl: 'https://example.com/external-process-echo',
    privacyPolicyUrl: 'https://example.com/privacy',
    termsOfServiceUrl: 'https://example.com/terms'
  },
  functions: [{ name: 'EchoText', namespace: 'demo', description: 'Echo text.' }],
  skills: [{ name: 'external-process-echo', description: 'Echo plugin skill', enabled: true }],
  mcpServers: []
}

const mcpOnlyPlugin: PluginEntry = {
  id: 'review-tools-mcp',
  displayName: 'Review Tools MCP',
  description: 'Review workflows and MCP tools.',
  version: '0.1.0',
  enabled: true,
  installed: true,
  installable: false,
  removable: true,
  source: 'workspace',
  rootPath: 'F:\\dotcraft\\.craft\\plugins\\review-tools-mcp',
  interface: {
    displayName: 'Review Tools MCP',
    shortDescription: 'Review workflows and MCP tools.',
    developerName: 'Example Labs',
    category: 'Coding',
    defaultPrompt: 'Review this change.'
  },
  functions: [],
  skills: [],
  mcpServers: [
    {
      name: 'review',
      runtimeName: 'review-tools-mcp:review',
      transport: 'stdio',
      enabled: true,
      active: true
    }
  ]
}

const memorySkill: SkillEntry = {
  name: 'memory',
  displayName: 'Memory',
  shortDescription: 'Remember project facts',
  description: 'Remember project facts',
  source: 'builtin',
  available: true,
  enabled: true,
  path: 'F:\\dotcraft\\.craft\\skills\\memory\\SKILL.md'
}

const gitSkill: SkillEntry = {
  name: 'git-local',
  displayName: 'Git Local',
  shortDescription: 'Local git workflows',
  description: 'Local git workflows',
  source: 'workspace',
  available: true,
  enabled: true,
  path: 'F:\\dotcraft\\.craft\\skills\\git-local\\SKILL.md'
}

function renderPluginsView(): void {
  render(
    <LocaleProvider>
      <PluginsView />
    </LocaleProvider>
  )
}

describe('PluginsView local plugin visibility', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    useConnectionStore.getState().reset()
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: {
        pluginManagement: true
      }
    })
    usePluginStore.setState({
      plugins: [],
      diagnostics: [],
      loading: false,
      error: null,
      selectedPluginId: null,
      selectedPlugin: null,
      detailLoading: false
    })
    useSkillsStore.setState({
      skills: [],
      loading: false,
      error: null,
      selectedSkillName: null,
      skillContent: null,
      contentLoading: false
    })
    useUIStore.setState({ welcomeDraft: null, activeMainView: 'conversation' })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        shell: { openExternal: shellOpenExternal }
      }
    })
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirmDialog
    shellOpenExternal.mockResolvedValue(undefined)
    confirmDialog.mockResolvedValue(true)
  })

  it('shows workspace plugins by default under Installed locally', async () => {
    appServerSendRequest.mockResolvedValue({
      plugins: [browserUsePlugin, localPlugin],
      diagnostics: []
    })

    renderPluginsView()

    expect(await screen.findByText('Installed locally')).toBeInTheDocument()
    expect(screen.getByText('External Process Echo')).toBeInTheDocument()
    expect(screen.getByText('Browser Use')).toBeInTheDocument()
    expect(screen.getByText('All publishers')).toBeInTheDocument()
  })

  it('renders plugin diagnostics returned by plugin/list', async () => {
    appServerSendRequest.mockResolvedValue({
      plugins: [browserUsePlugin],
      diagnostics: [
        {
          severity: 'error',
          code: 'MissingPluginCapabilities',
          message: 'Plugin manifest must declare a skills path or at least one tool.',
          pluginId: 'broken-plugin',
          path: 'F:\\dotcraft\\.craft\\plugins\\broken-plugin\\.craft-plugin\\plugin.json'
        }
      ]
    })

    renderPluginsView()

    expect(await screen.findByText('Plugin diagnostics')).toBeInTheDocument()
    expect(screen.getByText('MissingPluginCapabilities')).toBeInTheDocument()
    expect(screen.getByText('Plugin manifest must declare a skills path or at least one tool.')).toBeInTheDocument()
    expect(usePluginStore.getState().diagnostics).toHaveLength(1)
  })

  it('refreshes plugins when the window regains focus', async () => {
    appServerSendRequest
      .mockResolvedValueOnce({ plugins: [browserUsePlugin], diagnostics: [] })
      .mockResolvedValueOnce({ plugins: [browserUsePlugin, localPlugin], diagnostics: [] })

    renderPluginsView()

    expect(await screen.findByText('Browser Use')).toBeInTheDocument()
    expect(screen.queryByText('External Process Echo')).not.toBeInTheDocument()

    fireEvent.focus(window)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledTimes(2)
    })
    expect(await screen.findByText('External Process Echo')).toBeInTheDocument()
  })

  it('refreshes plugins from the more actions menu', async () => {
    appServerSendRequest.mockResolvedValue({
      plugins: [browserUsePlugin],
      diagnostics: []
    })

    renderPluginsView()

    expect(await screen.findByText('Browser Use')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Refresh' })).not.toBeInTheDocument()
    const initialCalls = appServerSendRequest.mock.calls.length

    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Refresh' }))

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.length).toBeGreaterThan(initialCalls)
    })
  })

  it('shows remove for removable local plugins and refreshes after confirmation', async () => {
    let removed = false
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') {
        return { plugins: removed ? [browserUsePlugin] : [browserUsePlugin, localPlugin], diagnostics: [] }
      }
      if (method === 'plugin/view') return { plugin: localPlugin }
      if (method === 'plugin/remove') {
        removed = true
        return {}
      }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('External Process Echo'))
    fireEvent.click(await screen.findByRole('button', { name: 'Remove from DotCraft' }))

    await waitFor(() => {
      expect(confirmDialog).toHaveBeenCalledWith(expect.objectContaining({ danger: true }))
      expect(appServerSendRequest).toHaveBeenCalledWith('plugin/remove', { id: 'external-process-echo' })
    })
    expect(screen.queryByRole('button', { name: 'Remove from DotCraft' })).not.toBeInTheDocument()
  })

  it('hides remove for installed plugins that are not removable', async () => {
    const externalRootPlugin = { ...localPlugin, removable: false, source: 'explicit' }
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [externalRootPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: externalRootPlugin }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('External Process Echo'))

    expect(await screen.findByRole('button', { name: 'Try in chat' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove from DotCraft' })).not.toBeInTheDocument()
  })

  it('opens plugin detail links in the external browser', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [localPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: localPlugin }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('External Process Echo'))
    fireEvent.click((await screen.findAllByLabelText('Website'))[0]!)
    fireEvent.click(await screen.findByLabelText('Privacy policy'))

    expect(shellOpenExternal).toHaveBeenCalledWith('https://example.com/external-process-echo')
    expect(shellOpenExternal).toHaveBeenCalledWith('https://example.com/privacy')
  })

  it('keeps manage mode while switching between plugin and skill tabs', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') {
        return { plugins: [browserUsePlugin, localPlugin], diagnostics: [] }
      }
      if (method === 'skills/list') {
        return { skills: [memorySkill, gitSkill] }
      }
      return {}
    })

    renderPluginsView()

    expect(await screen.findByText('Browser Use')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Manage' }))

    expect(await screen.findByText('Plugins 2')).toBeInTheDocument()
    expect(await screen.findByText('Skills 2')).toBeInTheDocument()
    expect(screen.queryByText('Apps 0')).not.toBeInTheDocument()
    expect(screen.queryByText('MCP 0')).not.toBeInTheDocument()
    const pluginsTab = screen.getByRole('button', { name: 'Plugins 2' })
    const skillsTab = screen.getByRole('button', { name: 'Skills 2' })
    expect(pluginsTab).toBeInTheDocument()
    expect(skillsTab).toBeInTheDocument()

    fireEvent.click(skillsTab)

    expect(await screen.findByText('Skills 2')).toBeInTheDocument()
    expect(screen.getByPlaceholderText('Search installed skills')).toBeInTheDocument()
    expect(screen.getByText('Memory')).toBeInTheDocument()
    expect(screen.getByText('Git Local')).toBeInTheDocument()
    expect(screen.getAllByRole('switch')).toHaveLength(2)

    fireEvent.click(screen.getByRole('button', { name: 'Plugins 2' }))

    expect(await screen.findByText('Plugins 2')).toBeInTheDocument()
    expect(await screen.findByPlaceholderText('Search installed plugins')).toBeInTheDocument()
    expect(screen.getByText('External Process Echo')).toBeInTheDocument()
  })

  it('shows plugin-bundled MCP content on plugin details', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [mcpOnlyPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: mcpOnlyPlugin }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('Review Tools MCP'))

    expect(await screen.findByText('review-tools-mcp:review')).toBeInTheDocument()
    expect(screen.getByText('MCP server')).toBeInTheDocument()
    expect(screen.getByText('STDIO · Active')).toBeInTheDocument()
  })

  it('does not generate a skill mention for MCP-only plugin try in chat', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [mcpOnlyPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: mcpOnlyPlugin }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('Review Tools MCP'))
    fireEvent.click(await screen.findByRole('button', { name: 'Try in chat' }))

    expect(useUIStore.getState().welcomeDraft?.text).toBe('Review this change.')
    expect(useUIStore.getState().welcomeDraft?.segments).toEqual([])
  })
})
