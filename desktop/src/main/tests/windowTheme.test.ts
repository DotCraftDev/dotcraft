import { describe, expect, it } from 'vitest'
import { resolveInitialTheme } from '../windowTheme'

describe('resolveInitialTheme', () => {
  it('uses persisted light theme for the first frame', () => {
    expect(resolveInitialTheme({ theme: 'light' })).toBe('light')
  })

  it('uses persisted dark theme for the first frame', () => {
    expect(resolveInitialTheme({ theme: 'dark' })).toBe('dark')
  })

  it('defaults missing or unknown values to light', () => {
    expect(resolveInitialTheme({})).toBe('light')
    expect(resolveInitialTheme({ theme: 'system' as never })).toBe('light')
  })
})

