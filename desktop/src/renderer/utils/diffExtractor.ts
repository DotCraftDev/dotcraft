import { diffLines } from 'diff'
import type { FileDiff, DiffHunk, DiffLine } from '../types/toolCall'

/**
 * Extract the file path from tool result status strings like:
 *   "Successfully wrote 1234 bytes (42 lines) to src/foo.ts"
 *   "Successfully edited src/foo.ts"
 */
export function parseResultPath(resultText: string): string | null {
  // "... to <path>" pattern (WriteFile)
  const toMatch = resultText.match(/\bto\s+(\S+)\s*$/)
  if (toMatch) return toMatch[1]
  // "Successfully edited <path>" pattern (EditFile)
  const editMatch = resultText.match(/Successfully edited\s+(\S+)/)
  if (editMatch) return editMatch[1]
  return null
}

/**
 * Convert jsdiff change objects into our DiffHunk[] format.
 */
export function computeDiffHunks(oldText: string, newText: string): { hunks: DiffHunk[]; additions: number; deletions: number } {
  const changes = diffLines(oldText, newText)
  const lines: DiffLine[] = []

  for (const change of changes) {
    const changeLines = change.value.split('\n')
    // diffLines may include a trailing empty string from the final newline
    if (changeLines[changeLines.length - 1] === '') {
      changeLines.pop()
    }
    const type: DiffLine['type'] = change.added ? 'add' : change.removed ? 'remove' : 'context'
    for (const line of changeLines) {
      lines.push({ type, content: line })
    }
  }

  // Group consecutive lines into hunks (context windows of 3 lines around changes)
  const CONTEXT = 3
  const hunks: DiffHunk[] = []
  let additions = 0
  let deletions = 0

  // Count additions/deletions first
  for (const line of lines) {
    if (line.type === 'add') additions++
    else if (line.type === 'remove') deletions++
  }

  // Build hunks: collect changed line indices and expand with context
  const changedIndices = new Set<number>()
  for (let i = 0; i < lines.length; i++) {
    if (lines[i].type !== 'context') changedIndices.add(i)
  }

  if (changedIndices.size === 0) return { hunks: [], additions: 0, deletions: 0 }

  // Expand ranges with context
  const ranges: Array<[number, number]> = []
  let rangeStart = -1
  let rangeEnd = -1

  const sortedChanged = Array.from(changedIndices).sort((a, b) => a - b)
  for (const idx of sortedChanged) {
    const start = Math.max(0, idx - CONTEXT)
    const end = Math.min(lines.length - 1, idx + CONTEXT)
    if (rangeStart === -1) {
      rangeStart = start
      rangeEnd = end
    } else if (start <= rangeEnd + 1) {
      rangeEnd = Math.max(rangeEnd, end)
    } else {
      ranges.push([rangeStart, rangeEnd])
      rangeStart = start
      rangeEnd = end
    }
  }
  if (rangeStart !== -1) ranges.push([rangeStart, rangeEnd])

  // Build hunk objects with line numbers
  let oldLine = 1
  let newLine = 1
  let lineIdx = 0

  for (const [start, end] of ranges) {
    // Advance counters to the hunk start
    while (lineIdx < start) {
      const l = lines[lineIdx]
      if (l.type !== 'add') oldLine++
      if (l.type !== 'remove') newLine++
      lineIdx++
    }

    const hunkLines = lines.slice(start, end + 1)
    const hunkOldStart = oldLine
    const hunkNewStart = newLine
    let hunkOldLines = 0
    let hunkNewLines = 0

    for (const l of hunkLines) {
      if (l.type !== 'add') hunkOldLines++
      if (l.type !== 'remove') hunkNewLines++
    }

    hunks.push({
      oldStart: hunkOldStart,
      oldLines: hunkOldLines,
      newStart: hunkNewStart,
      newLines: hunkNewLines,
      lines: hunkLines
    })

    // Advance counters through the hunk
    for (const l of hunkLines) {
      if (l.type !== 'add') oldLine++
      if (l.type !== 'remove') newLine++
      lineIdx++
    }
  }

  return { hunks, additions, deletions }
}

/**
 * Extract a FileDiff from a WriteFile tool call.
 * For new files, the entire content is treated as additions.
 */
export function extractDiffFromWriteFile(
  args: Record<string, unknown>,
  resultText: string,
  turnId: string
): FileDiff | null {
  const filePath = (args.path as string | undefined) ?? parseResultPath(resultText)
  if (!filePath) return null

  const content = (args.content as string | undefined) ?? ''
  const contentLines = content.split('\n')
  if (contentLines[contentLines.length - 1] === '') contentLines.pop()

  const hunkLines: DiffLine[] = contentLines.map((line) => ({ type: 'add' as const, content: line }))

  const hunk: DiffHunk = {
    oldStart: 0,
    oldLines: 0,
    newStart: 1,
    newLines: contentLines.length,
    lines: hunkLines
  }

  return {
    filePath,
    turnId,
    additions: contentLines.length,
    deletions: 0,
    diffHunks: hunkLines.length > 0 ? [hunk] : [],
    status: 'written',
    isNewFile: true
  }
}

/**
 * Extract a FileDiff from an EditFile tool call.
 * Supports oldText/newText (search-replace) mode and startLine/endLine/newText (line-range) mode.
 */
export function extractDiffFromEditFile(
  args: Record<string, unknown>,
  resultText: string,
  turnId: string
): FileDiff | null {
  const filePath = (args.path as string | undefined) ?? parseResultPath(resultText)
  if (!filePath) return null

  const oldText = (args.oldText as string | undefined) ?? ''
  const newText = (args.newText as string | undefined) ?? ''

  if (!oldText && !newText) return null

  const { hunks, additions, deletions } = computeDiffHunks(oldText, newText)

  return {
    filePath,
    turnId,
    additions,
    deletions,
    diffHunks: hunks,
    status: 'written',
    isNewFile: false
  }
}
