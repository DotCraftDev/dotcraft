/**
 * WebSearch / WebFetch / SearchTools display — aligned with DotCraft.Core
 * CoreToolDisplays and TUI `tool_format.rs`.
 */

import { translate, type AppLocale } from '../../shared/locales'

const WEB_TOOLS = new Set(['WebSearch', 'WebFetch', 'SearchTools'])

export function isWebToolName(toolName: string): boolean {
  return WEB_TOOLS.has(toolName)
}

export function truncate(s: string, max: number): string {
  if (max <= 0) return ''
  const chars = [...s]
  if (chars.length <= max) return s
  return chars.slice(0, max).join('') + '…'
}

/** JSON string field only — matches TUI `parse_string_field` / standalone invocation detection. */
function getJsonStringField(args: Record<string, unknown> | undefined, key: string): string | undefined {
  if (!args) return undefined
  const v = args[key]
  return typeof v === 'string' ? v : undefined
}

/**
 * Human-readable invocation line (matches CoreToolDisplays / format_invocation_display).
 * Returns null when required string fields are missing or not JSON strings (TUI: fall back to generic "Called …").
 */
export function formatInvocationDisplay(
  toolName: string,
  args: Record<string, unknown> | undefined,
  locale: AppLocale
): string | null {
  if (toolName === 'WebSearch') {
    const qRaw = getJsonStringField(args, 'query')
    if (qRaw === undefined) return null
    const q = truncate(qRaw, 80)
    return translate(locale, 'toolCall.webSearch.invocation', { query: q })
  }
  if (toolName === 'WebFetch') {
    const uRaw = getJsonStringField(args, 'url')
    if (uRaw === undefined) return null
    const u = truncate(uRaw, 80)
    return translate(locale, 'toolCall.webFetch.invocation', { url: u })
  }
  if (toolName === 'SearchTools') {
    const qRaw = getJsonStringField(args, 'query')
    if (qRaw === undefined) return null
    const q = truncate(qRaw, 60)
    return translate(locale, 'toolCall.searchTools.invocation', { query: q })
  }
  return null
}

/** When true, ToolCallCard should use "Calling …" + toolName; when false, show standalone sentence only (TUI parity). */
export function invocationNeedsCallingPrefix(
  toolName: string,
  args: Record<string, unknown> | undefined
): boolean {
  if (!isWebToolName(toolName)) return true
  return formatInvocationDisplay(toolName, args, 'en') === null
}

function peelJsonStringWrapper(parsed: unknown): unknown {
  if (typeof parsed === 'string') {
    try {
      return JSON.parse(parsed) as unknown
    } catch {
      return parsed
    }
  }
  return parsed
}

function hostFromUrl(url: string): string {
  try {
    const u = new URL(url)
    return u.hostname
  } catch {
    const rest = url
      .replace(/^https:\/\//i, '')
      .replace(/^http:\/\//i, '')
      .replace(/^ftp:\/\//i, '')
    const hostPort = rest.split(/[/?#]/)[0] ?? ''
    const host = hostPort.includes('@') ? hostPort.split('@').pop() ?? hostPort : hostPort
    return host
  }
}

function formatIntGrouped(n: number): string {
  return n.toLocaleString('en-US', { maximumFractionDigits: 0 })
}

function jsonNumberToInt(v: unknown): number | null {
  if (typeof v === 'number' && Number.isFinite(v)) return Math.trunc(v)
  return null
}

/**
 * Structured result lines (matches ToolRegistry.FormatToolResult for web tools).
 * Returns null to fall back to generic raw preview.
 */
export function formatResultSummary(toolName: string, result: string | undefined): string[] | null {
  const trimmed = result?.trim() ?? ''
  if (trimmed === '') return null

  if (toolName === 'SearchTools') {
    const first = trimmed
      .split('\n')
      .map((l) => l.trim())
      .find((l) => l.length > 0)
    return first ? [first] : null
  }

  if (toolName === 'WebSearch') {
    let root: unknown
    try {
      root = JSON.parse(trimmed) as unknown
    } catch {
      return null
    }
    root = peelJsonStringWrapper(root)
    return parseWebSearchResult(root)
  }

  if (toolName === 'WebFetch') {
    let root: unknown
    try {
      root = JSON.parse(trimmed) as unknown
    } catch {
      return null
    }
    root = peelJsonStringWrapper(root)
    return parseWebFetchResult(root)
  }

  return null
}

function parseWebSearchResult(root: unknown): string[] | null {
  if (root === null || typeof root !== 'object' || Array.isArray(root)) return null
  const obj = root as Record<string, unknown>

  if ('error' in obj && obj.error != null) {
    const msg = String(obj.error).trim()
    return msg ? [`Error: ${msg}`] : null
  }

  const resultsProp = obj.results
  if (!Array.isArray(resultsProp)) return null

  const count = resultsProp.length
  if (count === 0) {
    return ['No results found.']
  }

  const lines: string[] = []
  lines.push(`${count} result${count === 1 ? '' : 's'}:`)

  let i = 1
  for (const item of resultsProp) {
    if (item === null || typeof item !== 'object' || Array.isArray(item)) {
      i++
      continue
    }
    const row = item as Record<string, unknown>
    const title = row.title != null ? String(row.title) : null
    const url = row.url != null ? String(row.url) : null
    const titleText = truncate(title ?? url ?? '?', 70)
    let domain = ''
    if (url && url.trim()) {
      domain = hostFromUrl(url)
    }
    const line =
      domain && domain.length > 0
        ? `${i}. ${titleText} — ${domain}`
        : `${i}. ${titleText}`
    lines.push(line)
    i++
  }

  return lines
}

function parseWebFetchResult(root: unknown): string[] | null {
  if (root === null || typeof root !== 'object' || Array.isArray(root)) return null
  const obj = root as Record<string, unknown>

  if ('error' in obj && obj.error != null) {
    const msg = String(obj.error).trim()
    return msg ? [`Error: ${msg}`] : null
  }

  const parts: string[] = []

  const status = jsonNumberToInt(obj.status)
  if (status !== null) parts.push(String(status))

  const len = obj.length
  if (typeof len === 'number' && Number.isFinite(len)) {
    parts.push(`${formatIntGrouped(Math.trunc(len))} chars`)
  }

  if (obj.extractor != null) {
    const ext = String(obj.extractor).trim()
    if (ext) parts.push(ext)
  }

  if (obj.truncated === true) {
    parts.push('truncated')
  }

  if (parts.length === 0) return null
  return [parts.join(' · ')]
}

export function getWebToolSectionLabel(toolName: string, locale: AppLocale): string | null {
  if (toolName === 'WebSearch') {
    return translate(locale, 'toolCall.webSearch.section')
  }
  if (toolName === 'WebFetch') {
    return translate(locale, 'toolCall.webFetch.section')
  }
  if (toolName === 'SearchTools') {
    return translate(locale, 'toolCall.searchTools.section')
  }
  return null
}

/** Icon from ToolRegistry / server (🔍 WebSearch, 🌐 WebFetch); SearchTools uses wrench-style search. */
export function getWebToolIcon(toolName: string): string {
  if (toolName === 'WebSearch') return '🔍'
  if (toolName === 'WebFetch') return '🌐'
  if (toolName === 'SearchTools') return '🔧'
  return '🔧'
}
