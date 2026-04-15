import { promises as fs } from 'fs'
import { ChildProcess, execFile, spawn } from 'child_process'
import * as path from 'path'
import type { WireProtocolClient } from './WireProtocolClient'
import type { DiscoveredModule } from './moduleScanner'

export type ProcessState = 'starting' | 'running' | 'stopping' | 'stopped' | 'crashed'

interface ManagedModuleProcess {
  moduleId: string
  channelName: string
  process: ChildProcess | null
  state: ProcessState
  restartCount: number
  lastExitCode: number | null
  spawnedAt: number
  startedStableAt: number | null
  expectedStop: boolean
  promoteTimer: ReturnType<typeof setTimeout> | null
}

export interface ModuleStatusEntry {
  processState: ProcessState
  connected: boolean
  restartCount: number
  lastExitCode: number | null
}

export type ModuleStatusMap = Record<string, ModuleStatusEntry>

interface ChannelStatusWire {
  name: string
  running: boolean
}

interface StartResult {
  ok: boolean
  error?: string
}

interface StopResult {
  ok: boolean
  error?: string
}

const POLL_INTERVAL_MS = 3_000
const PROMOTE_TO_RUNNING_MS = 5_000
const STABLE_RESTART_MIN_MS = 10_000
const STABLE_RESET_RESTART_MS = 60_000
const STOP_GRACE_MS = 5_000

function isNodeSpawnError(error: unknown): boolean {
  const code = (error as NodeJS.ErrnoException | null)?.code
  return code === 'ENOENT'
}

export class ModuleProcessManager {
  private readonly workspacePath: string
  private readonly getWireClient: () => WireProtocolClient | null
  private readonly onStatusChanged: (statusMap: ModuleStatusMap) => void
  private readonly getCachedModules: () => DiscoveredModule[] | null
  private readonly managed = new Map<string, ManagedModuleProcess>()
  private readonly stopWaiters = new Map<string, Promise<void>>()
  private statusPollTimer: ReturnType<typeof setInterval> | null = null
  private lastPolledConnected = new Map<string, boolean>()
  private lastBroadcastSnapshot = ''

  constructor(options: {
    workspacePath: string
    getWireClient: () => WireProtocolClient | null
    onStatusChanged: (statusMap: ModuleStatusMap) => void
    getCachedModules: () => DiscoveredModule[] | null
  }) {
    this.workspacePath = options.workspacePath
    this.getWireClient = options.getWireClient
    this.onStatusChanged = options.onStatusChanged
    this.getCachedModules = options.getCachedModules
  }

  async start(moduleId: string): Promise<StartResult> {
    const existing = this.managed.get(moduleId)
    if (existing && (existing.state === 'starting' || existing.state === 'running')) {
      return { ok: true }
    }

    const module = this.findModule(moduleId)
    if (!module) {
      return { ok: false, error: `Module '${moduleId}' not found` }
    }

    try {
      await fs.access(path.join(this.workspacePath, '.craft', module.configFileName))
    } catch {
      return { ok: false, error: `Missing config file: ${module.configFileName}` }
    }

    const client = this.getWireClient()
    if (!client) {
      return { ok: false, error: 'AppServer is not connected' }
    }
    try {
      await client.sendRequest('externalChannel/upsert', {
        channel: {
          name: module.channelName,
          enabled: true,
          transport: 'websocket'
        }
      })
    } catch (error) {
      return {
        ok: false,
        error: error instanceof Error ? error.message : String(error)
      }
    }

    const entry: ManagedModuleProcess =
      existing ??
      ({
        moduleId: module.moduleId,
        channelName: module.channelName,
        process: null,
        state: 'stopped',
        restartCount: 0,
        lastExitCode: null,
        spawnedAt: 0,
        startedStableAt: null,
        expectedStop: false,
        promoteTimer: null
      } satisfies ManagedModuleProcess)

    entry.channelName = module.channelName
    entry.expectedStop = false
    entry.startedStableAt = null
    entry.spawnedAt = Date.now()
    entry.state = 'starting'
    this.managed.set(moduleId, entry)
    this.emitStatusIfChanged()

    const bundleCliPath = path.join(module.absolutePath, 'dist', 'cli.bundle.js')
    let cliPath = bundleCliPath
    try {
      await fs.access(bundleCliPath)
    } catch {
      cliPath = path.join(module.absolutePath, 'dist', 'cli.js')
    }
    const child = spawn('node', [cliPath, '--workspace', this.workspacePath], {
      cwd: this.workspacePath,
      stdio: ['pipe', 'pipe', 'pipe']
    })
    entry.process = child

    child.stdout?.on('data', (buffer: Buffer) => {
      const text = buffer.toString('utf-8').trim()
      if (text) {
        console.log(`[module:${module.moduleId}] ${text}`)
      }
    })
    child.stderr?.on('data', (buffer: Buffer) => {
      const text = buffer.toString('utf-8').trim()
      if (text) {
        console.warn(`[module:${module.moduleId}] ${text}`)
      }
    })

    child.once('error', (error) => {
      entry.process = null
      entry.state = 'crashed'
      entry.lastExitCode = null
      if (isNodeSpawnError(error)) {
        entry.lastExitCode = 127
      }
      this.clearPromoteTimer(entry)
      this.emitStatusIfChanged()
    })

    child.once('exit', (code) => {
      void this.handleExit(module.moduleId, code)
    })

    entry.promoteTimer = setTimeout(() => {
      if (entry.state === 'starting' && entry.process && !entry.process.killed) {
        entry.state = 'running'
        entry.startedStableAt = Date.now()
        this.emitStatusIfChanged()
      }
    }, PROMOTE_TO_RUNNING_MS)

    this.ensurePoller()
    return { ok: true }
  }

