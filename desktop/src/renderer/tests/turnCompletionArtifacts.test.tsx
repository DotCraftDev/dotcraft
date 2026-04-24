import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { TurnArtifacts } from '../components/conversation/TurnArtifacts'
import { TurnCompletionSummary } from '../components/conversation/TurnCompletionSummary'
import { useConversationStore } from '../stores/conversationStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import type { FileDiff } from '../types/toolCall'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const listEditors = vi.fn()
const launchEditor = vi.fn()
const toViewerUrl = vi.fn()
const browserCreate = vi.fn()
const writeFile = vi.fn()
const deleteFile = vi.fn()

function makeDiff(filePath: string, overrides: Partial<FileDiff> = {}): FileDiff {
  return {
    filePath,
    turnId: 'turn-1',
    turnIds: ['turn-1'],
    additions: 1,
    deletions: 1,
    status: 'written',
    isNewFile: false,
    originalContent: 'old\n',
    currentContent: 'new\n',
    diffHunks: [
      {
        oldStart: 1,
        oldLines: 1,
        newStart: 1,
        newLines: 1,
        lines: [
          { type: 'remove', content: 'old' },
          { type: 'add', content: 'new' }
        ]
      }
    ],
    ...overrides
  }
}

function renderWithLocale(ui: JSX.Element): void {
  render(<LocaleProvider>{ui}</LocaleProvider>)
}

function resetStores(): void {
  useConversationStore.getState().reset()
  useConversationStore.setState({
    workspacePath: 'F:/workspace',
    changedFiles: new Map()
  })
  useViewerTabStore.setState({
    byThread: new Map(),
    currentThreadId: 'thread-1',
    currentWorkspacePath: 'F:/workspace'
  })
}

describe('turn completion artifacts', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    resetStores()
    settingsGet.mockResolvedValue({ locale: 'en', lastOpenEditorId: 'explorer' })
    settingsSet.mockResolvedValue(undefined)
    listEditors.mockResolvedValue([{ id: 'explorer', labelKey: 'editors.explorer', iconKey: 'explorer' }])
    launchEditor.mockResolvedValue(undefined)
    toViewerUrl.mockResolvedValue({ url: 'dotcraft-viewer://workspace/F%3A/workspace/site/index.html' })
    browserCreate.mockResolvedValue({
      tabId: 'browser-tab',
      currentUrl: 'dotcraft-viewer://workspace/F%3A/workspace/site/index.html',
      title: 'index.html',
      canGoBack: false,
      canGoForward: false,
      loading: false
    })
    writeFile.mockResolvedValue(undefined)
    deleteFile.mockResolvedValue(undefined)
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet, set: settingsSet },
        shell: { listEditors, launchEditor },
        file: { writeFile, deleteFile },
        workspace: {
          viewer: {
            toViewerUrl,
            browser: { create: browserCreate }
          }
        }
      }
    })
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = undefined
  })

  it('renders Markdown and HTML artifact cards', async () => {
    useConversationStore.setState({
      changedFiles: new Map([
        ['README.md', makeDiff('README.md')],
        ['site/index.html', makeDiff('site/index.html')]
      ])
    })

    renderWithLocale(<TurnArtifacts turnId="turn-1" />)

    expect(screen.getByText('README.md')).toBeInTheDocument()
    expect(screen.getByText('Document · MD')).toBeInTheDocument()
    expect(screen.getByText('index.html')).toBeInTheDocument()
    expect(screen.getByText('Web page · HTML')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Choose how to open file' })).toBeEnabled())
  })

  it('opens HTML artifacts in the internal browser', async () => {
    useConversationStore.setState({
      changedFiles: new Map([['site/index.html', makeDiff('site/index.html')]])
    })

    renderWithLocale(<TurnArtifacts turnId="turn-1" />)
    fireEvent.click(screen.getByRole('button', { name: 'Preview index.html in DotCraft browser' }))

    await waitFor(() => {
      expect(toViewerUrl).toHaveBeenCalledWith({ absolutePath: 'F:/workspace/site/index.html' })
      expect(browserCreate).toHaveBeenCalledWith(expect.objectContaining({
        workspacePath: 'F:/workspace',
        initialUrl: 'dotcraft-viewer://workspace/F%3A/workspace/site/index.html'
      }))
    })
  })

  it('expands turn file diffs inline and can undo written files', async () => {
    useConversationStore.setState({
      changedFiles: new Map([['src/App.tsx', makeDiff('src/App.tsx')]])
    })
    ;(window as Window & { __confirmDialog?: (opts: unknown) => Promise<boolean> }).__confirmDialog = vi.fn()
      .mockResolvedValue(true)

    renderWithLocale(<TurnCompletionSummary turnId="turn-1" />)

    fireEvent.click(screen.getAllByRole('button', { name: /src\/App\.tsx/ })[0]!)
    expect(screen.getByText('@@ -1,1 +1,1 @@')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Undo' }))
    await waitFor(() => {
      expect(writeFile).toHaveBeenCalledWith('F:/workspace/src/App.tsx', 'old\n')
    })
    expect(useConversationStore.getState().changedFiles.get('src/App.tsx')?.status).toBe('reverted')
  })
})
