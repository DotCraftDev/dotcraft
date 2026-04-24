import { describe, it, expect, beforeEach, vi } from 'vitest'
import {
  VIEWER_SCHEME,
  buildViewerUrl,
  getViewerWorkspaceRoot,
  installViewerProtocolHandlerForSession,
  isPathInsideWorkspace,
  setViewerWorkspaceRoot,
  viewerUrlToPath
} from '../viewerFileProtocol'

beforeEach(() => {
  setViewerWorkspaceRoot('')
})

describe('VIEWER_SCHEME', () => {
  it('is "dotcraft-viewer"', () => {
    expect(VIEWER_SCHEME).toBe('dotcraft-viewer')
  })
})

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

describe('buildViewerUrl', () => {
  it('returns a dotcraft-viewer:// URL', () => {
    const url = buildViewerUrl('/home/user/project/src/main.ts')
    expect(url.startsWith(`${VIEWER_SCHEME}://workspace/`)).toBe(true)
  })

  it('uses path-like URLs so relative HTML assets can resolve', () => {
    const url = buildViewerUrl('/home/user/my project/file name.ts')
    expect(url).toContain('/home/user/my%20project/file%20name.ts')
    expect(url).not.toContain('my%20project%2Ffile')
  })

  it('normalizes Windows backslashes to forward slashes', () => {
    const url = buildViewerUrl('C:\\Users\\user\\project\\src\\index.ts')
    expect(url).not.toContain('\\')
    expect(url).toContain('/C%3A/Users/user/project/src/index.ts')
  })

  it('handles paths with special characters', () => {
    const abs = '/home/user/special chars/resume.md'
    const url = buildViewerUrl(abs)
    expect(decodeURI(url)).toContain('/home/user/special chars/resume.md')
  })
})

describe('viewerUrlToPath', () => {
  it('decodes fixed-host Windows viewer URLs without creating UNC paths', () => {
    expect(viewerUrlToPath(`${VIEWER_SCHEME}://workspace/E%3A/workspace/index.html`)).toBe(
      'E:/workspace/index.html'
    )
  })

  it('decodes legacy drive-host URLs defensively', () => {
    expect(viewerUrlToPath(`${VIEWER_SCHEME}://e/workspace/index.html`)).toBe(
      'E:/workspace/index.html'
    )
  })

  it('decodes legacy empty-host URLs', () => {
    expect(viewerUrlToPath(`${VIEWER_SCHEME}:///E:/workspace/index.html`)).toBe(
      'E:/workspace/index.html'
    )
  })
})

describe('isPathInsideWorkspace', () => {
  it('rejects missing workspace roots', async () => {
    await expect(isPathInsideWorkspace('/tmp/project/index.html', '')).resolves.toBe(false)
  })
})

describe('installViewerProtocolHandlerForSession', () => {
  it('registers the viewer protocol handler on custom sessions once', () => {
    const handle = vi.fn()
    const fakeSession = {
      protocol: { handle }
    } as unknown as Electron.Session

    installViewerProtocolHandlerForSession(fakeSession)
    installViewerProtocolHandlerForSession(fakeSession)

    expect(handle).toHaveBeenCalledTimes(1)
    expect(handle).toHaveBeenCalledWith(VIEWER_SCHEME, expect.any(Function))
  })
})
