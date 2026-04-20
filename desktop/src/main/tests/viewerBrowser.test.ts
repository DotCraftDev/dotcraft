import { describe, expect, it } from 'vitest'
import {
  classifyBrowserUrl,
  loadOrReport,
  normalizeBrowserUrl,
  partitionForWorkspace
} from '../viewerBrowser'

describe('normalizeBrowserUrl', () => {
  it('normalizes absolute http/https urls', () => {
    expect(normalizeBrowserUrl('https://example.com/docs')).toBe('https://example.com/docs')
    expect(normalizeBrowserUrl('http://example.com')).toBe('http://example.com/')
  })

  it('promotes host-like input to https', () => {
    expect(normalizeBrowserUrl('example.com')).toBe('https://example.com/')
    expect(normalizeBrowserUrl('docs.example.com/path')).toBe('https://docs.example.com/path')
  })

  it('returns null for empty or control-character input', () => {
    expect(normalizeBrowserUrl('')).toBeNull()
    expect(normalizeBrowserUrl('   ')).toBeNull()
    expect(normalizeBrowserUrl('\u0000https://example.com')).toBeNull()
  })
})

describe('classifyBrowserUrl', () => {
  it('allows http/https and blocks unsupported schemes', () => {
    expect(classifyBrowserUrl('https://example.com')).toBe('allow')
    expect(classifyBrowserUrl('http://example.com')).toBe('allow')
    expect(classifyBrowserUrl('file:///tmp/a.txt')).toBe('blocked')
    expect(classifyBrowserUrl('chrome://settings')).toBe('blocked')
    expect(classifyBrowserUrl('javascript:alert(1)')).toBe('blocked')
  })

  it('marks mailto/tel as external handoff', () => {
    expect(classifyBrowserUrl('mailto:test@example.com')).toBe('external-handoff')
    expect(classifyBrowserUrl('tel:10086')).toBe('external-handoff')
  })
})

describe('partitionForWorkspace', () => {
  it('creates deterministic partition ids', () => {
    const p1 = partitionForWorkspace('F:/dotcraft')
    const p2 = partitionForWorkspace('F:/dotcraft')
    expect(p1).toBe(p2)
    expect(p1.startsWith('persist:dotcraft-viewer:')).toBe(true)
  })

  it('is path-casing-insensitive on Windows style paths', () => {
    const upper = partitionForWorkspace('F:/DOTCRAFT/Workspace')
    const lower = partitionForWorkspace('f:/dotcraft/workspace')
    expect(upper).toBe(lower)
  })
})

describe('loadOrReport', () => {
  it('emits did-fail-load and did-stop-loading when load rejects', async () => {
    const events: Array<{ type: string; message?: string; url?: string }> = []
    await expect(loadOrReport({
      tabId: 'tab-1',
      url: 'https://example.com/',
      load: () => Promise.reject(new Error('load failed')),
      emit: (payload) => {
        events.push({
          type: payload.type,
          message: 'message' in payload ? payload.message : undefined,
          url: 'url' in payload ? payload.url : undefined
        })
      }
    })).resolves.toBeUndefined()

    expect(events).toHaveLength(2)
    expect(events[0]).toEqual({
      type: 'did-fail-load',
      message: 'load failed',
      url: 'https://example.com/'
    })
    expect(events[1]).toEqual({
      type: 'did-stop-loading',
      message: undefined,
      url: 'https://example.com/'
    })
  })
})
