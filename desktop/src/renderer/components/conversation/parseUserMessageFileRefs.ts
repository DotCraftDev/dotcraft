export type UserMessageSegment =
  | { type: 'text'; value: string }
  | { type: 'fileRef'; relativePath: string }

/**
 * Splits user message text into plain segments and @fileRef segments.
 * Matches RichInputArea serializeEditor output: `@` at line/start or after whitespace,
 * then a non-whitespace run (workspace-relative path).
 */
export function parseUserMessageFileRefs(text: string): UserMessageSegment[] {
  const out: UserMessageSegment[] = []
  let buf = ''
  let i = 0

  const flush = (): void => {
    if (buf.length > 0) {
      out.push({ type: 'text', value: buf })
      buf = ''
    }
  }

  while (i < text.length) {
    if (text[i] === '@' && (i === 0 || /\s/.test(text[i - 1]!))) {
      let j = i + 1
      while (j < text.length && !/\s/.test(text[j]!)) {
        j++
      }
      const relativePath = text.slice(i + 1, j)
      if (relativePath.length > 0) {
        flush()
        out.push({ type: 'fileRef', relativePath })
        i = j
        continue
      }
    }
    buf += text[i]!
    i++
  }
  flush()
  return out
}
