import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConversationWelcome } from '../components/conversation/ConversationWelcome'
import { COMMAND_REF_CLASS, FILE_REF_CLASS, SKILL_REF_CLASS } from '../components/conversation/richInputConstants'
import { useConnectionStore } from '../stores/connectionStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

const fileReadFile = vi.fn()
const appServerSendRequest = vi.fn()
const saveImageToTemp = vi.fn()
const pickFiles = vi.fn()
const settingsGet = vi.fn()

function linearizeSelection(root: Node): string {
  let out = ''
  const walk = (node: Node): void => {
    if (node.nodeType === Node.TEXT_NODE) {
      out += node.textContent ?? ''
      return
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return
    const el = node as HTMLElement
    if (
      el.classList.contains(FILE_REF_CLASS) ||
      el.classList.contains(COMMAND_REF_CLASS) ||
      el.classList.contains(SKILL_REF_CLASS) ||
      el.tagName === 'BR'
    ) {
      out += ' '
      return
    }
    for (const child of Array.from(node.childNodes)) {
      walk(child)
    }
  }
  walk(root)
  return out
}

function getTextboxSelection(textbox: HTMLElement): { start: number; end: number } | null {
  const selection = window.getSelection()
  if (!selection || selection.rangeCount === 0) return null
  const range = selection.getRangeAt(0)
  if (!textbox.contains(range.startContainer) || !textbox.contains(range.endContainer)) return null

  const startRange = document.createRange()
  startRange.selectNodeContents(textbox)
  startRange.setEnd(range.startContainer, range.startOffset)
  const endRange = document.createRange()
  endRange.selectNodeContents(textbox)
  endRange.setEnd(range.endContainer, range.endOffset)

  const startContainer = document.createElement('div')
  startContainer.appendChild(startRange.cloneContents())
  const endContainer = document.createElement('div')
  endContainer.appendChild(endRange.cloneContents())

  return {
    start: linearizeSelection(startContainer).length,
    end: linearizeSelection(endContainer).length
  }
}

function setTextboxCaret(textbox: HTMLElement, offset: number): void {
  const textNode = textbox.firstChild
  if (!textNode) throw new Error('textbox has no text node')
  const range = document.createRange()
  range.setStart(textNode, offset)
  range.setEnd(textNode, offset)
  const selection = window.getSelection()
  selection?.removeAllRanges()
  selection?.addRange(range)
}

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
      autoShowPlanForItem: null,
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
        workspaceConfigManagement: true,
        extensions: {
          welcomeSuggestions: true
        }
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
      if (method === 'welcome/suggestions') {
        return {
          source: 'none',
          items: [],
          fingerprint: 'none'
        }
      }
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
          saveImageToTemp,
          pickFiles
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
    expect(sendButton.getAttribute('style')).toContain('color-mix(in srgb, var(--bg-primary) 92%, #ffffff 8%)')
    expect(sendButton.getAttribute('style')).toContain('var(--text-dimmed)')
    expect(screen.queryByText('Attach file')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add attachment' })).toBeInTheDocument()
  })

  it('opens the compact attachment menu and still routes file references through pickFiles', async () => {
    pickFiles.mockResolvedValue([{ path: 'C:\\temp\\brief.md', fileName: 'brief.md' }])

    renderWelcome()

    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))
    expect(screen.getByRole('menuitem', { name: 'Attach image' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('menuitem', { name: 'Reference file' }))

    await waitFor(() => {
      expect(pickFiles).toHaveBeenCalled()
      expect(screen.getByText('brief.md')).toBeInTheDocument()
    })
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
      const threadStartCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'thread/start')
      expect(threadStartCall?.[0]).toBe('thread/start')
      const payload = threadStartCall?.[1] as {
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
      selectionStart: 6,
      selectionEnd: 6,
      images: [],
      mode: 'plan',
      model: 'gpt-5.4-mini'
    })

    const mounted = renderWelcome()

    const textbox = await screen.findByRole('textbox')
    await waitFor(() => {
      expect(textbox.textContent).toContain('resume draft message')
    })
    await waitFor(() => {
      expect(getTextboxSelection(textbox)).toEqual({ start: 6, end: 6 })
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

  it('preserves caret position across thread switch and welcome remount', async () => {
    const firstMount = renderWelcome()

    const textbox = await screen.findByRole('textbox')
    fireEvent.input(textbox, { target: { textContent: 'restore this caret' } })
    setTextboxCaret(textbox, 7)
    fireEvent.mouseUp(textbox)

    await waitFor(() => {
      expect(getTextboxSelection(textbox)).toEqual({ start: 7, end: 7 })
    })

    firstMount.unmount()

    expect(useUIStore.getState().welcomeDraft).toMatchObject({
      text: 'restore this caret',
      selectionStart: 7,
      selectionEnd: 7
    })

    const secondMount = renderWelcome()
    const restoredTextbox = await screen.findByRole('textbox')

    await waitFor(() => {
      expect(restoredTextbox.textContent).toContain('restore this caret')
      expect(getTextboxSelection(restoredTextbox)).toEqual({ start: 7, end: 7 })
    })

    secondMount.unmount()
  })

  it('hydrates structured welcome drafts back into inline tags', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'Check @src/foo.ts then /code-review and [[Use Skill: browser]]',
      segments: [
        { type: 'text', value: 'Check ' },
        { type: 'file', relativePath: 'src/foo.ts' },
        { type: 'text', value: ' then ' },
        { type: 'command', command: '/code-review' },
        { type: 'text', value: ' and ' },
        { type: 'skill', skillName: 'browser' }
      ],
      images: [],
      mode: 'agent',
      model: 'Default'
    })

    const mounted = renderWelcome()

    const textbox = await screen.findByRole('textbox')
    await waitFor(() => {
      expect(textbox.querySelector(`.${FILE_REF_CLASS}`)).not.toBeNull()
      expect(textbox.querySelector(`.${COMMAND_REF_CLASS}`)).not.toBeNull()
      expect(textbox.querySelector(`.${SKILL_REF_CLASS}`)).not.toBeNull()
    })
    expect(textbox.textContent).not.toContain('[[Use Skill: browser]]')

    mounted.unmount()

    expect(useUIStore.getState().welcomeDraft).toMatchObject({
      text: 'Check @src/foo.ts then /code-review and [[Use Skill: browser]]'
    })
    expect(useUIStore.getState().welcomeDraft?.segments).toEqual([
      { type: 'text', value: 'Check ' },
      { type: 'file', relativePath: 'src/foo.ts' },
      { type: 'text', value: ' then ' },
      { type: 'command', command: '/code-review' },
      { type: 'text', value: ' and ' },
      { type: 'skill', skillName: 'browser' }
    ])
  })

  it('restores legacy text drafts into tags and keeps serialized text when sending', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'Check @src/foo.ts /code-review [[Use Skill: browser]]',
      images: [],
      mode: 'agent',
      model: 'Default'
    })

    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    await waitFor(() => {
      expect(textbox.querySelector(`.${FILE_REF_CLASS}`)).not.toBeNull()
      expect(textbox.querySelector(`.${COMMAND_REF_CLASS}`)).not.toBeNull()
      expect(textbox.querySelector(`.${SKILL_REF_CLASS}`)).not.toBeNull()
    })

    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'Check @src/foo.ts /code-review [[Use Skill: browser]]'
      })
    })
  })

  it('hydrates file attachments from welcomeDraft and keeps them when creating the pending welcome turn', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'Review this file',
      images: [],
      files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }],
      mode: 'agent',
      model: 'Default'
    })

    renderWelcome()

    expect(await screen.findByText('notes.txt')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'Review this file',
        files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }]
      })
    })
  })

  it('replaces static welcome suggestions when dynamic suggestions load successfully', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'welcome/suggestions') {
        return {
          source: 'dynamic',
          fingerprint: 'dynamic-1',
          items: [
            {
              title: 'Review desktop welcome flow',
              prompt: 'Review the Desktop welcome flow and identify where we should inject dynamic quick suggestions.'
            },
            {
              title: 'Map thread history inputs',
              prompt: 'Trace how current workspace thread history is loaded so we can feed it into welcome suggestion generation.'
            }
          ]
        }
      }
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

    renderWelcome()

    expect(await screen.findByText('Review desktop welcome flow')).toBeInTheDocument()
    expect(screen.queryByText('Explore this workspace')).not.toBeInTheDocument()
  })

  it('keeps static welcome suggestions when the server returns none', async () => {
    renderWelcome()

    expect(await screen.findByRole('button', { name: 'Explore this workspace' })).toBeInTheDocument()
    await waitFor(() => {
      const methods = appServerSendRequest.mock.calls.map((call) => call[0])
      expect(methods).toContain('welcome/suggestions')
    })
  })

  it('does not request welcome suggestions when the workspace config disables them', async () => {
    fileReadFile.mockResolvedValue(
      JSON.stringify({
        WelcomeSuggestions: {
          Enabled: false
        }
      })
    )

    renderWelcome()

    await screen.findByRole('button', { name: 'Explore this workspace' })
    await waitFor(() => {
      const methods = appServerSendRequest.mock.calls.map((call) => call[0])
      expect(methods).not.toContain('welcome/suggestions')
    })
  })

  it('clicking a dynamic suggestion prefills the welcome composer', async () => {
    const dynamicPrompt = 'Audit how workspace memory is currently loaded and suggest how to reuse it for welcome suggestions.'

    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'welcome/suggestions') {
        return {
          source: 'dynamic',
          fingerprint: 'dynamic-2',
          items: [
            {
              title: 'Audit workspace memory usage',
              prompt: dynamicPrompt
            }
          ]
        }
      }
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

    renderWelcome()

    const dynamicButton = await screen.findByRole('button', { name: 'Audit workspace memory usage' })
    fireEvent.click(dynamicButton)

    const textbox = await screen.findByRole('textbox')
    expect(textbox.textContent).toContain('Audit how workspace memory is currently loaded')
    await waitFor(() => {
      expect(getTextboxSelection(textbox)).toEqual({
        start: dynamicPrompt.length,
        end: dynamicPrompt.length
      })
    })
  })
})
