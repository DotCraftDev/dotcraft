import { parseAnsi } from '../../utils/ansi'

interface AnsiPreProps {
  text: string
  maxHeight?: number
  colorWhenNoSgr?: string
  truncatedLinesOver?: number
}

export function AnsiPre({
  text,
  maxHeight,
  colorWhenNoSgr = 'var(--text-secondary)',
  truncatedLinesOver
}: AnsiPreProps): JSX.Element {
  const lines = text.split('\n')
  const shouldTruncate = truncatedLinesOver != null && lines.length > truncatedLinesOver
  const visibleLines = shouldTruncate && truncatedLinesOver != null
    ? [...lines.slice(0, truncatedLinesOver), '…']
    : lines

  return (
    <pre
      style={{
        margin: 0,
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-all',
        overflow: 'auto',
        maxHeight: maxHeight != null ? `${maxHeight}px` : undefined,
        fontFamily: 'var(--font-mono)',
        color: colorWhenNoSgr
      }}
    >
      {visibleLines.map((line, lineIdx) => {
        const spans = parseAnsi(line)
        return (
          <span key={lineIdx}>
            {spans.length > 0
              ? spans.map((span, spanIdx) => {
                const fg = span.inverse ? (span.bg ?? colorWhenNoSgr) : (span.fg ?? colorWhenNoSgr)
                const bg = span.inverse ? span.fg : span.bg
                const decorations = [
                  span.underline ? 'underline' : '',
                  span.strike ? 'line-through' : ''
                ].filter((value) => value.length > 0).join(' ')
                return (
                  <span
                    key={spanIdx}
                    style={{
                      color: fg,
                      backgroundColor: bg,
                      fontWeight: span.bold ? 600 : undefined,
                      opacity: span.dim ? 0.65 : undefined,
                      fontStyle: span.italic ? 'italic' : undefined,
                      textDecoration: decorations || undefined
                    }}
                  >
                    {span.text}
                  </span>
                )
              })
              : line}
            {lineIdx < visibleLines.length - 1 ? '\n' : null}
          </span>
        )
      })}
    </pre>
  )
}
