import type { CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { DiffLine, FileDiff } from '../../types/toolCall'

export type DiffDisplayMode = 'inline' | 'split'

interface DiffViewerProps {
  diff: FileDiff
  workspacePath: string
  mode?: DiffDisplayMode
}

interface NumberedLine {
  num: string
  content: string
  type: DiffLine['type'] | 'blank'
}

interface SplitRow {
  left: NumberedLine
  right: NumberedLine
}

export function DiffViewer({ diff, workspacePath, mode = 'inline' }: DiffViewerProps): JSX.Element {
  const relativePath = toRelativePath(diff.filePath, workspacePath)

  return (
    <div
      data-testid="diff-viewer"
      data-mode={mode}
      style={{
        fontFamily: 'var(--font-mono)',
        fontSize: '12px',
        lineHeight: '1.55',
        overflow: 'hidden'
      }}
    >
      {mode === 'split'
        ? <SplitDiffBody diff={diff} relativePath={relativePath} />
        : <UnifiedDiffBody diff={diff} relativePath={relativePath} />}
    </div>
  )
}

export function UnifiedDiffBody({ diff, relativePath }: { diff: FileDiff; relativePath?: string }): JSX.Element {
  if (diff.diffHunks.length === 0) {
    return <EmptyDiffMessage />
  }

  let previousOldEnd = 1
  let previousNewEnd = 1

  return (
    <div data-testid="unified-diff-body" style={{ overflowX: 'auto' }}>
      {diff.diffHunks.map((hunk, hunkIdx) => {
        let oldLineNum = hunk.oldStart
        let newLineNum = hunk.newStart
        const unchanged = unchangedBeforeHunk(hunk.oldStart, hunk.newStart, previousOldEnd, previousNewEnd)
        previousOldEnd = hunk.oldStart + hunk.oldLines
        previousNewEnd = hunk.newStart + hunk.newLines

        return (
          <div key={hunkIdx} style={{ minWidth: 'max-content' }}>
            {unchanged > 0 && <UnchangedDivider count={unchanged} />}
            {hunk.lines.map((line, lineIdx) => {
              const oldNum = line.type === 'add' ? '' : String(oldLineNum)
              const newNum = line.type === 'remove' ? '' : String(newLineNum)
              if (line.type !== 'add') oldLineNum++
              if (line.type !== 'remove') newLineNum++
              return (
                <div
                  key={lineIdx}
                  style={{
                    display: 'flex',
                    minWidth: 'max-content',
                    background: diffLineBackground(line.type),
                    whiteSpace: 'pre'
                  }}
                >
                  <span style={lineNumberStyle}>{oldNum}</span>
                  <span style={lineNumberStyle}>{newNum}</span>
                  <span style={markerStyle(line.type)}>
                    {line.type === 'add' ? '+' : line.type === 'remove' ? '-' : ' '}
                  </span>
                  <span
                    title={relativePath}
                    style={{
                      padding: '0 10px 0 4px',
                      color: line.type === 'remove' ? 'var(--text-secondary)' : 'var(--text-primary)'
                    }}
                  >
                    {line.content}
                  </span>
                </div>
              )
            })}
          </div>
        )
      })}
    </div>
  )
}

export function SplitDiffBody({ diff, relativePath }: { diff: FileDiff; relativePath?: string }): JSX.Element {
  if (diff.diffHunks.length === 0) {
    return <EmptyDiffMessage />
  }

  let previousOldEnd = 1
  let previousNewEnd = 1

  return (
    <div
      data-testid="split-diff-body"
      style={{
        overflowX: 'auto'
      }}
    >
      <div style={{ minWidth: '720px' }}>
        {diff.diffHunks.map((hunk, hunkIdx) => {
          const unchanged = unchangedBeforeHunk(hunk.oldStart, hunk.newStart, previousOldEnd, previousNewEnd)
          previousOldEnd = hunk.oldStart + hunk.oldLines
          previousNewEnd = hunk.newStart + hunk.newLines
          return (
            <div key={hunkIdx}>
              {unchanged > 0 && <UnchangedDivider count={unchanged} />}
              {buildSplitRows(hunk.lines, hunk.oldStart, hunk.newStart).map((row, rowIdx) => (
                <div
                  key={rowIdx}
                  style={{
                    display: 'grid',
                    gridTemplateColumns: 'minmax(340px, 1fr) minmax(340px, 1fr)',
                    minWidth: '720px'
                  }}
                >
                  <SplitCell side="left" line={row.left} title={relativePath} />
                  <SplitCell side="right" line={row.right} title={relativePath} />
                </div>
              ))}
            </div>
          )
        })}
      </div>
    </div>
  )
}

function SplitCell({
  side,
  line,
  title
}: {
  side: 'left' | 'right'
  line: NumberedLine
  title?: string
}): JSX.Element {
  const isBlank = line.type === 'blank'
  return (
    <div
      style={{
        display: 'flex',
        minWidth: 0,
        background: isBlank ? 'var(--bg-primary)' : diffLineBackground(line.type),
        borderRight: side === 'left' ? '1px solid var(--border-default)' : undefined,
        whiteSpace: 'pre'
      }}
    >
      <span style={lineNumberStyle}>{line.num}</span>
      <span style={markerStyle(line.type)}>{line.type === 'add' ? '+' : line.type === 'remove' ? '-' : ' '}</span>
      <span
        title={title}
        style={{
          minWidth: 0,
          padding: '0 10px 0 4px',
          color: isBlank ? 'transparent' : line.type === 'remove' ? 'var(--text-secondary)' : 'var(--text-primary)'
        }}
      >
        {isBlank ? ' ' : line.content}
      </span>
    </div>
  )
}

function buildSplitRows(lines: DiffLine[], oldStart: number, newStart: number): SplitRow[] {
  const rows: SplitRow[] = []
  let oldLineNum = oldStart
  let newLineNum = newStart
  let index = 0

  while (index < lines.length) {
    const line = lines[index]
    if (!line) break

    if (line.type === 'context') {
      rows.push({
        left: { num: String(oldLineNum++), content: line.content, type: 'context' },
        right: { num: String(newLineNum++), content: line.content, type: 'context' }
      })
      index++
      continue
    }

    if (line.type === 'remove') {
      const removes: DiffLine[] = []
      while (lines[index]?.type === 'remove') {
        removes.push(lines[index]!)
        index++
      }
      const adds: DiffLine[] = []
      while (lines[index]?.type === 'add') {
        adds.push(lines[index]!)
        index++
      }
      const count = Math.max(removes.length, adds.length)
      for (let i = 0; i < count; i++) {
        const removed = removes[i]
        const added = adds[i]
        rows.push({
          left: removed
            ? { num: String(oldLineNum++), content: removed.content, type: 'remove' }
            : blankLine(),
          right: added
            ? { num: String(newLineNum++), content: added.content, type: 'add' }
            : blankLine()
        })
      }
      continue
    }

    rows.push({
      left: blankLine(),
      right: { num: String(newLineNum++), content: line.content, type: 'add' }
    })
    index++
  }

  return rows
}

function blankLine(): NumberedLine {
  return { num: '', content: '', type: 'blank' }
}

function EmptyDiffMessage(): JSX.Element {
  const t = useT()
  return (
    <div style={{ padding: '10px 12px', color: 'var(--text-dimmed)', fontSize: '12px' }}>
      {t('diffViewer.noChanges')}
    </div>
  )
}

function UnchangedDivider({ count }: { count: number }): JSX.Element {
  const t = useT()
  return (
    <div
      style={{
        padding: '4px 8px',
        background: 'var(--bg-secondary)',
        color: 'var(--text-dimmed)',
        fontSize: '11px',
        userSelect: 'none'
      }}
    >
      {t('diffViewer.unchangedLines', { count })}
    </div>
  )
}

function unchangedBeforeHunk(
  oldStart: number,
  newStart: number,
  previousOldEnd: number,
  previousNewEnd: number
): number {
  return Math.max(0, oldStart - previousOldEnd, newStart - previousNewEnd)
}

function diffLineBackground(type: NumberedLine['type']): string {
  if (type === 'add') return 'var(--diff-add-bg)'
  if (type === 'remove') return 'var(--diff-remove-bg)'
  return 'transparent'
}

function markerStyle(type: NumberedLine['type']): CSSProperties {
  return {
    width: '16px',
    flexShrink: 0,
    textAlign: 'center',
    color: type === 'add'
      ? 'var(--success)'
      : type === 'remove'
        ? 'var(--error)'
        : 'var(--text-dimmed)',
    userSelect: 'none'
  }
}

const lineNumberStyle: CSSProperties = {
  width: '40px',
  flexShrink: 0,
  textAlign: 'right',
  paddingRight: '6px',
  color: 'var(--text-dimmed)',
  userSelect: 'none',
  fontSize: '11px'
}

function toRelativePath(filePath: string, workspacePath: string): string {
  if (!workspacePath) return filePath
  const ws = workspacePath.replace(/\\/g, '/').replace(/\/$/, '')
  const fp = filePath.replace(/\\/g, '/')
  if (fp.startsWith(ws + '/')) return fp.slice(ws.length + 1)
  return filePath
}
