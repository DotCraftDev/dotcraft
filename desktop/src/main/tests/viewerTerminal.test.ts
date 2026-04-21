import { beforeEach, describe, expect, it, vi } from 'vitest'

const { spawnMock } = vi.hoisted(() => ({
  spawnMock: vi.fn()
}))

vi.mock('@lydell/node-pty', () => ({
  spawn: spawnMock
}))

import { ViewerTerminalManager } from '../viewerTerminal'

interface MockPty {
  pid: number
  onData: (cb: (data: string) => void) => void
  onExit: (cb: (event: { exitCode: number; signal: number | null }) => void) => void
  write: (data: string) => void
  resize: (cols: number, rows: number) => void
  kill: () => void
}

function createMockWindow() {
  const send = vi.fn()
  return {
    id: 99,
    isDestroyed: vi.fn(() => false),
    webContents: {
      isDestroyed: vi.fn(() => false),
      send
    }
  } as unknown as Electron.BrowserWindow
}

describe('ViewerTerminalManager', () => {
  let onDataHandler: ((data: string) => void) | null
  let onExitHandler: ((event: { exitCode: number; signal: number | null }) => void) | null
  let mockPty: MockPty

  beforeEach(() => {
    onDataHandler = null
    onExitHandler = null
    mockPty = {
      pid: 4321,
      onData: (cb) => {
        onDataHandler = cb
      },
      onExit: (cb) => {
        onExitHandler = cb
      },
      write: vi.fn(),
      resize: vi.fn(),
      kill: vi.fn()
    }
    spawnMock.mockReset()
    spawnMock.mockReturnValue(mockPty)
  })

  it('creates tab, forwards output and returns attach snapshot', () => {
    const manager = new ViewerTerminalManager()
    const win = createMockWindow()
    const created = manager.createTab(win, {
      tabId: 'tab-1',
      threadId: 'thread-1',
      workspacePath: 'C:\\repo',
      cols: 80,
      rows: 24
    })

    expect(created.tabId).toBe('tab-1')
    expect(created.pid).toBe(4321)
    expect(spawnMock).toHaveBeenCalledTimes(1)

    onDataHandler?.('hello\r\n')
    expect(vi.mocked(win.webContents.send)).toHaveBeenCalledWith(
      'viewer:terminal:data',
      { tabId: 'tab-1', data: 'hello\r\n' }
    )

    const attached = manager.attachTab(win, 'tab-1')
    expect(attached.buffer).toContain('hello')
    expect(attached.pid).toBe(4321)
  })

  it('writes/resizes active terminal and marks exit state', () => {
    const manager = new ViewerTerminalManager()
    const win = createMockWindow()
    manager.createTab(win, {
      tabId: 'tab-1',
      threadId: 'thread-1',
      workspacePath: 'C:\\repo',
      cols: 80,
      rows: 24
    })

    manager.write(win, { tabId: 'tab-1', data: 'echo hi\r' })
    manager.resize(win, { tabId: 'tab-1', cols: 120, rows: 40 })
    expect(mockPty.write).toHaveBeenCalledWith('echo hi\r')
    expect(mockPty.resize).toHaveBeenCalledWith(120, 40)

    onExitHandler?.({ exitCode: 0, signal: null })
    expect(vi.mocked(win.webContents.send)).toHaveBeenCalledWith(
      'viewer:terminal:exit',
      { tabId: 'tab-1', code: 0, signal: null }
    )

    const attached = manager.attachTab(win, 'tab-1')
    expect(attached.exited?.code).toBe(0)
  })

  it('kills pty when disposing a tab', () => {
    const manager = new ViewerTerminalManager()
    const win = createMockWindow()
    manager.createTab(win, {
      tabId: 'tab-1',
      threadId: 'thread-1',
      workspacePath: 'C:\\repo',
      cols: 80,
      rows: 24
    })

    manager.destroyTab(win, 'tab-1')
    expect(mockPty.kill).toHaveBeenCalledTimes(1)
    expect(() => manager.attachTab(win, 'tab-1')).toThrow('Terminal tab not found')
  })
})
