import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConversationWelcome } from '../components/conversation/ConversationWelcome'
import { useConnectionStore } from '../stores/connectionStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

const fileReadFile = vi.fn()
const appServerSendRequest = vi.fn()
const saveImageToTemp = vi.fn()
const settingsGet = vi.fn()

function renderWelcome() {
  return render(
    <LocaleProvider>
      <ConversationWelcome workspacePath="F:\\dotcraft" />
    </LocaleProvider>
  )
}

describe('ConversationWelcome composer', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    useModelCatalogStore.getState().reset()
    useUIStore.setState({
      activeMainView: 'conversation',
      automationsTab: 'tasks',
      sidebarCollapsed: false,
      sidebarWidth: 240,
      detailPanelVisible: true,
      detailPanelWidth: 400,
      activeDetailTab: 'changes',
      selectedChangedFile: null,
      autoShowTriggeredForTurn: null,
      composerPrefill: null,
      pendingWelcomeTurn: null,
      welcomeDraft: null,
      _pendingWelcomeTimer: null
    })

    useConnectionStore.setState({
      status: 'connected',
      serverInfo: null,
      dashboardUrl: null,
      errorMessage: null,
      errorType: null,
      binarySource: null,
      capabilities: {
        commandManagement: true,
        modelCatalogManagement: true,
        workspaceConfigManagement: true
      }
    })
    useModelCatalogStore.setState({
      status: 'ready',
      modelOptions: ['gpt-5.4', 'gpt-5.4-mini'],
      modelListUnsupportedEndpoint: false
    })

    fileReadFile.mockResolvedValue('{}')
    settingsGet.mockResolvedValue({ locale: 'en' })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/start') {
        return {
          thread: {
            id: 'thread-welcome',
            displayName: 'Welcome thread',
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-04-16T08:00:00.000Z',
            lastActiveAt: '2026-04-16T08:00:00.000Z'
          }
        }
      }
      return {}
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: settingsGet
        },
        appServer: {
          sendRequest: appServerSendRequest
        },
        file: {
          readFile: fileReadFile
        },
        workspace: {
          saveImageToTemp
        }
      }
    })
  })

  it('renders the same single mode toggle and themed model picker as the main composer', async () => {
    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    const composerSurface = textbox.closest('div[style*="border-radius: 20px"]')

    expect(composerSurface).not.toBeNull()
    expect(textbox.getAttribute('style')).toContain('border-radius: 0px')
    expect(textbox.getAttribute('style')).toContain('background-color: transparent')
    const modeToggle = screen.getByRole('button', { name: 'Agent' })
    expect(modeToggle.getAttribute('style')).toContain('background: transparent')
    expect(modeToggle.getAttribute('style')).not.toContain('var(--border-default)')
    expect(screen.queryByRole('button', { name: 'Plan' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    const listbox = screen.getByRole('listbox', { name: 'Select model' })
    expect(listbox).toBeInTheDocument()
    expect(listbox.getAttribute('style')).toContain('var(--bg-secondary)')
    const sendButton = screen.getByRole('button', { name: 'Send message' })
    expect(sendButton.querySelector('svg')?.getAttribute('width')).toBe('20')
  })

  it('creates a thread and stores the pending welcome turn on first send', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'stale draft',
      images: [],
      mode: 'agent',
      model: 'Default'
    })

    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    fireEvent.input(textbox, { target: { textContent: 'Help me understand this workspace' } })
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls[1]?.[0]).toBe('thread/start')
      const payload = appServerSendRequest.mock.calls[1]?.[1] as {
        historyMode?: string
        identity?: { workspacePath?: string }
      }
      expect(payload.historyMode).toBe('server')
      expect(payload.identity?.workspacePath).toContain('dotcraft')
    })

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'Help me understand this workspace',
        mode: 'agent',
        model: ''
      })
      expect(useThreadStore.getState().activeThreadId).toBe('thread-welcome')
      expect(useUIStore.getState().welcomeDraft).toBeNull()
    })
  })

  it('hydrates from welcomeDraft and persists latest draft on unmount', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'resume draft message',
      images: [],
      mode: 'plan',
      model: 'gpt-5.4-mini'
    })

    const mounted = renderWelcome()

    const textbox = await screen.findByRole('textbox')
    await waitFor(() => {
      expect(textbox.textContent).toContain('resume draft message')
    })
    expect(screen.getByRole('button', { name: 'Plan' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Explore this workspace' }))
    mounted.unmount()

    expect(useUIStore.getState().welcomeDraft).toMatchObject({
      mode: 'plan',
      model: 'gpt-5.4-mini'
    })
    expect(useUIStore.getState().welcomeDraft?.text).toContain('Give me a quick overview of this project')
  })
})
