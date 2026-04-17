import { COMMAND_REF_CLASS, FILE_REF_CLASS, SKILL_REF_CLASS } from './richInputConstants'

export function serializeSkillMarker(skillName: string): string {
  return `[[Use Skill: ${skillName}]]`
}

export function serializeEditor(root: HTMLElement): string {
  let out = ''
  const walk = (node: Node): void => {
    if (node.nodeType === Node.TEXT_NODE) {
      out += node.textContent ?? ''
      return
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return
    const el = node as HTMLElement
    if (el.tagName === 'STYLE' || el.tagName === 'SCRIPT') {
      return
    }
    if (el.classList.contains(FILE_REF_CLASS)) {
      const p = el.getAttribute('data-relative-path') ?? ''
      if (p) out += `@${p}`
      return
    }
    if (el.classList.contains(COMMAND_REF_CLASS)) {
      const command = el.getAttribute('data-command') ?? ''
      if (command) out += command
      return
    }
    if (el.classList.contains(SKILL_REF_CLASS)) {
      const skill = el.getAttribute('data-skill') ?? ''
      if (skill) out += serializeSkillMarker(skill)
      return
    }
    if (el.tagName === 'BR') {
      out += '\n'
      return
    }
    for (const c of Array.from(el.childNodes)) {
      walk(c)
    }
  }
  for (const c of Array.from(root.childNodes)) {
    walk(c)
  }
  return out
}

/**
 * Trims the editor subtree so {@link serializeEditor} length is <= max, preserving
 * file-ref spans when they fit entirely before the cut (never replaces the whole editor with plain text).
 */
export function truncateEditorDomToSerializedLength(root: HTMLElement, max: number): void {
  if (serializeEditor(root).length <= max) return

  let remaining = max

  function removeTrailingAfter(node: Node): void {
    let cur: Node | null = node
    while (cur && cur !== root) {
      while (cur.nextSibling) {
        cur.nextSibling.remove()
      }
      cur = cur.parentNode
    }
  }

  function process(node: Node): boolean {
    if (remaining <= 0) {
      node.parentNode?.removeChild(node)
      return false
    }
    if (node.nodeType === Node.TEXT_NODE) {
      const t = node.textContent ?? ''
      if (t.length <= remaining) {
        remaining -= t.length
        if (remaining === 0) {
          removeTrailingAfter(node)
          return false
        }
        return true
      }
      node.textContent = t.slice(0, remaining)
      remaining = 0
      removeTrailingAfter(node)
      return false
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return true
    const el = node as HTMLElement
    if (el.tagName === 'STYLE' || el.tagName === 'SCRIPT') {
      return true
    }
    if (el.classList.contains(FILE_REF_CLASS)) {
      const p = el.getAttribute('data-relative-path') ?? ''
      const len = p ? 1 + p.length : 0
      if (len <= remaining) {
        remaining -= len
        if (remaining === 0) {
          removeTrailingAfter(el)
          return false
        }
        return true
      }
      removeTrailingAfter(el)
      el.remove()
      remaining = 0
      return false
    }
    if (el.classList.contains(COMMAND_REF_CLASS)) {
      const command = el.getAttribute('data-command') ?? ''
      const len = command.length
      if (len <= remaining) {
        remaining -= len
        if (remaining === 0) {
          removeTrailingAfter(el)
          return false
        }
        return true
      }
      removeTrailingAfter(el)
      el.remove()
      remaining = 0
      return false
    }
    if (el.classList.contains(SKILL_REF_CLASS)) {
      const skill = el.getAttribute('data-skill') ?? ''
      const len = skill ? serializeSkillMarker(skill).length : 0
      if (len <= remaining) {
        remaining -= len
        if (remaining === 0) {
          removeTrailingAfter(el)
          return false
        }
        return true
      }
      removeTrailingAfter(el)
      el.remove()
      remaining = 0
      return false
    }
    if (el.tagName === 'BR') {
      if (remaining >= 1) {
        remaining -= 1
        if (remaining === 0) {
          removeTrailingAfter(el)
          return false
        }
        return true
      }
      removeTrailingAfter(el)
      el.remove()
      remaining = 0
      return false
    }
    const children = Array.from(node.childNodes)
    for (let i = 0; i < children.length; i++) {
      const child = children[i]!
      const cont = process(child)
      if (!cont) {
        for (let j = i + 1; j < children.length; j++) {
          children[j]!.remove()
        }
        return false
      }
    }
    return true
  }

  const top = Array.from(root.childNodes)
  for (let i = 0; i < top.length; i++) {
    const cont = process(top[i]!)
    if (!cont) {
      for (let j = i + 1; j < top.length; j++) {
        top[j]!.remove()
      }
      break
    }
  }
}
