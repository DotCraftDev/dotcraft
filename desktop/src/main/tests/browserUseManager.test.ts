import { describe, expect, it, vi } from 'vitest'

vi.mock('electron', () => ({
  BrowserWindow: vi.fn(),
  session: {
    fromPartition: vi.fn(() => ({
      protocol: { handle: vi.fn() },
      on: vi.fn(),
      setPermissionCheckHandler: vi.fn(),
      setPermissionRequestHandler: vi.fn()
    }))
  }
}))

import {
  BrowserUseManager,
  isBrowserUseUrlAllowed,
  normalizeBrowserUseUrl
} from '../browserUseManager'

describe('normalizeBrowserUseUrl', () => {
  it('defaults local host-like URLs to http', () => {
    expect(normalizeBrowserUseUrl('localhost:3000')).toBe('http://localhost:3000/')
    expect(normalizeBrowserUseUrl('127.0.0.1:5173/app')).toBe('http://127.0.0.1:5173/app')
  })

  it('normalizes absolute URLs and rejects invalid input', () => {
    expect(normalizeBrowserUseUrl('http://localhost:3000')).toBe('http://localhost:3000/')
    expect(normalizeBrowserUseUrl('\u0000http://localhost')).toBeNull()
  })
})

describe('isBrowserUseUrlAllowed', () => {
  it('allows local, file, and dotcraft-viewer URLs', () => {
    expect(isBrowserUseUrlAllowed('http://localhost:3000/')).toBe(true)
    expect(isBrowserUseUrlAllowed('https://127.0.0.1:8443/')).toBe(true)
    expect(isBrowserUseUrlAllowed('file:///tmp/index.html')).toBe(true)
    expect(isBrowserUseUrlAllowed('dotcraft-viewer://workspace/E%3A/index.html')).toBe(true)
  })

  it('blocks remote and unsupported URLs', () => {
    expect(isBrowserUseUrlAllowed('https://example.com/')).toBe(false)
    expect(isBrowserUseUrlAllowed('javascript:alert(1)')).toBe(false)
  })
})

describe('BrowserUseManager JavaScript runtime', () => {
  it('does not expose external Node globals', async () => {
    const manager = new BrowserUseManager()
    const owner = { getTitle: () => 'test-window' } as Electron.BrowserWindow
    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'return `${typeof process}:${typeof require}`;'
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('undefined:undefined')
  })
})
