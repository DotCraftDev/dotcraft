import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { SkillsView } from '../components/skills/SkillsView'
import { useSkillsStore } from '../stores/skillsStore'
import { useSkillMarketStore } from '../stores/skillMarketStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()
const skillMarketSearch = vi.fn()
const skillMarketDetail = vi.fn()
const skillMarketInstall = vi.fn()
const openExternal = vi.fn()

function renderView(): void {
  render(
    <LocaleProvider>
      <ConfirmDialogHost />
      <SkillsView />
    </LocaleProvider>
  )
}

describe('SkillsView marketplace browse and manage modes', () => {
  afterEach(() => {
    cleanup()
    vi.clearAllTimers()
  })

  beforeEach(() => {
    vi.clearAllMocks()
    useSkillsStore.setState({
      skills: [],
      loading: false,
      error: null,
      selectedSkillName: null,
      skillContent: null,
      contentLoading: false
    })
    useSkillMarketStore.setState({
      query: '',
      provider: 'all',
      results: [],
      loading: false,
      error: null,
      selectedSkill: null,
      detailLoading: false,
      installSlug: null
    })
    useThreadStore.getState().reset()
    useThreadStore.getState().setActiveThreadId('thread-existing')
    useUIStore.setState({
      activeMainView: 'skills',
      welcomeDraft: null
    })
    settingsGet.mockResolvedValue({ locale: 'en' })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'skills/list') {
        return {
          skills: [
            {
              name: 'memory',
              displayName: 'Memory',
              shortDescription: 'Remember project facts',
              description: 'Remember project facts',
              source: 'builtin',
              available: true,
              enabled: true,
              path: 'E:\\Git\\dotcraft\\.craft\\skills\\memory\\SKILL.md'
            },
            {
              name: 'git-local',
              displayName: 'Git Local',
              shortDescription: 'Local git workflows',
              description: 'Local git workflows',
              source: 'workspace',
              available: true,
              enabled: true,
              path: 'E:\\Git\\dotcraft\\.craft\\skills\\git-local\\SKILL.md'
            }
          ]
        }
      }
      if (method === 'skills/setEnabled') {
        return {
          skill: {
            name: 'memory',
            description: 'Remember project facts',
            source: 'builtin',
            available: true,
            enabled: false,
            path: 'E:\\Git\\dotcraft\\.craft\\skills\\memory\\SKILL.md'
          }
        }
      }
      return { content: '---\nname: memory\n---\n# Memory' }
    })
    skillMarketSearch.mockResolvedValue({
      skills: [
        {
          provider: 'clawhub',
          slug: 'git-helper',
          name: 'Git Helper',
          description: 'Git workflow help',
          version: '1.0.0',
          installed: false
        }
      ]
    })
    skillMarketDetail.mockResolvedValue({
      provider: 'clawhub',
      slug: 'git-helper',
      name: 'Git Helper',
      description: 'Git workflow help',
      readme: '# Git Helper\n\nUse git safely.',
      version: '1.0.0',
      installed: false
    })
    skillMarketInstall.mockResolvedValue({
      skillName: 'git-helper',
      targetDir: 'E:\\Git\\dotcraft\\.craft\\skills\\git-helper',
      overwritten: false
    })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        skillMarket: {
          search: skillMarketSearch,
          detail: skillMarketDetail,
          install: skillMarketInstall
        },
        shell: {
          openExternal,
          openPath: vi.fn()
        }
      }
    })
  })

  it('searches local and marketplace skills from the browse page without switches', async () => {
    renderView()

    expect(await screen.findByText('Give DotCraft the skills you need')).toBeInTheDocument()
    expect(await screen.findByText('Memory')).toBeInTheDocument()
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()

    fireEvent.change(screen.getByPlaceholderText('Search skills or install from Marketplace'), {
      target: { value: 'git' }
    })

    await waitFor(() => {
      expect(skillMarketSearch).toHaveBeenCalledWith({
        query: 'git',
        provider: 'all',
        limit: 24
      })
    })

    expect(await screen.findByText('Git Local')).toBeInTheDocument()
    expect(await screen.findByText('Git Helper')).toBeInTheDocument()
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()
  })

  it('filters browse skills with the custom source menu', async () => {
    renderView()

    expect(await screen.findByText('Memory')).toBeInTheDocument()
    expect(screen.getByText('Git Local')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Filter skills' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'System' }))

    expect(screen.getByText('Memory')).toBeInTheDocument()
    expect(screen.queryByText('Git Local')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Filter skills' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Personal' }))

    expect(screen.queryByText('Memory')).not.toBeInTheDocument()
    expect(screen.getByText('Git Local')).toBeInTheDocument()
  })

  it('shows the local skill detail switch, menu, and scroll body', async () => {
    renderView()

    fireEvent.click(await screen.findByText('Memory'))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'More actions' })).toBeInTheDocument()
    expect(screen.getByTestId('skill-detail-scroll-body')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('switch', { name: 'Toggle Memory skill' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/setEnabled', {
        name: 'memory',
        enabled: false
      })
    })
  })

  it('starts a new welcome draft with the selected skill tag from detail', async () => {
    renderView()

    fireEvent.click(await screen.findByText('Memory'))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Try in chat' }))

    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(useUIStore.getState().activeMainView).toBe('conversation')
    expect(useUIStore.getState().welcomeDraft).toMatchObject({
      text: '$memory',
      segments: [{ type: 'skill', skillName: 'memory' }],
      selectionStart: 1,
      selectionEnd: 1,
      mode: 'agent',
      model: 'Default',
      approvalPolicy: 'default'
    })
  })

  it('shows switches only after entering manage mode', async () => {
    renderView()

    expect(await screen.findByText('Memory')).toBeInTheDocument()
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Manage' }))

    expect(await screen.findByText('Skills 2')).toBeInTheDocument()
    expect(screen.getAllByRole('switch')).toHaveLength(2)
  })

  it('refreshes from the more actions menu', async () => {
    renderView()
    expect(await screen.findByText('Memory')).toBeInTheDocument()
    const initialCalls = appServerSendRequest.mock.calls.length

    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Refresh' }))

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.length).toBeGreaterThan(initialCalls)
    })
  })

  it('installs a marketplace skill and refreshes installed skills', async () => {
    renderView()
    fireEvent.change(await screen.findByPlaceholderText('Search skills or install from Marketplace'), {
      target: { value: 'git' }
    })
    fireEvent.click(await screen.findByText('Git Helper'))
    fireEvent.click(await screen.findByRole('button', { name: 'Install' }))

    await waitFor(() => {
      expect(skillMarketInstall).toHaveBeenCalledWith({
        provider: 'clawhub',
        slug: 'git-helper',
        version: '1.0.0',
        overwrite: false
      })
    })
    expect(appServerSendRequest).toHaveBeenCalledWith('skills/list', { includeUnavailable: true })
  })
})
