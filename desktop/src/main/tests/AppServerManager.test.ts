import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { EventEmitter } from 'events'
import { PassThrough } from 'stream'

// Mock child_process.spawn before importing AppServerManager
vi.mock('child_process', () => ({
  spawn: vi.fn(),
  execFileSync: vi.fn(() => { throw new Error('not found') })
}))

vi.mock('fs', () => ({
  existsSync: vi.fn((p: string) => p === '/usr/bin/dotcraft')
}))

import { AppServerManager, resolveBinaryLocation } from '../AppServerManager'
import { spawn, execFileSync } from 'child_process'
import { existsSync } from 'fs'

function makeMockProcess(): {
  proc: EventEmitter & {
    stdin: PassThrough
    stdout: PassThrough
    killed: boolean
    exitCode: number | null
    kill: ReturnType<typeof vi.fn>
  }
} {
  const proc = new EventEmitter() as EventEmitter & {
    stdin: PassThrough
    stdout: PassThrough
    killed: boolean
    exitCode: number | null
    kill: ReturnType<typeof vi.fn>
  }
  proc.stdin = new PassThrough()
  proc.stdout = new PassThrough()
  proc.killed = false
  proc.exitCode = null
  proc.kill = vi.fn(() => {
    proc.killed = true
    proc.emit('exit', null, 'SIGKILL')
  })
  return { proc }
}

function makeMockProcessNoStdin(): {
  proc: EventEmitter & {
    stdin: null
    stdout: PassThrough
    killed: boolean
    exitCode: number | null
    kill: ReturnType<typeof vi.fn>
  }
} {
  const proc = new EventEmitter() as EventEmitter & {
    stdin: null
    stdout: PassThrough
    killed: boolean
    exitCode: number | null
    kill: ReturnType<typeof vi.fn>
  }
  proc.stdin = null
  proc.stdout = new PassThrough()
  proc.killed = false
  proc.exitCode = null
  proc.kill = vi.fn(() => {
    proc.killed = true
    proc.emit('exit', null, 'SIGKILL')
  })
  return { proc }
}

