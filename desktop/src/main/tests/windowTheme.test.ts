import { describe, expect, it } from 'vitest'
import { resolveInitialTheme } from '../windowTheme'

describe('resolveInitialTheme', () => {
  it('uses persisted light theme for the first frame', () => {
    expect(resolveInitialTheme({ theme: 'light' })).toBe('light')
  })

  it('defaults missing or unknown values to dark', () => {
    expect(resolveInitialTheme({})).toBe('dark')
    expect(resolveInitialTheme({ theme: 'system' as never })).toBe('dark')
  })
})

