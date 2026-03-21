import { describe, it, expect } from 'vitest'
import { formatTokenCount } from '../utils/formatTokens'

describe('formatTokenCount', () => {
  it('returns "0" for zero', () => {
    expect(formatTokenCount(0)).toBe('0')
  })

  it('returns the number as string for values under 1000', () => {
    expect(formatTokenCount(1)).toBe('1')
    expect(formatTokenCount(500)).toBe('500')
    expect(formatTokenCount(999)).toBe('999')
  })

  it('returns "1.0k" for exactly 1000', () => {
    expect(formatTokenCount(1000)).toBe('1.0k')
  })

  it('returns "1.5k" for 1500', () => {
    expect(formatTokenCount(1500)).toBe('1.5k')
  })

  it('returns "12.3k" for 12345', () => {
    expect(formatTokenCount(12345)).toBe('12.3k')
  })

  it('returns "100.0k" for 100000', () => {
    expect(formatTokenCount(100000)).toBe('100.0k')
  })
})
