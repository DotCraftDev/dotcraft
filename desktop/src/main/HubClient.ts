import { spawn } from 'child_process'
import { existsSync, readFileSync } from 'fs'
import { join } from 'path'
import { homedir } from 'os'
import { resolveBinaryLocation } from './AppServerManager'
import type { BinarySource } from './settings'

export interface HubClientOptions {
  binarySource?: BinarySource
  binaryPath?: string
}

export interface HubLockInfo {
  pid: number
  apiBaseUrl: string
  token: string
  startedAt: string
  version: string
}

export interface HubAppServerResponse {
  workspacePath: string
  canonicalWorkspacePath: string
  state: string
  pid?: number | null
  endpoints: Record<string, string>
  serviceStatus: Record<string, { state: string; url?: string | null; reason?: string | null }>
  serverVersion?: string | null
  startedByHub: boolean
  exitCode?: number | null
  lastError?: string | null
  recentStderr?: string | null
}

export interface HubStatusResponse {
  hubVersion: string
  pid: number
  startedAt: string
  statePath: string
  apiBaseUrl: string
  capabilities: {
    appServerManagement: boolean
    portManagement: boolean
    events: boolean
    notifications: boolean
    tray: boolean
  }
}

export interface HubEvent {
  kind: string
  at: string
  workspacePath?: string | null
  data?: unknown
}

export class HubClientError extends Error {
  constructor(
    readonly code: string,
    message: string
  ) {
    super(message)
    this.name = 'HubClientError'
  }
}

const STARTUP_TIMEOUT_MS = 15_000
const POLL_MS = 200

function hubLockPath(): string {
  return join(homedir(), '.craft', 'hub', 'hub.lock')
}

function isProcessAlive(pid: number): boolean {
  try {
    process.kill(pid, 0)
    return true
  } catch {
    return false
  }
}

function readHubLock(): HubLockInfo | null {
  const lockPath = hubLockPath()
  if (!existsSync(lockPath)) return null
  try {
    const parsed = JSON.parse(readFileSync(lockPath, 'utf8')) as Partial<HubLockInfo>
    if (
      typeof parsed.pid === 'number' &&
      typeof parsed.apiBaseUrl === 'string' &&
      typeof parsed.token === 'string'
    ) {
      return {
        pid: parsed.pid,
        apiBaseUrl: parsed.apiBaseUrl,
        token: parsed.token,
        startedAt: parsed.startedAt ?? '',
        version: parsed.version ?? ''
      }
    }
  } catch {
    // Ignore stale or partially written locks.
  }
  return null
}

async function sleep(ms: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, ms))
}

export class HubClient {
  constructor(private readonly options: HubClientOptions = {}) {}

  async ensureAppServer(workspacePath: string, clientName = 'dotcraft-desktop'): Promise<HubAppServerResponse> {
    const hub = await this.ensureHub()
    return this.requestJson<HubAppServerResponse>(
      hub,
      '/v1/appservers/ensure',
      {
        method: 'POST',
        body: JSON.stringify({
          workspacePath,
          client: { name: clientName, version: process.env.npm_package_version ?? '0.1.0' },
          startIfMissing: true
        })
      }
    )
  }

  async restartAppServer(workspacePath: string): Promise<HubAppServerResponse> {
    const hub = await this.ensureHub()
    return this.requestJson<HubAppServerResponse>(
      hub,
      '/v1/appservers/restart',
      {
        method: 'POST',
        body: JSON.stringify({ workspacePath })
      }
    )
  }

  async stopAppServer(workspacePath: string): Promise<HubAppServerResponse> {
    const hub = await this.ensureHub()
    return this.requestJson<HubAppServerResponse>(
      hub,
      '/v1/appservers/stop',
      {
        method: 'POST',
        body: JSON.stringify({ workspacePath })
      }
    )
  }

  async listAppServers(): Promise<HubAppServerResponse[]> {
    const hub = await this.ensureHub()
    return this.requestJson<HubAppServerResponse[]>(hub, '/v1/appservers', { method: 'GET' })
  }

