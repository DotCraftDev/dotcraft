import { spawn, ChildProcess } from 'child_process'
import { EventEmitter } from 'events'
import { Readable, Writable } from 'stream'
import { resolve as resolvePath } from 'path'
import { existsSync } from 'fs'
import { execFileSync } from 'child_process'

export type AppServerManagerEvent =
  | 'started'
  | 'stopped'
  | 'error'
  | 'crash'

export interface AppServerManagerOptions {
  binaryPath?: string
  workspacePath: string
  listenUrl?: string
}

/**
 * Manages the DotCraft AppServer subprocess lifecycle.
 * Spawns `dotcraft app-server` (optionally with `--listen`) and manages transport streams.
 * Emits lifecycle events for the WireProtocolClient and Main Process to consume.
 */
export class AppServerManager extends EventEmitter {
  private process: ChildProcess | null = null
  private _workspacePath: string
  private _binaryPath: string | undefined
  private _listenUrl: string | undefined
  private _shutdownRequested = false
  private _killTimer: ReturnType<typeof setTimeout> | null = null

  get stdin(): Writable | null {
    return this.process?.stdin ?? null
  }

  get stdout(): Readable | null {
    return this.process?.stdout ?? null
  }

  get isRunning(): boolean {
    return this.process !== null && !this.process.killed && this.process.exitCode === null
  }

  constructor(options: AppServerManagerOptions) {
    super()
    this._workspacePath = options.workspacePath
    this._binaryPath = options.binaryPath
    this._listenUrl = options.listenUrl
  }

  /**
   * Resolves the dotcraft binary path.
   * Order: explicit setting -> PATH lookup -> bundled binary
   */
  private resolveBinary(): string {
    if (this._binaryPath && existsSync(this._binaryPath)) {
      return this._binaryPath
    }

    // Try PATH lookup
    const whichCommand = process.platform === 'win32' ? 'where' : 'which'
    try {
      const result = execFileSync(whichCommand, ['dotcraft'], { encoding: 'utf8' }).trim()
      const firstLine = result.split('\n')[0].trim()
      if (firstLine && existsSync(firstLine)) {
        return firstLine
      }
    } catch {
      // Not on PATH
    }

    // Try bundled binary next to the app executable
    const bundledPath = resolvePath(
      process.execPath,
      '..',
      process.platform === 'win32' ? 'dotcraft.exe' : 'dotcraft'
    )
    if (existsSync(bundledPath)) {
      return bundledPath
    }

    throw new Error(
      'DotCraft AppServer binary not found. Please install DotCraft or configure the binary path in Settings.'
    )
  }

  /**
   * Spawns the AppServer subprocess.
   * Emits 'started' on success, 'error' if binary is not found.
   */
  spawn(): void {
    this._shutdownRequested = false

    let binaryPath: string
    try {
      binaryPath = this.resolveBinary()
    } catch (err) {
      this.emit('error', err instanceof Error ? err : new Error(String(err)))
      return
    }

    const args = ['app-server']
    if (this._listenUrl) {
      args.push('--listen', this._listenUrl)
    }
    const useStdio = !this._listenUrl || this._listenUrl.startsWith('ws+stdio://')
    const proc = spawn(binaryPath, args, {
      cwd: this._workspacePath,
      stdio: useStdio ? ['pipe', 'pipe', 'inherit'] : ['ignore', 'inherit', 'inherit'],
      windowsHide: true
    })

    this.process = proc

    proc.on('spawn', () => {
      this.emit('started')
    })

    proc.on('error', (err) => {
      if (!this._shutdownRequested) {
        this.emit('error', err)
      }
    })

    proc.on('exit', (code, signal) => {
      this.process = null
      if (this._killTimer) {
        clearTimeout(this._killTimer)
        this._killTimer = null
      }
      if (this._shutdownRequested) {
        this.emit('stopped')
      } else {
        this.emit('crash', { code, signal })
      }
    })
  }

  /**
   * Gracefully shuts down the AppServer.
   * Closes stdin (sends EOF) when a pipe exists (stdio / ws+stdio).
   * When stdin is not piped (pure WebSocket listen mode), sends SIGTERM instead.
   * Force-kills after 5s if still running.
   */
  shutdown(): void {
    if (!this.process || this._shutdownRequested) {
      return
    }
    this._shutdownRequested = true

    try {
      if (this.process.stdin) {
        this.process.stdin.end()
      } else {
        try {
          this.process.kill('SIGTERM')
        } catch {
          // Process already gone
        }
      }
    } catch {
      // stdin may already be closed
    }

    // Force kill after 5s
    this._killTimer = setTimeout(() => {
      if (this.process && !this.process.killed) {
        try {
          this.process.kill('SIGKILL')
        } catch {
          // Process already gone
        }
      }
    }, 5000)
  }

  /**
   * Updates the workspace path for future spawns (e.g. workspace switching).
   */
  setWorkspacePath(workspacePath: string): void {
    this._workspacePath = workspacePath
  }

  /**
   * Updates the binary path (e.g. from Settings).
   */
  setBinaryPath(binaryPath: string): void {
    this._binaryPath = binaryPath
  }

  /**
   * Updates the listen URL for future spawns.
   */
  setListenUrl(listenUrl: string | undefined): void {
    this._listenUrl = listenUrl
  }
}
