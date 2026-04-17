export function stripUtf8Bom(input: string): string {
  return input.charCodeAt(0) === 0xfeff ? input.slice(1) : input
}

export function parseJsonConfig<T>(raw: string, fallback: T): T {
  const trimmed = stripUtf8Bom(raw).trim()
  if (!trimmed) return fallback
  try {
    const parsed = JSON.parse(trimmed) as unknown
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      return parsed as T
    }
    return fallback
  } catch {
    return fallback
  }
}