  async getStatus(): Promise<HubStatusResponse> {
    const hub = await this.ensureHub()
    const response = await fetch(`${hub.apiBaseUrl}/v1/status`)
    if (!response.ok) {
      throw await this.toError(response)
    }
    return await response.json() as HubStatusResponse
  }

  async shutdownHub(): Promise<void> {
    const hub = await this.tryGetLiveHub()
    if (!hub) return
    await this.requestJson<{ ok: boolean }>(hub, '/v1/shutdown', { method: 'POST' })
  }

  async subscribeEvents(onEvent: (event: HubEvent) => void, signal: AbortSignal): Promise<void> {
    const hub = await this.ensureHub()
    const response = await fetch(`${hub.apiBaseUrl}/v1/events`, {
      headers: { Authorization: `Bearer ${hub.token}` },
      signal
    })
    if (!response.ok || !response.body) {
      throw await this.toError(response)
    }

    const reader = response.body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''
    while (!signal.aborted) {
      const { done, value } = await reader.read()
      if (done) break
      buffer += decoder.decode(value, { stream: true })
      let boundary = buffer.indexOf('\n\n')
      while (boundary !== -1) {
        const raw = buffer.slice(0, boundary)
        buffer = buffer.slice(boundary + 2)
        const dataLine = raw.split(/\r?\n/).find((line) => line.startsWith('data:'))
        const data = dataLine?.slice('data:'.length).trim()
        if (data) {
          try {
            onEvent(JSON.parse(data) as HubEvent)
          } catch {
            // Ignore malformed event frames.
          }
        }
        boundary = buffer.indexOf('\n\n')
      }
    }
  }

  private async ensureHub(): Promise<HubLockInfo> {
    const live = await this.tryGetLiveHub()
    if (live) return live

    this.startHub()

    const started = Date.now()
    while (Date.now() - started < STARTUP_TIMEOUT_MS) {
      const info = await this.tryGetLiveHub()
      if (info) return info
      await sleep(POLL_MS)
    }

    throw new HubClientError('hubUnavailable', 'DotCraft Hub could not be started.')
  }

  private async tryGetLiveHub(): Promise<HubLockInfo | null> {
    const info = readHubLock()
    if (!info || !isProcessAlive(info.pid)) return null

    try {
      const response = await fetch(`${info.apiBaseUrl}/v1/status`)
      return response.ok ? info : null
    } catch {
      return null
    }
  }

  private startHub(): void {
    const resolved = resolveBinaryLocation({
      binarySource: this.options.binarySource,
      binaryPath: this.options.binaryPath
    })

    if (!resolved.path) {
      throw new HubClientError('binary-not-found', 'DotCraft binary not found. Hub could not be started.')
    }

    const child = spawn(resolved.path, ['hub'], {
      detached: true,
      stdio: 'ignore',
      windowsHide: true
    })
    child.unref()
  }

  private async requestJson<T>(
    hub: HubLockInfo,
    path: string,
    init: RequestInit
  ): Promise<T> {
    const response = await fetch(`${hub.apiBaseUrl}${path}`, {
      ...init,
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${hub.token}`,
        ...(init.headers ?? {})
      }
    })

    if (!response.ok) {
      throw await this.toError(response)
    }

    return await response.json() as T
  }

  private async toError(response: Response): Promise<HubClientError> {
    try {
      const body = await response.json() as { error?: { code?: string; message?: string } }
      if (body.error?.code || body.error?.message) {
        return new HubClientError(
          body.error.code ?? 'hubRequestFailed',
          body.error.message ?? `Hub request failed with HTTP ${response.status}.`
        )
      }
    } catch {
      // Fall through.
    }
    return new HubClientError(
      response.status === 401 ? 'unauthorized' : 'hubRequestFailed',
      `Hub request failed with HTTP ${response.status}.`
    )
  }
}
