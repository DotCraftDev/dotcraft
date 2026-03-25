import { describe, expect, it } from 'vitest'
import { FILE_REF_CLASS } from '../components/conversation/richInputConstants'
import { serializeEditor, truncateEditorDomToSerializedLength } from '../components/conversation/richInputSerialization'

function makeFileRef(relativePath: string): HTMLSpanElement {
  const s = document.createElement('span')
  s.className = FILE_REF_CLASS
  s.setAttribute('data-relative-path', relativePath)
  s.textContent = '📄 x'
  return s
}

describe('truncateEditorDomToSerializedLength', () => {
  it('keeps file-ref spans that fit entirely before the cut', () => {
    const root = document.createElement('div')
    root.appendChild(document.createTextNode('aa'))
    root.appendChild(makeFileRef('b'))
    root.appendChild(document.createTextNode('ccccc'))
    // serialized: "aa" + "@b" + "ccccc" = 9 chars
    truncateEditorDomToSerializedLength(root, 5)
    expect(serializeEditor(root).length).toBe(5)
    expect(serializeEditor(root)).toBe('aa@bc')
    expect(root.querySelectorAll(`.${FILE_REF_CLASS}`).length).toBe(1)
    expect((root.querySelector(`.${FILE_REF_CLASS}`) as HTMLElement).getAttribute('data-relative-path')).toBe('b')
  })

  it('drops a file ref that does not fit entirely instead of flattening to text', () => {
    const root = document.createElement('div')
    root.appendChild(document.createTextNode('hello'))
    root.appendChild(makeFileRef('toolong'))
    root.appendChild(document.createTextNode('tail'))
    // "hello" = 5, "@toolong" = 9, total before tail = 14
    truncateEditorDomToSerializedLength(root, 7)
    expect(serializeEditor(root)).toBe('hello')
    expect(root.querySelectorAll(`.${FILE_REF_CLASS}`).length).toBe(0)
  })

  it('does not mutate when already under the limit', () => {
    const root = document.createElement('div')
    const span = makeFileRef('x')
    root.appendChild(span)
    truncateEditorDomToSerializedLength(root, 100)
    expect(root.querySelectorAll(`.${FILE_REF_CLASS}`).length).toBe(1)
    expect(serializeEditor(root)).toBe('@x')
  })
})
