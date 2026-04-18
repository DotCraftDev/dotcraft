import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { InputComposer } from '../components/conversation/InputComposer'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

function setCaretToEnd(element: HTMLElement): void {
  const selection = window.getSelection()
  if (!selection) return
  const range = document.createRange()
  range.selectNodeContents(element)
  range.collapse(false)
  selection.removeAllRanges()
  selection.addRange(range)
}

describe('InputComposer custom command expansion', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') {
        return {
          commands: [
            {
              name: '/code-review',
              aliases: ['/cr'],
              description: 'Review files',
              category: 'custom',
              requiresAdmin: false
            }
          ]
        }
      }
      if (method === 'skills/list') {
        return {
          skills: [
            {
              name: 'browser',
              description: 'Browse web pages',
              source: 'builtin',
              available: true,
              enabled: true,
              path: '/skills/browser/SKILL.md'
            }
          ]
        }
      }
      if (method === 'command/execute') {
        return {
          handled: true,
          expandedPrompt: 'Expanded review prompt'
        }
      }
      if (method === 'turn/start') {
        return { turn: { id: 'turn-1' } }
      }
      return {}
    })

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
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        commandManagement: true,
        skillsManagement: true
      }
    })
    useThreadStore.setState({
      threadList: [
        {
          id: 'thread-1',
          displayName: null,
          status: 'active',
          originChannel: 'appserver',
          createdAt: new Date().toISOString(),
          lastActiveAt: new Date().toISOString()
        }
      ]
    })
  })

  it('executes custom command and sends expanded prompt', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('command/list', { language: 'en' })
    })

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/code-review'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('command/execute', {
        threadId: 'thread-1',
        command: '/code-review',
        arguments: []
      })
    })
    await waitFor(() => {
      const turnStartCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/start')
      expect(turnStartCall).toBeDefined()
      expect(turnStartCall?.[1]).toEqual(
        expect.objectContaining({
          threadId: 'thread-1',
          input: [{ type: 'text', text: 'Expanded review prompt' }]
        })
      )
    })
  })

  it('prevents duplicate send while command resolution is in flight', async () => {
    let resolveCommandExecute: ((value: unknown) => void) | null = null
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'command/list') {
        return Promise.resolve({
          commands: [
            {
              name: '/code-review',
              aliases: ['/cr'],
              description: 'Review files',
              category: 'custom',
              requiresAdmin: false
            }
          ]
        })
      }
      if (method === 'command/execute') {
        return new Promise((resolve) => {
          resolveCommandExecute = resolve
        })
      }
      if (method === 'turn/start') {
        return Promise.resolve({ turn: { id: 'turn-1' } })
      }
      return Promise.resolve({})
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('command/list', { language: 'en' })
    })

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/code-review'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      const commandCalls = appServerSendRequest.mock.calls.filter((call) => call[0] === 'command/execute')
      expect(commandCalls).toHaveLength(1)
    })

    resolveCommandExecute?.({
      handled: true,
      expandedPrompt: 'Expanded review prompt'
    })

    await waitFor(() => {
      const turnStartCalls = appServerSendRequest.mock.calls.filter((call) => call[0] === 'turn/start')
      expect(turnStartCalls).toHaveLength(1)
    })
  })

  it('inserts skill via slash and serializes marker text', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/list', { includeUnavailable: true })
    })

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/browser'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      const turnStartCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/start')
      expect(turnStartCall).toBeDefined()
      expect(turnStartCall?.[1]).toEqual(
        expect.objectContaining({
          threadId: 'thread-1',
          input: [{ type: 'text', text: '[[Use Skill: browser]]' }]
        })
      )
    })
  })
})
