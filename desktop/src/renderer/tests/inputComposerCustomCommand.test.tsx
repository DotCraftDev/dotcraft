import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { InputComposer } from '../components/conversation/InputComposer'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useToastStore } from '../stores/toastStore'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()
const pickFiles = vi.fn()
const saveImageToTemp = vi.fn()

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
    pickFiles.mockResolvedValue([])
    saveImageToTemp.mockResolvedValue({ path: 'C:\\temp\\image.png' })
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
        workspace: { saveImageToTemp, pickFiles }
      }
    })

    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    useToastStore.setState({ toasts: [] })
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
      autoShowPlanForItem: null,
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

  it('serializes picked file attachments into attached-file markers on turn/start', async () => {
    pickFiles.mockResolvedValue([
      { path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }
    ])

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'Review this file'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Reference file' }))

    expect(await screen.findByText('notes.txt')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      const turnStartCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/start')
      expect(turnStartCall).toBeDefined()
      expect(turnStartCall?.[1]).toEqual(
        expect.objectContaining({
          threadId: 'thread-1',
          input: [{ type: 'text', text: '[[Attached File: C:\\temp\\notes.txt]]\n\nReview this file' }]
        })
      )
    })
  })

  it('opens a compact attachment menu with image and file actions', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/list', { includeUnavailable: true })
    })

    expect(screen.queryByText('Attach file')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))

    expect(screen.getByRole('menuitem', { name: 'Attach image' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Reference file' })).toBeInTheDocument()
  })

  it('accepts mixed dropped images and files', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    const surface = screen
      .getByRole('textbox')
      .closest('div[style*="border-radius: 20px"]') as HTMLElement

    const image = new File(['image-bytes'], 'diagram.png', { type: 'image/png' })
    Object.defineProperty(image, 'path', { configurable: true, value: 'C:\\temp\\diagram.png' })
    const note = new File(['notes'], 'notes.txt', { type: 'text/plain' })
    Object.defineProperty(note, 'path', { configurable: true, value: 'C:\\temp\\notes.txt' })

    fireEvent.drop(surface, {
      dataTransfer: {
        files: [image, note],
        items: [
          {
            kind: 'file',
            getAsFile: () => image,
            webkitGetAsEntry: () => ({ isDirectory: false })
          },
          {
            kind: 'file',
            getAsFile: () => note,
            webkitGetAsEntry: () => ({ isDirectory: false })
          }
        ]
      }
    })

    await waitFor(() => {
      expect(saveImageToTemp).toHaveBeenCalled()
      expect(screen.getByText('notes.txt')).toBeInTheDocument()
    })
  })

  it('queues structured pending messages while running so slash commands keep their leading slash', async () => {
    pickFiles.mockResolvedValue([
      { path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }
    ])
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-running'
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/code-review'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Reference file' }))

    expect(await screen.findByText('notes.txt')).toBeInTheDocument()

    fireEvent.keyDown(textbox, { key: 'Enter' })

    expect(useConversationStore.getState().pendingMessage).toEqual({
      text: '/code-review',
      files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }]
    })
    expect(screen.getByText(/Queued:/)).toHaveTextContent('/code-review')
  })

  it('shows a file-reference queue label instead of raw markers when queued text is empty', async () => {
    pickFiles.mockResolvedValue([
      { path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }
    ])
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-running'
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Reference file' }))

    expect(await screen.findByText('notes.txt')).toBeInTheDocument()

    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Enter' })

    expect(useConversationStore.getState().pendingMessage).toEqual({
      text: '',
      files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }]
    })
    expect(screen.getByText(/Queued:/)).toHaveTextContent(/Queued follow-up with 1 file reference/)
    expect(screen.queryByText(/\[\[Attached File:/)).not.toBeInTheDocument()
  })

  it('drops queued images but preserves queued text and files while warning the user', async () => {
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-running'
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    const surface = screen
      .getByRole('textbox')
      .closest('div[style*="border-radius: 20px"]') as HTMLElement

    const image = new File(['image-bytes'], 'diagram.png', { type: 'image/png' })
    Object.defineProperty(image, 'path', { configurable: true, value: 'C:\\temp\\diagram.png' })
    const note = new File(['notes'], 'notes.txt', { type: 'text/plain' })
    Object.defineProperty(note, 'path', { configurable: true, value: 'C:\\temp\\notes.txt' })

    fireEvent.drop(surface, {
      dataTransfer: {
        files: [image, note],
        items: [
          {
            kind: 'file',
            getAsFile: () => image,
            webkitGetAsEntry: () => ({ isDirectory: false })
          },
          {
            kind: 'file',
            getAsFile: () => note,
            webkitGetAsEntry: () => ({ isDirectory: false })
          }
        ]
      }
    })

    await waitFor(() => {
      expect(saveImageToTemp).toHaveBeenCalled()
      expect(screen.getByText('notes.txt')).toBeInTheDocument()
    })

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/code-review'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(useConversationStore.getState().pendingMessage).toEqual({
        text: '/code-review',
        files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }]
      })
    })
    expect(
      useToastStore.getState().toasts.some((toast) =>
        toast.message.includes('Image attachments cannot be queued')
      )
    ).toBe(true)
  })
})
