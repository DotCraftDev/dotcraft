/**
 * IPC handlers for the viewer panel (M1).
 *
 * Exposes three channels:
 *  - `workspace:viewer:list-files`   — list (or fuzzy-search) workspace files
 *  - `workspace:viewer:classify`     — classify a file into text / image / pdf / unsupported
 *  - `workspace:viewer:read-text`    — read a text file with optional size cap
 *
 * All handlers apply the workspace boundary check before serving any data.
 */
import { promises as fs } from 'fs'
import * as path from 'path'
import {
  ensureFileIndex,
  searchWorkspaceFiles,
  type FileMatchWire
} from './workspaceComposerIpc'
import type {
  ClassifyResult,
  ReadTextResult,
  ViewerContentClass
} from '../shared/viewer/types'

// ─── Defaults ────────────────────────────────────────────────────────────────

const DEFAULT_READ_LIMIT_BYTES = 5 * 1024 * 1024 // 5 MB

// ─── Extension → content class map ────────────────────────────────────────────

const IMAGE_EXTENSIONS = new Set([
  '.png', '.jpg', '.jpeg', '.gif', '.webp', '.svg', '.bmp', '.ico', '.tiff', '.tif', '.avif'
])

const PDF_EXTENSION = '.pdf'

/** Well-known text / source extensions (non-exhaustive; fallback uses magic byte check). */
const TEXT_EXTENSIONS = new Set([
  '.ts', '.tsx', '.js', '.jsx', '.mjs', '.cjs',
  '.json', '.jsonc', '.json5',
  '.md', '.mdx', '.txt', '.rst', '.adoc',
  '.css', '.scss', '.less', '.sass',
  '.html', '.htm', '.xml', '.xhtml', '.svg',
  '.yaml', '.yml', '.toml', '.ini', '.cfg', '.conf', '.env',
  '.py', '.pyi', '.pyx',
  '.rs', '.go', '.java', '.kt', '.kts',
  '.c', '.h', '.cpp', '.cc', '.cxx', '.hpp', '.hxx',
  '.cs', '.vb', '.fs', '.fsx',
  '.rb', '.php', '.swift', '.dart',
  '.sh', '.bash', '.zsh', '.fish', '.ps1', '.psm1', '.bat', '.cmd',
  '.sql', '.graphql', '.gql',
  '.proto', '.thrift',
  '.lua', '.r', '.jl',
  '.tf', '.hcl',
  '.dockerfile', '.gitignore', '.gitattributes',
  '.editorconfig', '.eslintrc', '.prettierrc', '.babelrc',
  '.lock', '.log'
])

// PDF magic bytes: %PDF
const PDF_MAGIC = Buffer.from([0x25, 0x50, 0x44, 0x46])

// PNG magic
const PNG_MAGIC = Buffer.from([0x89, 0x50, 0x4e, 0x47])

// JPEG magic
const JPEG_MAGIC = Buffer.from([0xff, 0xd8, 0xff])

// GIF magic
const GIF_MAGIC_87 = Buffer.from([0x47, 0x49, 0x46, 0x38, 0x37, 0x61])
const GIF_MAGIC_89 = Buffer.from([0x47, 0x49, 0x46, 0x38, 0x39, 0x61])

// WebP magic (starts with RIFF????WEBP)
const RIFF_MAGIC = Buffer.from([0x52, 0x49, 0x46, 0x46])

function startsWithBytes(buf: Buffer, magic: Buffer): boolean {
  if (buf.length < magic.length) return false
  return magic.equals(buf.subarray(0, magic.length))
}

function isWebp(buf: Buffer): boolean {
  return buf.length >= 12 &&
    startsWithBytes(buf, RIFF_MAGIC) &&
    buf.subarray(8, 12).toString('ascii') === 'WEBP'
}

/** Sniff magic bytes to detect image or PDF content type. */
function sniffMagicClass(header: Buffer): ViewerContentClass | null {
  if (startsWithBytes(header, PDF_MAGIC)) return 'pdf'
  if (startsWithBytes(header, PNG_MAGIC)) return 'image'
  if (startsWithBytes(header, JPEG_MAGIC)) return 'image'
  if (startsWithBytes(header, GIF_MAGIC_87) || startsWithBytes(header, GIF_MAGIC_89)) return 'image'
  if (isWebp(header)) return 'image'
  return null
}

/** Extension → MIME type hint. */
function extToMime(ext: string): string {
  const map: Record<string, string> = {
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg',
    '.gif': 'image/gif',
    '.webp': 'image/webp',
    '.svg': 'image/svg+xml',
    '.bmp': 'image/bmp',
    '.ico': 'image/x-icon',
    '.pdf': 'application/pdf',
    '.txt': 'text/plain',
    '.html': 'text/html',
    '.css': 'text/css',
    '.js': 'text/javascript',
    '.json': 'application/json'
  }
  return map[ext] ?? 'application/octet-stream'
}

// ─── Workspace boundary ────────────────────────────────────────────────────────

/**
 * Returns true if `targetPath` is inside `workspaceRoot` after resolving both
 * via `fs.realpath` (handles symlinks and `..` traversal).
 */
