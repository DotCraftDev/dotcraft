import { describe, expect, it } from 'vitest'
import { findSseBoundary } from '../HubClient'

describe('HubClient SSE parsing', () => {
  it('detects LF and CRLF event boundaries', () => {
    expect(findSseBoundary('data: {}\n\n')?.sequence).toBe('\n\n')
    expect(findSseBoundary('data: {}\r\n\r\n')?.sequence).toBe('\r\n\r\n')
  })
})