describe('AppServerManager', () => {
  let manager: AppServerManager
  const mockSpawn = spawn as ReturnType<typeof vi.fn>
  const mockExistsSync = existsSync as ReturnType<typeof vi.fn>
  const mockExecFileSync = execFileSync as ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.clearAllMocks()
    // Default: a bundled binary exists so manager startup succeeds unless a test overrides it.
    mockExistsSync.mockReturnValue(true)
    manager = new AppServerManager({ workspacePath: '/home/user/project' })
  })

  afterEach(() => {
    manager.shutdown()
  })

  it('spawns with correct arguments and working directory', () => {
    const { proc } = makeMockProcess()
    mockSpawn.mockReturnValue(proc)
    mockExistsSync.mockReturnValue(true)
    mockExecFileSync.mockReturnValue('/usr/bin/dotcraft\n')

    manager.spawn()

    expect(mockSpawn).toHaveBeenCalledWith(
      expect.any(String),
      ['app-server'],
      expect.objectContaining({
        cwd: '/home/user/project',
        stdio: ['pipe', 'pipe', 'inherit']
      })
    )
  })

  it('passes --listen for websocket mode and disables stdio pipes', () => {
    const { proc } = makeMockProcess()
    mockSpawn.mockReturnValue(proc)
    mockExistsSync.mockReturnValue(true)
    mockExecFileSync.mockReturnValue('/usr/bin/dotcraft\n')
    manager = new AppServerManager({
      workspacePath: '/home/user/project',
      listenUrl: 'ws://127.0.0.1:9100'
    })

    manager.spawn()

    expect(mockSpawn).toHaveBeenCalledWith(
      expect.any(String),
      ['app-server', '--listen', 'ws://127.0.0.1:9100'],
      expect.objectContaining({
        cwd: '/home/user/project',
        stdio: ['ignore', 'inherit', 'inherit']
      })
    )
  })

  it('keeps stdio pipes enabled for ws+stdio mode', () => {
    const { proc } = makeMockProcess()
    mockSpawn.mockReturnValue(proc)
    mockExistsSync.mockReturnValue(true)
    mockExecFileSync.mockReturnValue('/usr/bin/dotcraft\n')
    manager = new AppServerManager({
      workspacePath: '/home/user/project',
      listenUrl: 'ws+stdio://127.0.0.1:9100'
    })

    manager.spawn()

    expect(mockSpawn).toHaveBeenCalledWith(
      expect.any(String),
      ['app-server', '--listen', 'ws+stdio://127.0.0.1:9100'],
      expect.objectContaining({
        cwd: '/home/user/project',
        stdio: ['pipe', 'pipe', 'inherit']
      })
    )
  })

  it('emits "started" when the process spawns successfully', () => {
    const { proc } = makeMockProcess()
    mockSpawn.mockReturnValue(proc)
    mockExecFileSync.mockReturnValue('/usr/bin/dotcraft\n')

    const started = vi.fn()
    manager.on('started', started)
    manager.spawn()

    proc.emit('spawn')
    expect(started).toHaveBeenCalledOnce()
  })

  it('exposes stdin and stdout after spawn', () => {
    const { proc } = makeMockProcess()
    mockSpawn.mockReturnValue(proc)
    mockExecFileSync.mockReturnValue('/usr/bin/dotcraft\n')

    manager.spawn()
    proc.emit('spawn')

    expect(manager.stdin).toBe(proc.stdin)
    expect(manager.stdout).toBe(proc.stdout)
  })

  it('emits "error" when binary is not found', () => {
    mockExistsSync.mockReturnValue(false)
    mockExecFileSync.mockImplementation(() => { throw new Error('not found') })

    const errorHandler = vi.fn()
    manager.on('error', errorHandler)
    manager.spawn()

    expect(errorHandler).toHaveBeenCalledOnce()
    const err: Error = errorHandler.mock.calls[0][0]
    expect(err.message).toMatch(/not found/i)
  })

  it('resolves the PATH binary when source is set to path', () => {
    mockExistsSync.mockImplementation((p: string) => p === '/usr/bin/dotcraft')
    mockExecFileSync.mockReturnValue('/usr/bin/dotcraft\n')

    const resolved = resolveBinaryLocation({ binarySource: 'path' })

    expect(resolved).toEqual({
      source: 'path',
      path: '/usr/bin/dotcraft'
    })
  })

  it('returns null for a missing custom binary path', () => {
    mockExistsSync.mockReturnValue(false)

    const resolved = resolveBinaryLocation({
      binarySource: 'custom',
      binaryPath: '/tmp/missing-dotcraft'
    })

    expect(resolved).toEqual({
      source: 'custom',
      path: null
    })
  })

  it('emits "crash" when process exits unexpectedly', () => {
    const { proc } = makeMockProcess()
    mockSpawn.mockReturnValue(proc)
    mockExecFileSync.mockReturnValue('/usr/bin/dotcraft\n')

    const crash = vi.fn()
    manager.on('crash', crash)
    manager.spawn()
    proc.emit('spawn')

    proc.emit('exit', 1, null)

    expect(crash).toHaveBeenCalledOnce()
    expect(crash.mock.calls[0][0]).toMatchObject({ code: 1 })
  })

  it('closes stdin on shutdown (sends EOF)', () => {
    const { proc } = makeMockProcess()
    mockSpawn.mockReturnValue(proc)
    mockExecFileSync.mockReturnValue('/usr/bin/dotcraft\n')

    manager.spawn()
    proc.emit('spawn')

    const endSpy = vi.spyOn(proc.stdin, 'end')
    manager.shutdown()

    expect(endSpy).toHaveBeenCalledOnce()
  })

  it('sends SIGTERM on shutdown when stdin is not piped (WebSocket-only mode)', () => {
    const { proc } = makeMockProcessNoStdin()
    mockSpawn.mockReturnValue(proc)
    mockExistsSync.mockReturnValue(true)
    mockExecFileSync.mockReturnValue('/usr/bin/dotcraft\n')
    manager = new AppServerManager({
      workspacePath: '/home/user/project',
      listenUrl: 'ws://127.0.0.1:9100'
    })

    manager.spawn()
    proc.emit('spawn')

    manager.shutdown()

    expect(proc.kill).toHaveBeenCalledWith('SIGTERM')
  })

  it('emits "stopped" (not "crash") after graceful shutdown', () => {
    const { proc } = makeMockProcess()
    mockSpawn.mockReturnValue(proc)
    mockExecFileSync.mockReturnValue('/usr/bin/dotcraft\n')

    const stopped = vi.fn()
    const crash = vi.fn()
    manager.on('stopped', stopped)
    manager.on('crash', crash)
    manager.spawn()
    proc.emit('spawn')

    manager.shutdown()
    proc.emit('exit', 0, null)

    expect(stopped).toHaveBeenCalledOnce()
    expect(crash).not.toHaveBeenCalled()
  })

  it('reports isRunning correctly', () => {
    const { proc } = makeMockProcess()
    mockSpawn.mockReturnValue(proc)
    mockExecFileSync.mockReturnValue('/usr/bin/dotcraft\n')

    expect(manager.isRunning).toBe(false)
    manager.spawn()
    proc.emit('spawn')
    expect(manager.isRunning).toBe(true)

    proc.emit('exit', 0, null)
    expect(manager.isRunning).toBe(false)
  })
})
