/**
 * IPC helpers for the conversation composer: temp image save and workspace file search.
 */
import { randomUUID } from 'crypto'
import { promises as fs } from 'fs'
import * as path from 'path'
import { translate, DEFAULT_LOCALE, type AppLocale } from '../shared/locales'
import { watch as fsWatch, type FSWatcher } from 'fs'
import { globby } from 'globby'

const MAX_IMAGE_BYTES = 20 * 1024 * 1024

const MIME_TO_EXT: Record<string, string> = {
  'image/png': '.png',
  'image/jpeg': '.jpg',
  'image/jpg': '.jpg',
  'image/gif': '.gif',
  'image/webp': '.webp',
  'image/bmp': '.bmp'
}

/** Path segments we always exclude (even if not in .gitignore), e.g. `.craft` temp. */
const FORCE_EXCLUDED_PATH_SEGMENTS = new Set([
  '.git',
  'node_modules',
  '.craft',
  'bin',
  'obj',
  'dist',
  'out',
  'build',
  '.next',
  '__pycache__'
])

/** fast-glob ignore patterns (merged with gitignore) to prune traversal early. */
const GLOB_FORCE_IGNORE: string[] = Array.from(FORCE_EXCLUDED_PATH_SEGMENTS).map(
  (seg) => `**/${seg}/**`
)

/** Debounce invalidating the in-memory index after fs.watch events (saves full globby rescans). */
const INDEX_INVALIDATE_DEBOUNCE_MS = 1200

export interface FileMatchWire {
  name: string
  relativePath: string
  dir: string
}

interface FileIndexEntry {
  relativePath: string
  name: string
  dir: string
}

let fileIndex: FileIndexEntry[] | null = null
let fileIndexWorkspace: string | null = null
let fileIndexWatcher: FSWatcher | null = null
let indexInvalidateDebounce: ReturnType<typeof setTimeout> | null = null

/** Bumped on explicit invalidate or debounced watch invalidation so in-flight builds do not commit stale data. */
let fileIndexEpoch = 0

let indexBuildPending: Promise<FileIndexEntry[]> | null = null
let indexBuildPendingRoot: string | null = null

function shouldSkipFile(relPath: string): boolean {
  const base = path.basename(relPath)
  if (base.endsWith('.pyc')) return true
  if (base.endsWith('.min.js')) return true
  return false
}

function shouldForceExclude(relPath: string): boolean {
  for (const segment of relPath.split('/')) {
    if (segment && FORCE_EXCLUDED_PATH_SEGMENTS.has(segment)) return true
  }
  return false
}

async function buildFileIndex(workspaceRoot: string): Promise<FileIndexEntry[]> {
  const root = path.resolve(workspaceRoot)
  const paths = await globby('**/*', {
    cwd: root,
    onlyFiles: true,
    gitignore: true,
    dot: true,
    ignore: GLOB_FORCE_IGNORE
  })
  const out: FileIndexEntry[] = []
  for (const relRaw of paths) {
    const rel = relRaw.replace(/\\/g, '/')
    if (shouldForceExclude(rel)) continue
    if (shouldSkipFile(rel)) continue
    const name = path.basename(rel)
    const dir = path.dirname(rel) === '.' ? '' : path.dirname(rel).replace(/\\/g, '/')
    out.push({ relativePath: rel, name, dir })
  }
  return out
}

function scheduleDebouncedIndexInvalidate(): void {
  if (indexInvalidateDebounce) {
    clearTimeout(indexInvalidateDebounce)
  }
  indexInvalidateDebounce = setTimeout(() => {
    indexInvalidateDebounce = null
    fileIndexEpoch++
    fileIndex = null
    fileIndexWorkspace = null
  }, INDEX_INVALIDATE_DEBOUNCE_MS)
}

function ensureFsWatchForWorkspace(resolvedRoot: string): void {
  if (fileIndexWatcher) {
    return
  }
  try {
    fileIndexWatcher = fsWatch(resolvedRoot, { recursive: true }, () => {
      scheduleDebouncedIndexInvalidate()
    })
  } catch {
    /* recursive watch unsupported or failed — index refreshes on next cold miss */
  }
}

