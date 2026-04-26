import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const tokensCssPath = resolve(__dirname, '../styles/tokens.css')

describe('conversation typography tokens', () => {
  it('uses compact conversation font and code sizing tokens', () => {
    const tokensCss = readFileSync(tokensCssPath, 'utf8')

    expect(tokensCss).toContain('--text-body-size: 13px')
    expect(tokensCss).toContain('--text-body-line-height: 1.48')
    expect(tokensCss).toContain('--text-code-size: 12px')
    expect(tokensCss).toContain('--text-code-line-height: 1.44')
  })

  it('trims trailing markdown block margin', () => {
    const tokensCss = readFileSync(tokensCssPath, 'utf8')

    expect(tokensCss).toContain('.markdown-body > :last-child')
    expect(tokensCss).toContain('margin-bottom: 0 !important')
  })

  it('runs tool text shimmer in the intended direction at a calmer speed', () => {
    const tokensCss = readFileSync(tokensCssPath, 'utf8')

    expect(tokensCss).toContain('background-position: 240px 50%')
    expect(tokensCss).toContain('animation: tool-running-gradient 4.8s linear infinite')
    expect(tokensCss).not.toContain('background-position: -240px 50%')
    expect(tokensCss).not.toContain('animation: tool-running-gradient 1.8s linear infinite')
  })

  it('uses a seamless fixed-period automation tab shimmer', () => {
    const tokensCss = readFileSync(tokensCssPath, 'utf8')

    expect(tokensCss).toContain('@keyframes dotcraft-automation-tab-flow')
    expect(tokensCss).toContain('background-position: 96px 50%')
    expect(tokensCss).toContain('.dotcraft-automation-viewer-tab')
    expect(tokensCss).toContain('animation: none !important')
    expect(tokensCss).not.toContain('background-position: 220% 50%')
  })
})
