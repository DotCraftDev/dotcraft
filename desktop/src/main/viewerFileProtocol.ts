/**
 * Registers the `dotcraft-viewer://` custom Electron protocol for safe,
 * workspace-scoped file serving to the viewer panel.
 *
 * Security contract:
 *  - Only files whose realpath is inside the current workspace root are served.
 *  - Symbolic links and `..` traversal that escape the workspace root are rejected (403).
 *  - When the workspace root is cleared (workspace switch / no workspace), all
 *    requests return 403 until a new valid root is set.
 *
 * Usage:
 *  1. Call `registerViewerScheme()` synchronously before `app.whenReady()` to
 *     register the privileged scheme.
 *  2. Call `installViewerProtocolHandler()` inside `app.whenReady()`.
 *  3. Call `setViewerWorkspaceRoot(path)` whenever the workspace changes.
 *  4. Call `setViewerWorkspaceRoot('')` on workspace cleared / app quit.
 *
 * URL format: `dotcraft-viewer:///<encodeURIComponent(absolutePath)>`
 */
import { protocol, net } from 'electron'
import { promises as fs } from 'fs'
import * as path from 'path'
import { pathToFileURL } from 'url'

export const VIEWER_SCHEME = 'dotcraft-viewer'

let currentWorkspaceRoot = ''

/**
 * Must be called before `app.whenReady()` to mark the scheme as privileged.
 */
export function registerViewerScheme(): void {
  protocol.registerSchemesAsPrivileged([
    {
      scheme: VIEWER_SCHEME,
      privileges: {
        standard: true,
        secure: true,
        supportFetchAPI: true,
        bypassCSP: false,
        stream: true,
        corsEnabled: false
      }
    }
  ])
}

/**
 * Installs the protocol.handle handler. Must be called after `app.whenReady()`.
 */
export function installViewerProtocolHandler(): void {
  protocol.handle(VIEWER_SCHEME, async (request) => {
    try {
      // URL format: dotcraft-viewer:///encodeURIComponent(absPath)
      const url = new URL(request.url)
      const encoded = url.pathname.replace(/^\/+/, '')
      const absPath = decodeURIComponent(encoded)

      if (!absPath) {
        return new Response(null, { status: 400 })
      }

      const root = currentWorkspaceRoot
      if (!root) {
        return new Response(null, { status: 403 })
      }

      const allowed = await isPathInsideWorkspace(absPath, root)
      if (!allowed) {
        return new Response(null, { status: 403 })
      }

      return net.fetch(pathToFileURL(absPath).toString())
    } catch {
      return new Response(null, { status: 500 })
    }
  })
}

/** Update the allowed workspace root. Pass '' to deny all requests. */
export function setViewerWorkspaceRoot(workspaceRoot: string): void {
  currentWorkspaceRoot = workspaceRoot
}

/** Returns the current workspace root registered with the protocol handler. */
export function getViewerWorkspaceRoot(): string {
  return currentWorkspaceRoot
}

/**
 * Builds a `dotcraft-viewer://` URL for the given absolute file path.
 * The absolute path must be workspace-scoped before calling this.
 */
export function buildViewerUrl(absolutePath: string): string {
  return `${VIEWER_SCHEME}:///${encodeURIComponent(absolutePath.replace(/\\/g, '/'))}`
}

/**
 * Checks whether `target` is inside `workspaceRoot` after resolving both via
 * `fs.realpath` so that symlinks and `..` traversal cannot escape the boundary.
 */
async function isPathInsideWorkspace(target: string, workspaceRoot: string): Promise<boolean> {
  try {
    const resolvedRoot = await fs.realpath(path.resolve(workspaceRoot))
    const resolvedTarget = await fs.realpath(path.resolve(target))
    const sep = path.sep
    return (
      resolvedTarget === resolvedRoot ||
      resolvedTarget.startsWith(resolvedRoot + sep)
    )
  } catch {
    return false
  }
}
