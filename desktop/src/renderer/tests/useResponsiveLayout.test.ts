import { describe, it, expect } from 'vitest'
import { classifyWidth } from '../hooks/useResponsiveLayout'

/**
 * Tests for the responsive layout breakpoint logic.
 * Spec §8.2: responsive behavior
 */
describe('classifyWidth (responsive breakpoint logic)', () => {
  // ─── Full (>= 1200px) ───────────────────────────────────────────────────────

  it('classifies 1400px as "full" (all panels visible)', () => {
    expect(classifyWidth(1400)).toBe('full')
  })

  it('classifies 1200px (boundary) as "full"', () => {
    expect(classifyWidth(1200)).toBe('full')
  })

  // ─── No detail (900-1199px) ─────────────────────────────────────────────────

  it('classifies 1000px as "no-detail" (detail panel auto-collapses)', () => {
    expect(classifyWidth(1000)).toBe('no-detail')
  })

  it('classifies 1199px as "no-detail" (just below full threshold)', () => {
    expect(classifyWidth(1199)).toBe('no-detail')
  })

  it('classifies 900px (boundary) as "no-detail"', () => {
    expect(classifyWidth(900)).toBe('no-detail')
  })

  // ─── Collapsed (< 900px) ────────────────────────────────────────────────────

  it('classifies 800px as "collapsed" (sidebar icon-only, detail hidden)', () => {
    expect(classifyWidth(800)).toBe('collapsed')
  })

  it('classifies 899px as "collapsed" (just below no-detail threshold)', () => {
    expect(classifyWidth(899)).toBe('collapsed')
  })

  it('classifies very small widths as "collapsed"', () => {
    expect(classifyWidth(400)).toBe('collapsed')
    expect(classifyWidth(0)).toBe('collapsed')
  })
})
