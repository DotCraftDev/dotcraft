import { describe, expect, it } from 'vitest'
import { getFallbackThreadName } from '../utils/threadFallbackName'

const fallbackNames = {
  fallbackThreadName: 'Message',
  fileFallbackThreadName: 'File reference message',
  attachmentFallbackThreadName: 'Attachment message'
} as const

describe('getFallbackThreadName', () => {
  it('prefers truncated visible text over attachment fallbacks', () => {
    expect(
      getFallbackThreadName({
        visibleText: 'A'.repeat(60),
        imagesCount: 1,
        filesCount: 1,
        ...fallbackNames
      })
    ).toBe(`${'A'.repeat(50)}...`)
  })

  it('uses the image fallback when only images are present', () => {
    expect(
      getFallbackThreadName({
        visibleText: '',
        imagesCount: 1,
        filesCount: 0,
        ...fallbackNames
      })
    ).toBe('Message')
  })

  it('uses the file fallback when only files are present', () => {
    expect(
      getFallbackThreadName({
        visibleText: '',
        imagesCount: 0,
        filesCount: 2,
        ...fallbackNames
      })
    ).toBe('File reference message')
  })

  it('uses the attachment fallback when images and files are both present', () => {
    expect(
      getFallbackThreadName({
        visibleText: '',
        imagesCount: 1,
        filesCount: 2,
        ...fallbackNames
      })
    ).toBe('Attachment message')
  })

  it('falls back to the caller-provided default when there are no attachments', () => {
    expect(
      getFallbackThreadName({
        visibleText: '',
        imagesCount: 0,
        filesCount: 0,
        ...fallbackNames
      })
    ).toBe('Message')
  })
})
