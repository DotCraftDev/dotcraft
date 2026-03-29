import { describe, it, expect } from 'vitest'
import {
  formatInvocationDisplay,
  formatResultSummary,
  invocationNeedsCallingPrefix,
  truncate
} from '../utils/webToolDisplay'

const en = 'en' as const

describe('truncate', () => {
  it('leaves short strings unchanged', () => {
    expect(truncate('abc', 80)).toBe('abc')
  })

  it('truncates long strings with ellipsis', () => {
    const s = 'a'.repeat(100)
    expect(truncate(s, 80).length).toBe(81)
    expect(truncate(s, 80).endsWith('…')).toBe(true)
  })
})

describe('formatInvocationDisplay', () => {
  it('formats WebSearch with query and maxResults', () => {
    expect(
      formatInvocationDisplay('WebSearch', { query: 'rust async', maxResults: 5 }, en)
    ).toBe('Searched "rust async"')
  })

  it('truncates long WebSearch query', () => {
    const q = 'a'.repeat(100)
    const out = formatInvocationDisplay('WebSearch', { query: q }, en)
    expect(out?.startsWith('Searched "')).toBe(true)
    expect(out?.endsWith('…"')).toBe(true)
  })

  it('formats WebFetch with url', () => {
    expect(
      formatInvocationDisplay('WebFetch', { url: 'https://example.com/path' }, en)
    ).toBe('Fetched https://example.com/path')
  })

  it('formats SearchTools', () => {
    expect(formatInvocationDisplay('SearchTools', { query: 'ReadFile' }, en)).toBe(
      'Searched tools: "ReadFile"'
    )
  })

  it('returns null for WebSearch without string query', () => {
    expect(formatInvocationDisplay('WebSearch', {}, en)).toBeNull()
  })
})

describe('invocationNeedsCallingPrefix', () => {
  it('is false for standalone web tools with valid args', () => {
    expect(
      invocationNeedsCallingPrefix('WebSearch', { query: 'x', maxResults: 5 })
    ).toBe(false)
    expect(
      invocationNeedsCallingPrefix('WebFetch', { url: 'https://a.com' })
    ).toBe(false)
    expect(invocationNeedsCallingPrefix('SearchTools', { query: 'ReadFile' })).toBe(false)
  })

  it('is true when standalone parse fails or non-web tool', () => {
    expect(invocationNeedsCallingPrefix('WebSearch', {})).toBe(true)
    expect(invocationNeedsCallingPrefix('ReadFile', { path: 'src/main.rs' })).toBe(true)
  })
})

describe('formatResultSummary', () => {
  it('parses WebSearch results and domains', () => {
    const json = JSON.stringify({
      query: 'q',
      provider: 'exa',
      results: [
        { title: 'T', url: 'https://exa.ai/docs' },
        { title: 'U', url: 'http://b.com/x' }
      ]
    })
    const lines = formatResultSummary('WebSearch', json)
    expect(lines).not.toBeNull()
    expect(lines!.length).toBe(3)
    expect(lines![0]).toBe('2 results:')
    expect(lines![1]).toContain('exa.ai')
    expect(lines![2]).toContain('b.com')
  })

  it('parses WebSearch error', () => {
    const lines = formatResultSummary('WebSearch', JSON.stringify({ error: 'rate limited' }))
    expect(lines).toEqual(['Error: rate limited'])
  })

  it('handles empty WebSearch results array', () => {
    const lines = formatResultSummary('WebSearch', JSON.stringify({ query: 'x', results: [] }))
    expect(lines).toEqual(['No results found.'])
  })

  it('double-decodes JSON string wrapper', () => {
    const inner = JSON.stringify({ results: [{ title: 'Hi', url: 'https://z.com' }] })
    const outer = JSON.stringify(inner)
    const lines = formatResultSummary('WebSearch', outer)
    expect(lines).not.toBeNull()
    expect(lines!.length).toBe(2)
    expect(lines![0]).toBe('1 result:')
  })

  it('formats WebFetch summary', () => {
    const json = JSON.stringify({
      status: 200,
      length: 50000,
      extractor: 'readability',
      truncated: true
    })
    const lines = formatResultSummary('WebFetch', json)
    expect(lines).not.toBeNull()
    expect(lines!.length).toBe(1)
    expect(lines![0]).toContain('200')
    expect(lines![0]).toContain('50,000')
    expect(lines![0]).toContain('readability')
    expect(lines![0]).toContain('truncated')
  })

  it('formats SearchTools first line', () => {
    const text = 'Found 3 matching tool(s)\nReadFile\nWriteFile'
    const lines = formatResultSummary('SearchTools', text)
    expect(lines).toEqual(['Found 3 matching tool(s)'])
  })

  it('returns null for unknown tool', () => {
    expect(formatResultSummary('ReadFile', '{}')).toBeNull()
  })
})
