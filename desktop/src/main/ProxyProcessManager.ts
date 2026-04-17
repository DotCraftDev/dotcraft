import { spawn, ChildProcess, execFileSync } from 'child_process'
import { EventEmitter } from 'events'
import { existsSync } from 'fs'
import { join, resolve as resolvePath } from 'path'

export type ProxyBinarySource = 'bundled' | 'path' | 'custom'

export interface ProxyProcessManagerOptions {
  workspacePath: string
  configPath: string
  binarySource?: ProxyBinarySource
  binaryPath?: string
}

export interface ResolvedProxyBinaryInfo {
  source: ProxyBinarySource
  path: string | null
}

export type ProxyProcessManagerEvent = 'started' | 'stopped' | 'error' | 'crash'

const DEFAULT_BINARY_SOURCE: ProxyBinarySource = 'bundled'
const PROXY_BINARY_BASE_NAMES = ['cliproxyapi', 'cli-proxy-api']

function normalizeBinarySource(source: ProxyBinarySource | undefined): ProxyBinarySource {
  return source === 'bundled' || source === 'path' || source === 'custom'
    ? source
    : DEFAULT_BINARY_SOURCE
}

function resolveBundledProxyBinaryPath(): string | null {
  const names = process.platform === 'win32'
    ? PROXY_BINARY_BASE_NAMES.map((name) => `${name}.exe`)
    : PROXY_BINARY_BASE_NAMES

  for (const binaryName of names) {
    if (typeof process.resourcesPath === 'string' && process.resourcesPath.length > 0) {
      const packagedResource = join(process.resourcesPath, 'bin', binaryName)
      if (existsSync(packagedResource)) {
        return packagedResource
      }
    }

    const devFallback = resolvePath(__dirname, '../../resources/bin', binaryName)
    if (existsSync(devFallback)) {
      return devFallback
    }
  }

  return null
}

function resolvePathProxyBinaryPath(): string | null {
  const whichCommand = process.platform === 'win32' ? 'where' : 'which'
  for (const baseName of PROXY_BINARY_BASE_NAMES) {
    try {
      const result = execFileSync(whichCommand, [baseName], { encoding: 'utf8' }).trim()
      const firstLine = result.split(/\r?\n/)[0]?.trim()
      if (firstLine && existsSync(firstLine)) {
        return firstLine
      }
    } catch {
      // Keep trying fallback names.
    }
  }
  return null
}

export function resolveProxyBinaryLocation(options: {
  binarySource?: ProxyBinarySource
  binaryPath?: string
}): ResolvedProxyBinaryInfo {
  const source = normalizeBinarySource(options.binarySource)
  const customPath = options.binaryPath?.trim()

  if (source === 'custom') {
    return {
      source,
      path: customPath && existsSync(customPath) ? customPath : null
    }
  }

  if (source === 'path') {
    return {
      source,
      path: resolvePathProxyBinaryPath()
    }
  }

  return {
    source,
    path: resolveBundledProxyBinaryPath()
  }
}

export class ProxyProcessManager extends EventEmitter {
  private static readonly FORCE_KILL_TIMEOUT_MS = 2200
  private process: ChildProcess | null = null
  private _workspacePath: string
  private _configPath: string
  private _binarySource: ProxyBinarySource
  private _binaryPath: string | undefined
  private _shutdownRequested = false
  private _killTimer: ReturnType<typeof setTimeout> | null = null

  get isRunning(): boolean {
    return this.process !== null && !this.process.killed && this.process.exitCode === null
  }

  get pid(): number | null {
    return this.process?.pid ?? null
  }

  constructor(options: ProxyProcessManagerOptions) {
    super()
    this._workspacePath = options.workspacePath
    this._configPath = options.configPath
    this._binarySource = normalizeBinarySource(options.binarySource)
    this._binaryPath = options.binaryPath
  }

  private clearShutdownTimers(): void {
    if (this._killTimer) {
      clearTimeout(this._killTimer)
      this._killTimer = null
    }
  }

  private tryKill(signal: NodeJS.Signals): void {
    if (!this.process || this.process.killed) {
      return
    }
    try {
      this.process.kill(signal)
    } catch {
      // Already exited.
    }
  }

  private scheduleForceKill(afterMs: number): void {
    this._killTimer = setTimeout(() => {
      this.tryKill('SIGKILL')
    }, afterMs)
  }

  private resolveBinary(): string {
    const resolved = resolveProxyBinaryLocation({
      binarySource: this._binarySource,
      binaryPath: this._binaryPath
    })
    if (resolved.path) {
      return resolved.path
    }
    if (resolved.source === 'custom') {
      const configuredPath = this._binaryPath?.trim()
      throw new Error(
        configuredPath
          ? `Configured CLIProxyAPI binary not found: ${configuredPath}`
          : 'Custom CLIProxyAPI binary path is empty. Please choose a binary or switch to another source.'
      )
    }
    if (resolved.source === 'path') {
      throw new Error(
        'CLIProxyAPI binary not found on PATH. Install cliproxyapi or switch to the bundled binary in Settings.'
      )
    }
    throw new Error(
      'Bundled CLIProxyAPI binary not found. Reinstall DotCraft Desktop or switch to another binary source in Settings.'
    )
  }

  resolveConfiguredBinary(): ResolvedProxyBinaryInfo {
    return resolveProxyBinaryLocation({
      binarySource: this._binarySource,
      binaryPath: this._binaryPath
    })
  }

  spawn(): void {
    this._shutdownRequested = false
    this.clearShutdownTimers()

    let binaryPath: string
    try {
      binaryPath = this.resolveBinary()
    } catch (err) {
      this.emit('error', err instanceof Error ? err : new Error(String(err)))
      return
    }

    const args = ['--config', this._configPath]
    const proc = spawn(binaryPath, args, {
      cwd: this._workspacePath,
      stdio: ['ignore', 'inherit', 'inherit'],
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
      this.clearShutdownTimers()
      if (this._shutdownRequested) {
        this.emit('stopped')
      } else {
        this.emit('crash', { code, signal })
      }
    })
  }

  shutdown(): void {
    if (!this.process || this._shutdownRequested) {
      return
    }
    this._shutdownRequested = true
    this.clearShutdownTimers()
    this.tryKill('SIGTERM')
    this.scheduleForceKill(ProxyProcessManager.FORCE_KILL_TIMEOUT_MS)
  }

  setWorkspacePath(workspacePath: string): void {
    this._workspacePath = workspacePath
  }

  setConfigPath(configPath: string): void {
    this._configPath = configPath
  }

  setBinarySource(binarySource: ProxyBinarySource): void {
    this._binarySource = normalizeBinarySource(binarySource)
  }

  setBinaryPath(binaryPath: string): void {
    this._binaryPath = binaryPath
  }
}