async function ensureFileIndex(workspaceRoot: string): Promise<FileIndexEntry[]> {
  const resolved = path.resolve(workspaceRoot)
  if (fileIndex && fileIndexWorkspace === resolved) {
    return fileIndex
  }
  if (indexBuildPending && indexBuildPendingRoot === resolved) {
    return indexBuildPending
  }
  const snapshotEpoch = fileIndexEpoch
  const p = (async (): Promise<FileIndexEntry[]> => {
    try {
      const built = await buildFileIndex(resolved)
      if (snapshotEpoch !== fileIndexEpoch) {
        if (fileIndex && fileIndexWorkspace === resolved) {
          return fileIndex
        }
        return ensureFileIndex(workspaceRoot)
      }
      fileIndex = built
      fileIndexWorkspace = resolved
      ensureFsWatchForWorkspace(resolved)
      return fileIndex
    } finally {
      indexBuildPending = null
      indexBuildPendingRoot = null
    }
  })()
  indexBuildPending = p
  indexBuildPendingRoot = resolved
  return p
}

/**
 * Starts a background index build so the first @ search is less likely to block.
 */
export function warmFileSearchIndex(workspaceRoot: string): void {
  if (!workspaceRoot.trim()) return
  void ensureFileIndex(workspaceRoot).catch(() => {
    /* ignore — next search will retry */
  })
}

function scoreMatch(name: string, qLower: string): number {
  const nLower = name.toLowerCase()
  if (nLower === qLower) return 0
  if (nLower.startsWith(qLower)) return 1
  const idx = nLower.indexOf(qLower)
  if (idx >= 0) return 2 + idx
  return 1000
}

export async function searchWorkspaceFiles(
  workspaceRoot: string,
  query: string,
  limit: number
): Promise<FileMatchWire[]> {
  const q = query.trim()
  if (!q) {
    return []
  }
  const index = await ensureFileIndex(workspaceRoot)
  const qLower = q.toLowerCase()
  const scored = index
    .map((e) => ({
      e,
      score: scoreMatch(e.name, qLower)
    }))
    .filter((x) => x.score < 1000)
    .sort((a, b) => {
      if (a.score !== b.score) return a.score - b.score
      return a.e.relativePath.localeCompare(b.e.relativePath)
    })
    .slice(0, limit)
    .map((x) => ({
      name: x.e.name,
      relativePath: x.e.relativePath,
      dir: x.e.dir
    }))
  return scored
}

export function invalidateFileIndex(): void {
  if (indexInvalidateDebounce) {
    clearTimeout(indexInvalidateDebounce)
    indexInvalidateDebounce = null
  }
  fileIndexEpoch++
  fileIndex = null
  fileIndexWorkspace = null
  indexBuildPending = null
  indexBuildPendingRoot = null
  if (fileIndexWatcher) {
    fileIndexWatcher.close()
    fileIndexWatcher = null
  }
}

/**
 * Writes a data URL to `.craft/tmp/images/<uuid>.<ext>` under the workspace.
 * Returns absolute path on disk.
 */
export async function saveImageDataUrlToTemp(
  workspaceRoot: string,
  dataUrl: string,
  suggestedFileName?: string,
  locale: AppLocale = DEFAULT_LOCALE
): Promise<string> {
  const resolved = path.resolve(workspaceRoot)
  const match = /^data:([^;]+);base64,(.+)$/s.exec(dataUrl.trim())
  if (!match) {
    throw new Error(translate(locale, 'ipc.invalidImageDataUrl'))
  }
  const mime = match[1].trim().toLowerCase()
  const b64 = match[2].replace(/\s/g, '')
  const buf = Buffer.from(b64, 'base64')
  if (buf.length > MAX_IMAGE_BYTES) {
    throw new Error(
      translate(locale, 'ipc.imageTooLarge', { bytes: buf.length, max: MAX_IMAGE_BYTES })
    )
  }
  if (!mime.startsWith('image/')) {
    throw new Error(translate(locale, 'ipc.clipboardNotImage'))
  }
  const ext = MIME_TO_EXT[mime] ?? (path.extname(suggestedFileName ?? '') || '.png')
  const dir = path.join(resolved, '.craft', 'tmp', 'images')
  await fs.mkdir(dir, { recursive: true })
  const fileName = `${randomUUID()}${ext.startsWith('.') ? ext : `.${ext}`}`
  const absPath = path.join(dir, fileName)
  await fs.writeFile(absPath, buf)
  return absPath
}
