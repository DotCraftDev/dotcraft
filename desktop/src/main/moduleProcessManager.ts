import { promises as fs } from 'fs'
import { ChildProcess, execFile, spawn } from 'child_process'
import * as path from 'path'
import { app } from 'electron'
import type { WireProtocolClient } from './WireProtocolClient'
import type { DiscoveredModule } from './moduleScanner'
import { QrFileWatcher, type QrUpdatePayload } from './qrWatcher'

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
  outputLines: string[]
  stderrLines: string[]
  crashHint: string | null
}

export interface ModuleStatusEntry {
  processState: ProcessState
  connected: boolean
  restartCount: number
  lastExitCode: number | null
  lastStderrExcerpt?: string[]
  crashHint?: string
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
const AUTO_START_STAGGER_MS = 500
const LOG_RING_BUFFER_LINES = 100
const STDERR_EXCERPT_LINES = 20

function appendLines(target: string[], raw: string): void {
  const lines = raw
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
  for (const line of lines) {
    target.push(line)
    if (target.length > LOG_RING_BUFFER_LINES) {
      target.splice(0, target.length - LOG_RING_BUFFER_LINES)
    }
  }
}

function inferCrashHint(lines: string[]): string | null {
  const joined = lines.join('\n')
  if (joined.includes('ECONNREFUSED')) {
    return 'Cannot connect to AppServer. Check that DotCraft is running.'
  }
  if (joined.includes('MODULE_NOT_FOUND')) {
    return 'Module dependency missing. Try reinstalling the module.'
  }
  if (joined.includes('ENOENT')) {
    return 'Config file not found.'
  }
  return null
}

function resolveNodeBinary(): string {
  if (app.isPackaged) {
    return process.execPath
  }
  return 'node'
}

function buildModuleProcessEnv(): NodeJS.ProcessEnv {
  const env = { ...process.env }
  if (app.isPackaged) {
    env.ELECTRON_RUN_AS_NODE = '1'
  }
  return env
}

export class ModuleProcessManager {
  private readonly workspacePath: string
  private readonly getWireClient: () => WireProtocolClient | null
  private readonly onStatusChanged: (statusMap: ModuleStatusMap) => void
  private readonly getCachedModules: () => DiscoveredModule[] | null
  private readonly qrWatcher: QrFileWatcher
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
    onQrUpdate: (payload: QrUpdatePayload) => void
  }) {
    this.workspacePath = options.workspacePath
    this.getWireClient = options.getWireClient
    this.onStatusChanged = options.onStatusChanged
    this.getCachedModules = options.getCachedModules
    this.qrWatcher = new QrFileWatcher({
      workspacePath: options.workspacePath,
      onQrUpdate: options.onQrUpdate
    })
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
        promoteTimer: null,
        outputLines: [],
        stderrLines: [],
        crashHint: null
      } satisfies ManagedModuleProcess)

    entry.channelName = module.channelName
    entry.expectedStop = false
    entry.startedStableAt = null
    entry.spawnedAt = Date.now()
    entry.state = 'starting'
    entry.restartCount = 0
    entry.outputLines = []
    entry.stderrLines = []
    entry.crashHint = null
    this.managed.set(moduleId, entry)
    this.emitStatusIfChanged()

    const bundleCliPath = path.join(module.absolutePath, 'dist', 'cli.bundle.js')
    let cliPath = bundleCliPath
    try {
      await fs.access(bundleCliPath)
    } catch {
      cliPath = path.join(module.absolutePath, 'dist', 'cli.js')
    }
    const child = spawn(resolveNodeBinary(), [cliPath, '--workspace', this.workspacePath], {
      cwd: this.workspacePath,
      stdio: ['pipe', 'pipe', 'pipe'],
      env: buildModuleProcessEnv()
    })
    entry.process = child

    child.stdout?.on('data', (buffer: Buffer) => {
      const text = buffer.toString('utf-8')
      appendLines(entry.outputLines, text)
      this.emitStatusIfChanged()
      const printable = text.trim()
      if (!printable) return
      console.log(`[module:${module.moduleId}] ${printable}`)
    })
    child.stderr?.on('data', (buffer: Buffer) => {
      const text = buffer.toString('utf-8')
      appendLines(entry.outputLines, text)
      appendLines(entry.stderrLines, text)
      this.emitStatusIfChanged()
      const printable = text.trim()
      if (!printable) return
      console.warn(`[module:${module.moduleId}] ${printable}`)
    })

    child.once('error', (error) => {
      const message = error instanceof Error ? error.message : String(error)
      appendLines(entry.outputLines, message)
      appendLines(entry.stderrLines, message)
      entry.crashHint = inferCrashHint(entry.outputLines)
      entry.process = null
      entry.state = 'crashed'
      entry.lastExitCode = null
      this.qrWatcher.stopWatching(module.moduleId)
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

    if (module.requiresInteractiveSetup) {
      void this.qrWatcher.startWatching(module.moduleId)
    }

    this.ensurePoller()
    return { ok: true }
  }

  async stop(moduleId: string): Promise<StopResult> {
    const entry = this.managed.get(moduleId)
    if (!entry) {
      return { ok: true }
    }
    await this.stopInternal(entry, { preserveExternalChannels: false })
    return { ok: true }
  }

  async stopAll(options?: { preserveExternalChannels?: boolean }): Promise<void> {
    const tasks: Promise<void>[] = []
    for (const entry of this.managed.values()) {
      tasks.push(this.stopInternal(entry, options))
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
        lastExitCode: entry.lastExitCode,
        lastStderrExcerpt:
          entry.stderrLines.length > 0
            ? entry.stderrLines.slice(-STDERR_EXCERPT_LINES)
            : undefined,
        crashHint: entry.crashHint ?? undefined
      }
    }
    return status
  }

  getRecentLogs(moduleId: string): string[] {
    return [...(this.managed.get(moduleId)?.outputLines ?? [])]
  }

  getRunningModuleIds(): string[] {
    const ids: string[] = []
    for (const [moduleId, entry] of this.managed) {
      if (entry.state === 'starting' || entry.state === 'running') {
        ids.push(moduleId)
      }
    }
    return ids
  }

  getQrStatus(moduleId: string): { active: boolean; qrDataUrl: string | null } {
    return this.qrWatcher.getStatus(moduleId)
  }

  async autoStartModules(enabledIds: string[]): Promise<void> {
    for (let index = 0; index < enabledIds.length; index += 1) {
      const moduleId = enabledIds[index]
      try {
        await this.start(moduleId)
      } catch (error) {
        console.warn(`[module:${moduleId}] auto-start failed`, error)
      }
      if (index < enabledIds.length - 1) {
        await new Promise((resolve) => setTimeout(resolve, AUTO_START_STAGGER_MS))
      }
    }
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
      this.qrWatcher.stopWatching(moduleId)
      this.stopPollerIfIdle()
      this.emitStatusIfChanged()
      return
    }

    entry.state = 'crashed'
    entry.crashHint = inferCrashHint(entry.outputLines)
    this.lastPolledConnected.set(moduleId, false)
    const runDurationMs = Date.now() - entry.spawnedAt
    const shouldRestart = runDurationMs >= STABLE_RESTART_MIN_MS && entry.restartCount < 3
    if (!shouldRestart) {
      this.qrWatcher.stopWatching(moduleId)
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

  private async stopInternal(
    entry: ManagedModuleProcess,
    options?: { preserveExternalChannels?: boolean }
  ): Promise<void> {
    if (entry.state === 'stopped' && !entry.process) {
      entry.expectedStop = false
      this.lastPolledConnected.set(entry.moduleId, false)
      this.qrWatcher.stopWatching(entry.moduleId)
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
      this.qrWatcher.stopWatching(entry.moduleId)
      if (options?.preserveExternalChannels !== true) {
        await this.removeExternalChannel(entry.channelName)
      }
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
        const wasConnected = this.lastPolledConnected.get(entry.moduleId) ?? false
        const isConnected = status?.running === true
        this.lastPolledConnected.set(entry.moduleId, isConnected)

        const module = this.findModule(entry.moduleId)
        if (module?.requiresInteractiveSetup) {
          if (isConnected && !wasConnected) {
            this.qrWatcher.onChannelConnected(entry.moduleId)
          } else if (!isConnected && wasConnected) {
            this.qrWatcher.onChannelDisconnected(entry.moduleId)
          }
        }

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
