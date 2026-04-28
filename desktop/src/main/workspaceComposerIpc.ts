/**
 * IPC helpers for the conversation composer: temp image save and workspace file search.
 */
import { randomUUID } from 'crypto'
import { Worker } from 'worker_threads'
import { promises as fs } from 'fs'
import * as path from 'path'
import { translate, DEFAULT_LOCALE, type AppLocale } from '../shared/locales'
import { watch as fsWatch, type FSWatcher } from 'fs'

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
const FILE_INDEX_CACHE_SCHEMA_VERSION = 1
const FILE_INDEX_IGNORE_CONFIG_VERSION = 'force-exclude-v1'
const FILE_INDEX_CACHE_RELATIVE_PATH = path.join('.craft', 'cache', 'desktop-file-index-v1.json')

const FILE_INDEX_WORKER_SOURCE = String.raw`
const { parentPort, workerData } = require('worker_threads')
const path = require('path')
const fs = require('fs/promises')

function shouldForceExclude(relPath, excludedSegments) {
  for (const segment of relPath.split('/')) {
    if (segment && excludedSegments.includes(segment)) return true
  }
  return false
}

function shouldSkipFile(relPath) {
  const base = path.basename(relPath)
  return base.endsWith('.pyc') || base.endsWith('.min.js')
}

(async () => {
  const root = path.resolve(workerData.workspaceRoot)
  const { globby } = await import('globby')
  const paths = await globby('**/*', {
    cwd: root,
    onlyFiles: true,
    gitignore: true,
    dot: true,
    ignore: workerData.ignorePatterns
  })
  const entries = []
  for (const relRaw of paths) {
    const rel = relRaw.replace(/\\/g, '/')
    if (shouldForceExclude(rel, workerData.excludedSegments)) continue
    if (shouldSkipFile(rel)) continue
    const name = path.basename(rel)
    const dir = path.dirname(rel) === '.' ? '' : path.dirname(rel).replace(/\\/g, '/')
    entries.push({ relativePath: rel, name, dir })
  }
  const cache = {
    schemaVersion: workerData.schemaVersion,
    workspaceRoot: root,
    generatedAt: new Date().toISOString(),
    ignoreConfigVersion: workerData.ignoreConfigVersion,
    entries
  }
  await fs.mkdir(path.dirname(workerData.cachePath), { recursive: true })
  await fs.writeFile(workerData.cachePath, JSON.stringify(cache), 'utf8')
  parentPort.postMessage({ type: 'success', entries, pathCount: paths.length })
})().catch((error) => {
  parentPort.postMessage({
    type: 'error',
    error: error && error.message ? error.message : String(error)
  })
})
`

export interface FileMatchWire {
  name: string
  relativePath: string
  dir: string
}

export interface FileIndexEntry {
  relativePath: string
  name: string
  dir: string
}

export type FileIndexStatus = 'empty' | 'building' | 'ready'

export interface FileListResultWire {
  files: FileMatchWire[]
  indexStatus: FileIndexStatus
  indexedCount: number
  stale: boolean
}

interface FileIndexCacheWire {
  schemaVersion: number
  workspaceRoot: string
  generatedAt: string
  ignoreConfigVersion: string
  entries: FileIndexEntry[]
}

interface WorkerSuccessMessage {
  type: 'success'
  entries: FileIndexEntry[]
  pathCount: number
}

interface WorkerErrorMessage {
  type: 'error'
  error: string
}

let fileIndex: FileIndexEntry[] | null = null
let fileIndexWorkspace: string | null = null
let activeIndexWorkspace: string | null = null
let fileIndexWatcher: FSWatcher | null = null
let indexInvalidateDebounce: ReturnType<typeof setTimeout> | null = null
let fileIndexStale = false

/** Bumped on explicit invalidate or debounced watch invalidation so in-flight builds do not commit stale data. */
let fileIndexEpoch = 0

let indexBuildPending: Promise<FileIndexEntry[]> | null = null
let indexBuildPendingRoot: string | null = null
let indexCacheLoadPending: Promise<FileIndexEntry[] | null> | null = null
let indexCacheLoadPendingRoot: string | null = null
let buildWorker: Worker | null = null

