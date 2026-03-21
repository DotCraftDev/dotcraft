/**
 * Formats a raw token count as a compact human-readable string.
 * Examples: 0 → "0", 500 → "500", 1000 → "1.0k", 1234 → "1.2k", 12345 → "12.3k"
 */
export function formatTokenCount(n: number): string {
  if (n >= 1000) return `${(n / 1000).toFixed(1)}k`
  return String(n)
}
