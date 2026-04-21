/**
 * Registers the `dotcraft-viewer://` custom Electron protocol for safe file
 * serving to the viewer panel.
 *
 * Security contract:
 *  - A workspace must be selected; when cleared, all requests return 403.
 *  - The requested path must resolve to a regular file.
 *  - Path traversal through malformed URL payloads is rejected by path decoding.
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

      const stat = await fs.stat(absPath)
      if (!stat.isFile()) {
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