function scheduleDebouncedIndexInvalidate(): void {
  if (indexInvalidateDebounce) {
    clearTimeout(indexInvalidateDebounce)
  }
  indexInvalidateDebounce = setTimeout(() => {
    indexInvalidateDebounce = null
    fileIndexEpoch++
    fileIndexStale = true
    if (activeIndexWorkspace) {
      void startBackgroundIndexBuild(activeIndexWorkspace, 'fs-watch-stale').catch(() => {})
    }
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

function cachePathForWorkspace(resolvedRoot: string): string {
  return path.join(resolvedRoot, FILE_INDEX_CACHE_RELATIVE_PATH)
}

function isValidCacheEntry(value: unknown): value is FileIndexEntry {
  if (value == null || typeof value !== 'object' || Array.isArray(value)) return false
  const candidate = value as Record<string, unknown>
  return (
    typeof candidate.relativePath === 'string' &&
    typeof candidate.name === 'string' &&
    typeof candidate.dir === 'string'
  )
}

function parseFileIndexCache(raw: string, resolvedRoot: string): FileIndexEntry[] | null {
  const parsed = JSON.parse(raw) as Partial<FileIndexCacheWire>
  if (parsed.schemaVersion !== FILE_INDEX_CACHE_SCHEMA_VERSION) return null
  if (parsed.ignoreConfigVersion !== FILE_INDEX_IGNORE_CONFIG_VERSION) return null
  if (path.resolve(parsed.workspaceRoot ?? '') !== resolvedRoot) return null
  if (!Array.isArray(parsed.entries)) return null
  if (!parsed.entries.every(isValidCacheEntry)) return null
  return parsed.entries
}

async function loadCacheForWorkspace(resolvedRoot: string): Promise<FileIndexEntry[] | null> {
  if (fileIndex && fileIndexWorkspace === resolvedRoot) return fileIndex
  if (indexCacheLoadPending && indexCacheLoadPendingRoot === resolvedRoot) {
    return indexCacheLoadPending
  }
  const p = (async () => {
    try {
      const raw = await fs.readFile(cachePathForWorkspace(resolvedRoot), 'utf8')
      const entries = parseFileIndexCache(raw, resolvedRoot)
      if (!entries) {
        return null
      }
      if (activeIndexWorkspace === resolvedRoot) {
        fileIndex = entries
        fileIndexWorkspace = resolvedRoot
        fileIndexStale = true
        ensureFsWatchForWorkspace(resolvedRoot)
      }
      return entries
    } catch {
      return null
    }
  })().finally(() => {
    indexCacheLoadPending = null
    indexCacheLoadPendingRoot = null
  })
  indexCacheLoadPending = p
  indexCacheLoadPendingRoot = resolvedRoot
  return p
}

function startBackgroundIndexBuild(resolvedRoot: string, _reason: string): Promise<FileIndexEntry[]> {
  if (indexBuildPending && indexBuildPendingRoot === resolvedRoot) {
    return indexBuildPending
  }
  if (buildWorker) {
    buildWorker.terminate().catch(() => {})
    buildWorker = null
  }

  const snapshotEpoch = fileIndexEpoch
  const worker = new Worker(FILE_INDEX_WORKER_SOURCE, {
    eval: true,
    workerData: {
      workspaceRoot: resolvedRoot,
      cachePath: cachePathForWorkspace(resolvedRoot),
      schemaVersion: FILE_INDEX_CACHE_SCHEMA_VERSION,
      ignoreConfigVersion: FILE_INDEX_IGNORE_CONFIG_VERSION,
      excludedSegments: Array.from(FORCE_EXCLUDED_PATH_SEGMENTS),
      ignorePatterns: GLOB_FORCE_IGNORE
    }
  })
  buildWorker = worker

  const p = new Promise<FileIndexEntry[]>((resolve, reject) => {
    worker.once('message', (message: WorkerSuccessMessage | WorkerErrorMessage) => {
      if (message.type === 'error') {
        reject(new Error(message.error))
        return
      }
      const entries = message.entries
      if (activeIndexWorkspace === resolvedRoot) {
        fileIndex = entries
        fileIndexWorkspace = resolvedRoot
        fileIndexStale = snapshotEpoch !== fileIndexEpoch
        ensureFsWatchForWorkspace(resolvedRoot)
      }
      resolve(entries)
    })
    worker.once('error', (error) => {
      reject(error)
    })
    worker.once('exit', (code) => {
      if (buildWorker === worker) buildWorker = null
      if (indexBuildPending === p) {
        indexBuildPending = null
        indexBuildPendingRoot = null
      }
    })
  }).finally(() => {
    if (buildWorker === worker) buildWorker = null
    if (indexBuildPending === p) {
      indexBuildPending = null
      indexBuildPendingRoot = null
    }
  })
  indexBuildPending = p
  indexBuildPendingRoot = resolvedRoot
  return p
}

export function activateFileIndexWorkspace(workspaceRoot: string): void {
  const trimmed = workspaceRoot.trim()
  if (!trimmed) {
    invalidateFileIndex()
    activeIndexWorkspace = null
    return
  }
  const resolved = path.resolve(trimmed)
  if (activeIndexWorkspace === resolved) return
  invalidateFileIndex()
  activeIndexWorkspace = resolved
}

async function getAvailableIndex(
  workspaceRoot: string,
  reason: string
): Promise<{ entries: FileIndexEntry[]; status: FileIndexStatus; stale: boolean }> {
  const resolved = path.resolve(workspaceRoot)
  activateFileIndexWorkspace(resolved)
  if (fileIndex && fileIndexWorkspace === resolved) {
    if (fileIndexStale) {
      void startBackgroundIndexBuild(resolved, `${reason}-stale-revalidate`).catch(() => {})
      return { entries: fileIndex, status: 'building', stale: true }
    }
    return { entries: fileIndex, status: 'ready', stale: false }
  }

  const cached = await loadCacheForWorkspace(resolved)
  if (cached) {
    void startBackgroundIndexBuild(resolved, `${reason}-cache-revalidate`).catch(() => {})
    return { entries: cached, status: 'building', stale: true }
  }

  void startBackgroundIndexBuild(resolved, reason).catch(() => {})
  return { entries: [], status: 'building', stale: true }
}

export async function ensureFileIndex(workspaceRoot: string): Promise<FileIndexEntry[]> {
  const resolved = path.resolve(workspaceRoot)
  const available = await getAvailableIndex(resolved, 'ensure')
  if (available.entries.length > 0) return available.entries
  if (indexBuildPending && indexBuildPendingRoot === resolved) {
    return indexBuildPending
  }
  return startBackgroundIndexBuild(resolved, 'ensure-wait')
}

/**
 * Starts a background index build so the first @ search is less likely to block.
 */
export function warmFileSearchIndex(workspaceRoot: string): void {
  if (!workspaceRoot.trim()) return
  void getAvailableIndex(workspaceRoot, 'warm').catch(() => {
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
  const { entries: index } = await getAvailableIndex(workspaceRoot, 'search')
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

export async function listWorkspaceFiles(
  workspaceRoot: string,
  query: string,
  limit: number
): Promise<FileListResultWire> {
  if (!workspaceRoot.trim()) {
    return { files: [], indexStatus: 'empty', indexedCount: 0, stale: false }
  }
  const { entries, status, stale } = await getAvailableIndex(workspaceRoot, 'list')
  const q = query.trim()
  const files = q
    ? await searchWorkspaceFiles(workspaceRoot, q, limit)
    : [...entries]
        .sort((a, b) => a.relativePath.localeCompare(b.relativePath))
        .slice(0, limit)
        .map((e) => ({ name: e.name, relativePath: e.relativePath, dir: e.dir }))
  return {
    files,
    indexStatus: entries.length > 0 ? status : 'building',
    indexedCount: entries.length,
    stale
  }
}

export function invalidateFileIndex(): void {
  if (indexInvalidateDebounce) {
    clearTimeout(indexInvalidateDebounce)
    indexInvalidateDebounce = null
  }
  fileIndexEpoch++
  fileIndex = null
  fileIndexWorkspace = null
  fileIndexStale = false
  indexBuildPending = null
  indexBuildPendingRoot = null
  indexCacheLoadPending = null
  indexCacheLoadPendingRoot = null
  if (buildWorker) {
    buildWorker.terminate().catch(() => {})
    buildWorker = null
  }
  if (fileIndexWatcher) {
    fileIndexWatcher.close()
    fileIndexWatcher = null
  }
}

function isPathWithin(parent: string, target: string): boolean {
  const resolvedParent = path.resolve(parent)
  const resolvedTarget = path.resolve(target)
  return resolvedTarget === resolvedParent || resolvedTarget.startsWith(`${resolvedParent}${path.sep}`)
}

function inferMimeTypeFromPath(absPath: string): string {
  const ext = path.extname(absPath).toLowerCase()
  return (
    Object.entries(MIME_TO_EXT).find(([, mappedExt]) => mappedExt === ext)?.[0]
    ?? 'image/png'
  )
}

/**
 * Writes a data URL to `.craft/attachments/images/<uuid>.<ext>` under the workspace.
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
  const dir = path.join(resolved, '.craft', 'attachments', 'images')
  await fs.mkdir(dir, { recursive: true })
  const fileName = `${randomUUID()}${ext.startsWith('.') ? ext : `.${ext}`}`
  const absPath = path.join(dir, fileName)
  await fs.writeFile(absPath, buf)
  return absPath
}

/**
 * Reads an image file under workspace attachment folders and returns a data URL.
 * Supports the current `.craft/attachments/images` and legacy `.craft/tmp/images`.
 */
export async function readImageAsDataUrl(
  workspaceRoot: string,
  absPath: string,
  locale: AppLocale = DEFAULT_LOCALE
): Promise<string> {
  const resolvedRoot = path.resolve(workspaceRoot)
  const resolvedPath = path.resolve(absPath)
  const attachmentsDir = path.join(resolvedRoot, '.craft', 'attachments', 'images')
  const legacyTmpDir = path.join(resolvedRoot, '.craft', 'tmp', 'images')
  const allowed =
    isPathWithin(attachmentsDir, resolvedPath) ||
    isPathWithin(legacyTmpDir, resolvedPath)
  if (!allowed) {
    throw new Error(
      translate(locale, 'ipc.pathOutsideWorkspace', { path: absPath })
    )
  }
  const buf = await fs.readFile(resolvedPath)
  if (buf.length > MAX_IMAGE_BYTES) {
    throw new Error(
      translate(locale, 'ipc.imageTooLarge', { bytes: buf.length, max: MAX_IMAGE_BYTES })
    )
  }
  const mimeType = inferMimeTypeFromPath(resolvedPath)
  if (!mimeType.startsWith('image/')) {
    throw new Error(translate(locale, 'ipc.clipboardNotImage'))
  }
  return `data:${mimeType};base64,${buf.toString('base64')}`
}
