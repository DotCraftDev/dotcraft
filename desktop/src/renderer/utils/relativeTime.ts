/**
 * Formats an ISO 8601 date string as a compact relative time label.
 * Used in the sidebar thread list (spec §9.5).
 *
 * Rules:
 *  < 1 min   → "just now"
 *  < 1 hour  → "Xm"
 *  < 24 hrs  → "Xh"
 *  < 7 days  → "Xd"
 *  < 30 days → "Xw"
 *  otherwise → "Xmo"
 */
export function formatRelativeTime(isoDate: string, now: Date = new Date()): string {
  const date = new Date(isoDate)
  const diffMs = now.getTime() - date.getTime()
  const diffSec = Math.floor(diffMs / 1000)
  const diffMin = Math.floor(diffSec / 60)
  const diffHours = Math.floor(diffMin / 60)
  const diffDays = Math.floor(diffHours / 24)
  const diffWeeks = Math.floor(diffDays / 7)
  const diffMonths = Math.floor(diffDays / 30)

  if (diffSec < 60) return 'just now'
  if (diffMin < 60) return `${diffMin}m`
  if (diffHours < 24) return `${diffHours}h`
  if (diffDays < 7) return `${diffDays}d`
  if (diffDays < 30) return `${diffWeeks}w`
  return `${diffMonths}mo`
}
