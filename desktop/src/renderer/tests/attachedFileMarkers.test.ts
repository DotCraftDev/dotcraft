import { describe, expect, it } from 'vitest'
import {
  expandAttachedFileMarkersForModel,
  parseLeadingAttachedFileMarkers,
  serializeAttachedFileMarkers
} from '../utils/attachedFileMarkers'

describe('attachedFileMarkers', () => {
  it('serializes files without body text', () => {
    expect(
      serializeAttachedFileMarkers(
        [
          { path: 'C:\\logs\\a.txt', fileName: 'a.txt' },
          { path: 'D:\\docs\\b.md', fileName: 'b.md' }
        ],
        ''
      )
    ).toBe('[[Attached File: C:\\logs\\a.txt]]\n[[Attached File: D:\\docs\\b.md]]')
  })

  it('serializes files ahead of body text with a blank separator', () => {
    expect(
      serializeAttachedFileMarkers(
        [{ path: 'C:\\logs\\a.txt', fileName: 'a.txt' }],
        'Summarize this file'
      )
    ).toBe('[[Attached File: C:\\logs\\a.txt]]\n\nSummarize this file')
  })

  it('parses only leading attached file markers and preserves body text', () => {
    expect(
      parseLeadingAttachedFileMarkers(
        '[[Attached File: C:\\logs\\a.txt]]\n[[Attached File: D:\\docs\\b.md]]\n\nReview these'
      )
    ).toEqual({
      files: [
        { path: 'C:\\logs\\a.txt', fileName: 'a.txt' },
        { path: 'D:\\docs\\b.md', fileName: 'b.md' }
      ],
      bodyText: 'Review these'
    })
  })

  it('does not parse body markers as leading file attachments', () => {
    expect(parseLeadingAttachedFileMarkers('Please keep [[Attached File: C:\\logs\\a.txt]] literal')).toEqual({
      files: [],
      bodyText: 'Please keep [[Attached File: C:\\logs\\a.txt]] literal'
    })
  })

  it('expands leading file markers into absolute paths for model-visible text', () => {
    expect(
      expandAttachedFileMarkersForModel(
        '[[Attached File: C:\\logs\\a.txt]]\n[[Attached File: D:\\docs\\b.md]]\n\nReview these'
      )
    ).toBe('C:\\logs\\a.txt\nD:\\docs\\b.md\n\nReview these')
  })
})

