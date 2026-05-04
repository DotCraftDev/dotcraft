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
              name: 'memory',
              description: 'Recall project context',
              source: 'builtin',
              available: true,
              enabled: true,
              path: '/skills/memory/SKILL.md'
            }
          ]
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

  it('sends custom commands as native commandRef parts', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('command/list', { language: 'en' })
    })

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/code-review'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      const turnStartCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/start')
      expect(turnStartCall).toBeDefined()
      expect(turnStartCall?.[1]).toEqual(
        expect.objectContaining({
          threadId: 'thread-1',
          input: [{ type: 'text', text: '/code-review' }]
        })
      )
    })
  })

  it('prevents duplicate send while turn/start is in flight', async () => {
    let resolveTurnStart: ((value: unknown) => void) | null = null
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
      if (method === 'turn/start') {
        return new Promise((resolve) => {
          resolveTurnStart = resolve
        })
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
      const turnStartCalls = appServerSendRequest.mock.calls.filter((call) => call[0] === 'turn/start')
      expect(turnStartCalls).toHaveLength(1)
    })

    resolveTurnStart?.({ turn: { id: 'turn-1' } })

    await waitFor(() => {
      const turnStartCalls = appServerSendRequest.mock.calls.filter((call) => call[0] === 'turn/start')
      expect(turnStartCalls).toHaveLength(1)
    })
  })

  it('inserts skill via slash and serializes skillRef input', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/list', { includeUnavailable: true })
    })

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/memory'
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
          input: [
            { type: 'skillRef', name: 'memory' },
            { type: 'text', text: '\u00a0' }
          ]
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
          input: [
            { type: 'text', text: '[[Attached File: C:\\temp\\notes.txt]]\n\n' },
            { type: 'text', text: 'Review this file' }
          ]
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

    await waitFor(() => {
      const enqueueCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/enqueue')
      expect(enqueueCall).toBeDefined()
      expect(enqueueCall?.[1]).toEqual({
        threadId: 'thread-1',
        input: [
          { type: 'text', text: '[[Attached File: C:\\temp\\notes.txt]]\n\n' },
          { type: 'text', text: '/code-review' }
        ],
        sender: undefined
      })
    })
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

    await waitFor(() => {
      const enqueueCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/enqueue')
      expect(enqueueCall).toBeDefined()
      expect(enqueueCall?.[1]).toEqual({
        threadId: 'thread-1',
        input: [
          { type: 'text', text: '[[Attached File: C:\\temp\\notes.txt]]' }
        ],
        sender: undefined
      })
    })
    expect(screen.queryByText(/\[\[Attached File:/)).not.toBeInTheDocument()
  })

  it('queues dropped images alongside text and file references while running', async () => {
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
      const enqueueCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/enqueue')
      expect(enqueueCall).toBeDefined()
      expect(enqueueCall?.[1]).toEqual({
        threadId: 'thread-1',
        input: [
          { type: 'text', text: '[[Attached File: C:\\temp\\notes.txt]]\n\n' },
          { type: 'text', text: '/code-review' },
          {
            type: 'localImage',
            path: 'C:\\temp\\image.png',
            mimeType: 'image/png',
            fileName: 'diagram.png'
          }
        ],
        sender: undefined
      })
    })
  })
})
