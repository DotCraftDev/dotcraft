/**
 * Next-run labels for Automations Cron cards (local time + optional relative).
 */

export interface NextRunDisplay {
  absolute: string
  relative: string | null
}

function formatRelativeUntil(ms: number): string {
  const diff = ms - Date.now()
  if (diff <= 0) return 'now'
  const seconds = Math.floor(diff / 1000)
  if (seconds < 60) return `in ${seconds}s`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `in ${minutes}m`
  const hours = Math.floor(minutes / 60)
  if (hours < 48) return `in ${hours}h`
  const days = Math.floor(hours / 24)
  return `in ${days}d`
}

/**
 * @param nextRunAtMs UTC epoch ms from server
 * @param enabled when false, absolute line still shows the scheduled instant; relative is omitted
 */
export function formatNextRun(nextRunAtMs: number | null | undefined, enabled: boolean): NextRunDisplay {
  if (nextRunAtMs == null) {
    return { absolute: 'Not scheduled', relative: null }
  }
  const d = new Date(nextRunAtMs)
  const absolute = Number.isNaN(d.getTime())
    ? '—'
    : d.toLocaleString(undefined, {
        month: 'short',
        day: 'numeric',
        year: d.getFullYear() !== new Date().getFullYear() ? 'numeric' : undefined,
        hour: '2-digit',
        minute: '2-digit'
      })
  const relative = enabled ? formatRelativeUntil(nextRunAtMs) : null
  return { absolute, relative }
}
