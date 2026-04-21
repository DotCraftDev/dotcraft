/**
 * Renderer-side utility for building `dotcraft-viewer://` URLs.
 *
 * The main process mirrors the same logic in viewerFileProtocol.ts.
 * URL format: `dotcraft-viewer:///<encodeURIComponent(absolutePath)>`
 */
export const VIEWER_SCHEME = 'dotcraft-viewer'

/**
 * Builds a `dotcraft-viewer://` URL from an absolute file path.
 * Uses forward slashes for cross-platform consistency (Windows paths are
 * normalized before encoding).
 */
export function buildViewerUrlRenderer(absolutePath: string): string {
  const normalized = absolutePath.replace(/\\/g, '/')
  return `${VIEWER_SCHEME}:///${encodeURIComponent(normalized)}`
}
