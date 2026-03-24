/**
 * Human-readable labels for the agent `Cron` tool — aligned with
 * DotCraft.Core.Cron.CronToolDisplays (server).
 */

export const CRON_TOOL_NAME = 'Cron'

function formatDuration(seconds: number): string {
  const s = Math.floor(seconds)
  if (s < 60) return `${s}s`
  if (s < 3600) return `${Math.floor(s / 60)}m`
  if (s < 86400) return `${Math.floor(s / 3600)}h`
  return `${Math.floor(s / 86400)}d`
}

function getString(args: Record<string, unknown> | undefined, key: string): string | undefined {
  if (!args) return undefined
  const v = args[key]
  if (v === undefined || v === null) return undefined
  return String(v)
}

function tryGetLong(value: unknown): number | null {
  if (value == null) return null
  if (typeof value === 'number' && Number.isFinite(value)) return Math.trunc(value)
  if (typeof value === 'string' && value.trim() !== '') {
    const n = Number(value)
    return Number.isFinite(n) ? Math.trunc(n) : null
  }
  return null
}

function formatAddCollapsed(args: Record<string, unknown> | undefined): string {
  const name = getString(args, 'name')
  const message = getString(args, 'message') ?? 'task'
  const label = name ?? (message.length > 40 ? `${message.slice(0, 40)}…` : message)

  const delaySec = tryGetLong(args?.delaySeconds)
  if (delaySec != null && delaySec > 0) {
    return `Schedule "${label}" in ${formatDuration(delaySec)}`
  }
  const everySec = tryGetLong(args?.everySeconds)
  if (everySec != null && everySec > 0) {
    return `Schedule "${label}" every ${formatDuration(everySec)}`
  }
  return `Schedule "${label}"`
}

/** Collapsed summary for a completed Cron call (matches CronToolDisplays.Cron). */
export function formatCronCollapsedLabel(args: Record<string, unknown> | undefined): string {
  const action = (getString(args, 'action') ?? '?').trim().toLowerCase()
  switch (action) {
    case 'add':
      return formatAddCollapsed(args)
    case 'list':
      return 'List scheduled jobs'
    case 'remove':
      return `Remove job ${getString(args, 'jobId') ?? '?'}`
    default:
      return `Cron (${action})`
  }
}

/** In-progress line while the Cron tool is running. */
export function formatCronRunningLabel(args: Record<string, unknown> | undefined): string {
  const action = (getString(args, 'action') ?? '?').trim().toLowerCase()
  switch (action) {
    case 'add':
      return 'Scheduling…'
    case 'list':
      return 'Listing scheduled jobs…'
    case 'remove': {
      const jid = getString(args, 'jobId')
      return jid ? `Removing job ${jid}…` : 'Removing job…'
    }
    default:
      return `Running Cron (${action})…`
  }
}

function getJsonStr(obj: Record<string, unknown>, ...keys: string[]): string | undefined {
  for (const k of keys) {
    const v = obj[k]
    if (v === undefined || v === null) continue
    if (typeof v === 'string') return v
    if (typeof v === 'number' || typeof v === 'boolean') return String(v)
  }
  return undefined
}

function getJsonNumber(obj: Record<string, unknown>, ...keys: string[]): number | undefined {
  for (const k of keys) {
    const v = obj[k]
    if (typeof v === 'number' && Number.isFinite(v)) return v
    if (typeof v === 'string' && v.trim() !== '') {
      const n = Number(v)
      if (Number.isFinite(n)) return n
    }
  }
  return undefined
}

/**
 * Parses Cron tool JSON result into display lines (matches CronToolDisplays.CronResult).
 * Returns null when the payload is not a recognized Cron result (caller may fall back to raw text).
 */
export function formatCronResultLines(result: string | undefined): string[] | null {
  if (result == null || result.trim() === '') return null
  let root: unknown
  try {
    root = JSON.parse(result) as unknown
  } catch {
    return null
  }
  if (root === null || typeof root !== 'object' || Array.isArray(root)) return null
  const o = root as Record<string, unknown>

  const err = getJsonStr(o, 'error')
  if (err !== undefined) {
    return [`Error: ${err}`]
  }

  const status = getJsonStr(o, 'status')
  if (status === 'created') {
    const id = getJsonStr(o, 'id', 'Id')
    const jobName = getJsonStr(o, 'name', 'Name')
    const nextRunMs = getJsonNumber(o, 'nextRun', 'NextRun')
    let timeLabel = '—'
    if (nextRunMs != null) {
      const d = new Date(nextRunMs)
      timeLabel = d.toLocaleTimeString(undefined, {
        hour: '2-digit',
        minute: '2-digit',
        hour12: false
      })
    }
    const nameDisplay = jobName ?? id ?? 'job'
    return [`Created: ${nameDisplay}  ·  triggers at ${timeLabel}`]
  }
  if (status === 'removed') {
    const jobId = getJsonStr(o, 'jobId', 'JobId')
    return [`Removed job ${jobId}`]
  }
  if (status === 'not_found') {
    const jobId = getJsonStr(o, 'jobId', 'JobId')
    return [`Job ${jobId} not found`]
  }

  const count = getJsonNumber(o, 'count', 'Count')
  if (count != null) {
    const c = Math.floor(count)
    return c === 0 ? ['No scheduled jobs'] : [`${c} scheduled job${c === 1 ? '' : 's'}`]
  }

  return null
}