  async stop(moduleId: string): Promise<StopResult> {
    const entry = this.managed.get(moduleId)
    if (!entry) {
      return { ok: true }
    }
    await this.stopInternal(entry)
    return { ok: true }
  }

  async stopAll(): Promise<void> {
    const tasks: Promise<void>[] = []
    for (const entry of this.managed.values()) {
      tasks.push(this.stopInternal(entry))
    }
    await Promise.all(tasks)
    this.stopPollerIfIdle()
    this.emitStatusIfChanged()
  }

  getStatusMap(): ModuleStatusMap {
    const status: ModuleStatusMap = {}
    for (const [moduleId, entry] of this.managed) {
      status[moduleId] = {
        processState: entry.state,
        connected: this.lastPolledConnected.get(moduleId) ?? false,
        restartCount: entry.restartCount,
        lastExitCode: entry.lastExitCode
      }
    }
    return status
  }

  private async handleExit(moduleId: string, code: number | null): Promise<void> {
    const entry = this.managed.get(moduleId)
    if (!entry) {
      return
    }

    this.clearPromoteTimer(entry)
    entry.process = null
    entry.lastExitCode = code

    if (entry.expectedStop || entry.state === 'stopping') {
      entry.state = 'stopped'
      entry.expectedStop = false
      entry.startedStableAt = null
      this.lastPolledConnected.set(moduleId, false)
      this.stopPollerIfIdle()
      this.emitStatusIfChanged()
      return
    }

    entry.state = 'crashed'
    this.lastPolledConnected.set(moduleId, false)
    const runDurationMs = Date.now() - entry.spawnedAt
    const shouldRestart = runDurationMs >= STABLE_RESTART_MIN_MS && entry.restartCount < 3
    if (!shouldRestart) {
      this.stopPollerIfIdle()
      this.emitStatusIfChanged()
      return
    }

    entry.restartCount += 1
    this.emitStatusIfChanged()
    const restartResult = await this.start(moduleId)
    if (!restartResult.ok) {
      entry.state = 'crashed'
      this.emitStatusIfChanged()
    }
  }

  private async stopInternal(entry: ManagedModuleProcess): Promise<void> {
    if (entry.state === 'stopped' && !entry.process) {
      entry.expectedStop = false
      this.lastPolledConnected.set(entry.moduleId, false)
      this.emitStatusIfChanged()
      return
    }

    if (this.stopWaiters.has(entry.moduleId)) {
      await this.stopWaiters.get(entry.moduleId)
      return
    }

    entry.expectedStop = true
    entry.state = 'stopping'
    this.clearPromoteTimer(entry)
    this.emitStatusIfChanged()

    const stopPromise = this.waitForStop(entry)
    this.stopWaiters.set(entry.moduleId, stopPromise)
    try {
      await stopPromise
      await this.removeExternalChannel(entry.channelName)
    } finally {
      this.stopWaiters.delete(entry.moduleId)
      this.stopPollerIfIdle()
      this.emitStatusIfChanged()
    }
  }

