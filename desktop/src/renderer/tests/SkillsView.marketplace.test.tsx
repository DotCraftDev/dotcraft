import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { SkillsView } from '../components/skills/SkillsView'
import { useSkillsStore } from '../stores/skillsStore'
import { useSkillMarketStore } from '../stores/skillMarketStore'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()
const skillMarketSearch = vi.fn()
const skillMarketDetail = vi.fn()
const openExternal = vi.fn()

function renderView(): void {
  render(
    <LocaleProvider>
      <ConfirmDialogHost />
      <SkillsView />
    </LocaleProvider>
  )
}

describe('SkillsView marketplace tab', () => {
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
    settingsGet.mockResolvedValue({ locale: 'en' })
    appServerSendRequest.mockResolvedValue({
      skills: [
        {
          name: 'memory',
          description: 'Remember project facts',
          source: 'workspace',
          available: true,
          enabled: true,
          path: 'E:\\Git\\dotcraft\\.craft\\skills\\memory\\SKILL.md'
        }
      ]
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
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        skillMarket: {
          search: skillMarketSearch,
          detail: skillMarketDetail,
          install: vi.fn()
        },
        shell: {
          openExternal,
          openPath: vi.fn()
        }
      }
    })
  })

  it('keeps installed skills and searches marketplace from the new tab', async () => {
    renderView()

    expect(await screen.findByText('memory')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('tab', { name: 'Marketplace' }))
    fireEvent.change(screen.getByPlaceholderText('Search SkillHub and ClawHub'), {
      target: { value: 'git' }
    })

    await waitFor(() => {
      expect(skillMarketSearch).toHaveBeenCalledWith({
        query: 'git',
        provider: 'all',
        limit: 24
      })
    })
    expect(await screen.findByText('Git Helper')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('tab', { name: 'Installed' }))
    expect(screen.getByText('memory')).toBeInTheDocument()
  })

  it('shows the full description in the detail body when preview markdown is unavailable', async () => {
    skillMarketSearch.mockResolvedValue({
      skills: [
        {
          provider: 'skillhub',
          slug: 'p4u',
          name: 'p4u',
          description: 'Full marketplace description for p4u.',
          installed: false
        }
      ]
    })
    skillMarketDetail.mockResolvedValue({
      provider: 'skillhub',
      slug: 'p4u',
      name: 'p4u',
      description: 'Full marketplace description for p4u.',
      installed: false
    })

    renderView()
    fireEvent.click(await screen.findByRole('tab', { name: 'Marketplace' }))
    fireEvent.change(screen.getByPlaceholderText('Search SkillHub and ClawHub'), {
      target: { value: 'p4u' }
    })
    fireEvent.click(await screen.findByRole('button', { name: /p4u/i }))

    const matches = await screen.findAllByText('Full marketplace description for p4u.')
    expect(matches.length).toBeGreaterThan(1)
  })

  it('opens links from marketplace markdown preview in the external browser', async () => {
    skillMarketSearch.mockResolvedValue({
      skills: [
        {
          provider: 'skillhub',
          slug: 'p4u',
          name: 'p4u',
          description: 'p4u description',
          installed: false
        }
      ]
    })
    skillMarketDetail.mockResolvedValue({
      provider: 'skillhub',
      slug: 'p4u',
      name: 'p4u',
      description: 'p4u description',
      readme: '# p4u\n\n[Repo](https://github.com/m9rco/p4u-skill)',
      installed: false
    })

    renderView()
    fireEvent.click(await screen.findByRole('tab', { name: 'Marketplace' }))
    fireEvent.change(screen.getByPlaceholderText('Search SkillHub and ClawHub'), {
      target: { value: 'p4u' }
    })
    fireEvent.click(await screen.findByRole('button', { name: /p4u/i }))
    fireEvent.click(await screen.findByRole('link', { name: /repo/i }))

    expect(openExternal).toHaveBeenCalledWith('https://github.com/m9rco/p4u-skill')
  })
})