export async function isPathInsideWorkspace(targetPath: string, workspaceRoot: string): Promise<boolean> {
  if (!workspaceRoot) return false
  try {
    const resolvedRoot = await fs.realpath(path.resolve(workspaceRoot))
    const resolvedTarget = await fs.realpath(path.resolve(targetPath))
    const sep = path.sep
    return (
      resolvedTarget === resolvedRoot ||
      resolvedTarget.startsWith(resolvedRoot + sep)
    )
  } catch {
    return false
  }
}

// ─── classify ────────────────────────────────────────────────────────────────

/**
 * Classifies a file into text / image / pdf / unsupported.
 * Extension is checked first; falls back to magic-byte sniffing for ambiguous cases.
 */
export async function classifyFile(
  absolutePath: string,
  workspaceRoot: string
): Promise<ClassifyResult> {
  if (!(await isPathInsideWorkspace(absolutePath, workspaceRoot))) {
    throw new Error(`Access denied: path is outside workspace: ${absolutePath}`)
  }

  const stat = await fs.stat(absolutePath)
  if (!stat.isFile()) {
    throw new Error(`Not a file: ${absolutePath}`)
  }

  const ext = path.extname(absolutePath).toLowerCase()
  const sizeBytes = stat.size

  if (ext === PDF_EXTENSION) {
    return { contentClass: 'pdf', mime: 'application/pdf', sizeBytes }
  }

  if (IMAGE_EXTENSIONS.has(ext)) {
    return { contentClass: 'image', mime: extToMime(ext), sizeBytes }
  }

  if (TEXT_EXTENSIONS.has(ext)) {
    return { contentClass: 'text', mime: extToMime(ext), sizeBytes }
  }

  // No recognized extension — try magic bytes (read first 16 bytes)
  try {
    const fh = await fs.open(absolutePath, 'r')
    const header = Buffer.alloc(16)
    const { bytesRead } = await fh.read(header, 0, 16, 0)
    await fh.close()
    const buf = header.subarray(0, bytesRead)
    const sniffed = sniffMagicClass(buf)
    if (sniffed) {
      return { contentClass: sniffed, mime: extToMime(ext) || 'application/octet-stream', sizeBytes }
    }

    // Check if content looks like text (all bytes in printable range)
    let likelyText = true
    for (let i = 0; i < buf.length; i++) {
      const b = buf[i]!
      if (b < 9 || (b > 13 && b < 32 && b !== 27)) {
        likelyText = false
        break
      }
    }
    if (likelyText) {
      return { contentClass: 'text', mime: 'text/plain', sizeBytes }
    }
  } catch {
    // If we can't read the header, fall through to unsupported
  }

  return { contentClass: 'unsupported', mime: extToMime(ext) || 'application/octet-stream', sizeBytes }
}

// ─── read-text ───────────────────────────────────────────────────────────────

/**
 * Reads a text file and returns its content as a UTF-8 string.
 * If the file exceeds `limitBytes`, only the first `limitBytes` are returned
 * and `truncated` is set to true.
 */
export async function readTextFile(
  absolutePath: string,
  workspaceRoot: string,
  limitBytes: number = DEFAULT_READ_LIMIT_BYTES
): Promise<ReadTextResult> {
  if (!(await isPathInsideWorkspace(absolutePath, workspaceRoot))) {
    throw new Error(`Access denied: path is outside workspace: ${absolutePath}`)
  }

  const stat = await fs.stat(absolutePath)
  if (!stat.isFile()) {
    throw new Error(`Not a file: ${absolutePath}`)
  }

  const fileSize = stat.size
  const truncated = fileSize > limitBytes

  let buffer: Buffer
  if (truncated) {
    const fh = await fs.open(absolutePath, 'r')
    try {
      buffer = Buffer.alloc(limitBytes)
      const { bytesRead } = await fh.read(buffer, 0, limitBytes, 0)
      buffer = buffer.subarray(0, bytesRead)
    } finally {
      await fh.close()
    }
  } else {
    buffer = await fs.readFile(absolutePath)
  }

  // Decode as UTF-8 with replacement for invalid bytes
  const decoder = new TextDecoder('utf-8', { fatal: false })
  const text = decoder.decode(buffer)

  return { text, truncated, encoding: 'utf-8' }
}

// ─── list-files (re-export for IPC) ──────────────────────────────────────────

/**
 * Lists workspace files for the Quick-Open dialog.
 * Loads the full index if query is empty (returns first `limit` entries sorted
 * by path). If query is non-empty, delegates to the existing `searchWorkspaceFiles`.
 */
export async function listViewerFiles(
  workspaceRoot: string,
  query: string,
  limit: number
): Promise<FileMatchWire[]> {
  if (!workspaceRoot) return []

  const index = await ensureFileIndex(workspaceRoot)

  if (!query.trim()) {
    // Return first `limit` entries sorted by relativePath
    return [...index]
      .sort((a, b) => a.relativePath.localeCompare(b.relativePath))
      .slice(0, limit)
      .map((e) => ({ name: e.name, relativePath: e.relativePath, dir: e.dir }))
  }

  // Delegate to the existing search (it handles query scoring)
  return searchWorkspaceFiles(workspaceRoot, query, limit)
}
