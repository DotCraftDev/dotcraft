/**
 * Tests for the viewerFileProtocol boundary check helpers.
 *
 * `isPathInsideWorkspace` is not directly exported, so we test the observable
 * behaviour through `buildViewerUrl` (pure) and through `setViewerWorkspaceRoot` /
 * `getViewerWorkspaceRoot` (state management).
 *
 * File-system boundary tests are done via the exported `classifyFile`'s underlying
 * workspace check — but those live in viewerIpc.test.ts.  Here we focus on the
 * pure / stateful helpers that don't require a running Electron app.
 */
import { describe, it, expect, beforeEach } from 'vitest'
import {
  VIEWER_SCHEME,
  buildViewerUrl,
  setViewerWorkspaceRoot,
  getViewerWorkspaceRoot
} from '../viewerFileProtocol'

beforeEach(() => {
  // Reset workspace root between tests
  setViewerWorkspaceRoot('')
})

// ---------------------------------------------------------------------------
// VIEWER_SCHEME constant
// ---------------------------------------------------------------------------

describe('VIEWER_SCHEME', () => {
  it('is "dotcraft-viewer"', () => {
    expect(VIEWER_SCHEME).toBe('dotcraft-viewer')
  })
})

// ---------------------------------------------------------------------------
// setViewerWorkspaceRoot / getViewerWorkspaceRoot
// ---------------------------------------------------------------------------

describe('setViewerWorkspaceRoot / getViewerWorkspaceRoot', () => {
  it('starts empty', () => {
    expect(getViewerWorkspaceRoot()).toBe('')
  })

  it('stores the provided path', () => {
    setViewerWorkspaceRoot('/home/user/project')
    expect(getViewerWorkspaceRoot()).toBe('/home/user/project')
  })

  it('can be cleared by passing an empty string', () => {
    setViewerWorkspaceRoot('/some/path')
    setViewerWorkspaceRoot('')
    expect(getViewerWorkspaceRoot()).toBe('')
  })

  it('replaces the previous value', () => {
    setViewerWorkspaceRoot('/first')
    setViewerWorkspaceRoot('/second')
    expect(getViewerWorkspaceRoot()).toBe('/second')
  })
})

// ---------------------------------------------------------------------------
// buildViewerUrl
// ---------------------------------------------------------------------------

describe('buildViewerUrl', () => {
  it('returns a dotcraft-viewer:// URL', () => {
    const url = buildViewerUrl('/home/user/project/src/main.ts')
    expect(url.startsWith(`${VIEWER_SCHEME}:///`)).toBe(true)
  })

  it('encodes the path so decodeURIComponent round-trips correctly', () => {
    const abs = '/home/user/my project/file name.ts'
    const url = buildViewerUrl(abs)
    const encoded = url.slice(`${VIEWER_SCHEME}:///`.length)
    expect(decodeURIComponent(encoded)).toBe(abs.replace(/\\/g, '/'))
  })

  it('normalizes Windows backslashes to forward slashes', () => {
    const abs = 'C:\\Users\\user\\project\\src\\index.ts'
    const url = buildViewerUrl(abs)
    const encoded = url.slice(`${VIEWER_SCHEME}:///`.length)
    const decoded = decodeURIComponent(encoded)
    expect(decoded).not.toContain('\\')
    expect(decoded).toContain('C:/Users/user/project/src/index.ts')
  })

  it('handles paths with special characters', () => {
    const abs = '/home/user/café/résumé.md'
    const url = buildViewerUrl(abs)
    const encoded = url.slice(`${VIEWER_SCHEME}:///`.length)
    expect(decodeURIComponent(encoded)).toBe(abs)
  })
})
