/**
 * M4 tool call types: diffs, file changes, and SubAgent progress.
 */

export interface DiffLine {
  type: 'context' | 'add' | 'remove'
  content: string
}

export interface DiffHunk {
  oldStart: number
  oldLines: number
  newStart: number
  newLines: number
  lines: DiffLine[]
}

export interface FileDiff {
  filePath: string
  turnId: string
  additions: number
  deletions: number
  diffHunks: DiffHunk[]
  status: 'written' | 'reverted'
  isNewFile: boolean
}

export interface SubAgentEntry {
  label: string
  currentTool: string | null
  inputTokens: number
  outputTokens: number
  isCompleted: boolean
}