  private async removeExternalChannel(channelName: string): Promise<void> {
    const client = this.getWireClient()
    if (!client) return
    try {
      await client.sendRequest('externalChannel/remove', { name: channelName })
    } catch {
      // Best-effort cleanup to avoid leaving stale module channels in workspace config.
    }
  }

  private waitForStop(entry: ManagedModuleProcess): Promise<void> {
    const child = entry.process
    if (!child || child.pid == null) {
      entry.process = null
      entry.state = 'stopped'
      entry.startedStableAt = null
      this.lastPolledConnected.set(entry.moduleId, false)
      return Promise.resolve()
    }

    return new Promise<void>((resolve) => {
      let resolved = false
      const finalize = (): void => {
        if (resolved) return
        resolved = true
        entry.process = null
        entry.state = 'stopped'
        entry.startedStableAt = null
        this.lastPolledConnected.set(entry.moduleId, false)
        resolve()
      }

      child.once('exit', () => finalize())

      try {
        child.kill()
      } catch {
        finalize()
        return
      }

      setTimeout(() => {
        if (resolved) return
        if (process.platform === 'win32' && child.pid != null) {
          execFile('taskkill', ['/pid', String(child.pid), '/t', '/f'], () => finalize())
          return
        }
        try {
          child.kill('SIGKILL')
        } catch {
          // Ignore, process may already be gone.
        }
        finalize()
      }, STOP_GRACE_MS)
    })
  }

  private findModule(moduleId: string): DiscoveredModule | null {
    const cached = this.getCachedModules()
    if (!cached) return null
    return cached.find((item) => item.moduleId === moduleId) ?? null
  }

  private clearPromoteTimer(entry: ManagedModuleProcess): void {
    if (entry.promoteTimer) {
      clearTimeout(entry.promoteTimer)
      entry.promoteTimer = null
    }
  }

  private ensurePoller(): void {
    if (this.statusPollTimer) return
    this.statusPollTimer = setInterval(() => {
      void this.pollChannelStatus()
    }, POLL_INTERVAL_MS)
    void this.pollChannelStatus()
  }

  private stopPollerIfIdle(): void {
    const active = [...this.managed.values()].some(
      (entry) => entry.state === 'starting' || entry.state === 'running'
    )
    if (!active && this.statusPollTimer) {
      clearInterval(this.statusPollTimer)
      this.statusPollTimer = null
    }
  }

  private async pollChannelStatus(): Promise<void> {
    const activeEntries = [...this.managed.values()].filter(
      (entry) => entry.state === 'starting' || entry.state === 'running'
    )
    if (activeEntries.length === 0) {
      this.stopPollerIfIdle()
      return
    }

    const client = this.getWireClient()
    if (!client) {
      for (const entry of activeEntries) {
        this.lastPolledConnected.set(entry.moduleId, false)
      }
      this.emitStatusIfChanged()
      return
    }

    try {
      const response = await client.sendRequest<{ channels?: ChannelStatusWire[] }>(
        'channel/status',
        {}
      )
      const channels = new Map<string, ChannelStatusWire>()
      for (const channel of response.channels ?? []) {
        channels.set(channel.name.toLowerCase(), channel)
      }

      for (const entry of this.managed.values()) {
        const status = channels.get(entry.channelName.toLowerCase())
        this.lastPolledConnected.set(entry.moduleId, status?.running === true)

        if (
          entry.state === 'running' &&
          entry.startedStableAt !== null &&
          Date.now() - entry.startedStableAt >= STABLE_RESET_RESTART_MS
        ) {
          entry.restartCount = 0
        }
      }
      this.emitStatusIfChanged()
    } catch {
      for (const entry of activeEntries) {
        this.lastPolledConnected.set(entry.moduleId, false)
      }
      this.emitStatusIfChanged()
    }
  }

  private emitStatusIfChanged(): void {
    const statusMap = this.getStatusMap()
    const snapshot = JSON.stringify(statusMap)
    if (snapshot === this.lastBroadcastSnapshot) return
    this.lastBroadcastSnapshot = snapshot
    this.onStatusChanged(statusMap)
  }
}
