import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { InputComposer } from '../components/conversation/InputComposer'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()

function renderComposer(): void {
  render(
    <LocaleProvider>
      <InputComposer
        threadId="thread-1"
        workspacePath="F:\\dotcraft"
        modelName="gpt-5.4"
        modelOptions={['gpt-5.4', 'gpt-5.4-mini']}
      />
    </LocaleProvider>
  )
}

describe('InputComposer layout', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    appServerSendRequest.mockResolvedValue({})

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        workspace: { saveImageToTemp: vi.fn() }
      }
    })

    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
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
      _pendingWelcomeTimer: null
    })
    useThreadStore.setState({
      threadList: [
        {
          id: 'thread-1',
          displayName: 'Layout test',
          status: 'active',
          originChannel: 'dotcraft-desktop',
          createdAt: new Date().toISOString(),
          lastActiveAt: new Date().toISOString()
        }
      ]
    })
  })

  it('renders single mode toggle and themed model picker inside the composer surface', () => {
    renderComposer()

    const textbox = screen.getByRole('textbox')
    const composerSurface = textbox.closest('div[style*="border-radius: 20px"]')

    expect(composerSurface).not.toBeNull()
    expect(textbox.getAttribute('style')).toContain('border-radius: 0px')
    expect(textbox.getAttribute('style')).toContain('background-color: transparent')
    const modeToggle = screen.getByRole('button', { name: 'Agent' })
    expect(modeToggle.getAttribute('style')).toContain('background: transparent')
    expect(modeToggle.getAttribute('style')).not.toContain('var(--border-default)')
    fireEvent.click(modeToggle)
    expect(screen.getByRole('button', { name: 'Plan' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Plan' }))
    expect(screen.getByRole('button', { name: 'Agent' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Plan' })).not.toBeInTheDocument()

    const modelButton = screen.getByRole('button', { name: 'Select model' })
    fireEvent.focus(modelButton)
    const tooltip = screen.getByRole('tooltip')
    expect(within(tooltip).getByText('Select model')).toBeInTheDocument()
    expect(within(tooltip).getByText('Ctrl')).toBeInTheDocument()
    expect(within(tooltip).getByText('Shift')).toBeInTheDocument()
    expect(within(tooltip).getByText('M')).toBeInTheDocument()

    fireEvent.keyDown(window, { key: 'M', ctrlKey: true, shiftKey: true })
    const listbox = screen.getByRole('listbox', { name: 'Select model' })

    expect(listbox).toBeInTheDocument()
    expect(listbox.getAttribute('style')).toContain('var(--bg-secondary)')
    expect(screen.getByRole('option', { name: 'gpt-5.4-mini' })).toBeInTheDocument()
  })

  it('keeps send button available alongside the inline toolbar', () => {
    renderComposer()

    const sendButton = screen.getByRole('button', { name: 'Send message' })
    const svg = sendButton.querySelector('svg')

    expect(sendButton).toBeInTheDocument()
    expect(svg?.getAttribute('width')).toBe('20')
    expect(sendButton.getAttribute('style')).toContain('color-mix(in srgb, var(--bg-primary) 92%, #ffffff 8%)')
    expect(sendButton.getAttribute('style')).toContain('var(--text-dimmed)')
  })

  it('keeps the context usage ring aligned to the model picker height with a smaller donut', () => {
    useConversationStore.getState().setContextUsage({
      tokens: 2500,
      contextWindow: 10000,
      autoCompactThreshold: 8000,
      warningThreshold: 7000,
      errorThreshold: 9000,
      percentLeft: 0.75
    })

    renderComposer()

    const ring = screen.getByRole('img', { name: 'Context usage: 25% used' })
    const ringSvg = ring.querySelector('svg')
    const modelButton = screen.getByRole('button', { name: 'Select model' })

    expect(ring.getAttribute('style')).toContain('width: 22px')
    expect(ring.getAttribute('style')).toContain('height: 22px')
    expect(ringSvg?.getAttribute('width')).toBe('14')
    expect(ringSvg?.getAttribute('height')).toBe('14')
    expect(modelButton.getAttribute('style')).toContain('height: 22px')
  })

  it('matches the running stop button to the enabled send button style', () => {
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-123'
    })

    renderComposer()

    const stopButton = screen.getByRole('button', { name: 'Stop turn' })

    expect(stopButton).toBeInTheDocument()
    expect(stopButton.getAttribute('style')).not.toContain('var(--error)')
    expect(stopButton.getAttribute('style')).not.toContain('#fff')
    expect(stopButton.getAttribute('style')).not.toContain('#ffffff')
    expect(stopButton.getAttribute('style')).toContain('rgb(245, 246, 247)')
    expect(stopButton.getAttribute('style')).toContain('rgb(31, 35, 40)')
  })
})
