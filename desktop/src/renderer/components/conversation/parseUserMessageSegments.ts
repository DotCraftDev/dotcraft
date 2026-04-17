import { serializeSkillMarker } from './richInputSerialization'

export type UserMessageSegment =
  | { type: 'text'; value: string }
  | { type: 'fileRef'; relativePath: string }
  | { type: 'skillRef'; skillName: string }

const SKILL_MARKER_RE = /\[\[Use Skill:\s*([^\]]+?)\]\]/g

interface Match {
  type: 'file' | 'skill'
  start: number
  end: number
  value: string
}

function findNextFileRef(text: string, from: number): Match | null {
  let i = from
  while (i < text.length) {
    if (text[i] === '@' && (i === 0 || /\s/.test(text[i - 1]!))) {
      let j = i + 1
      while (j < text.length && !/\s/.test(text[j]!)) {
        j++
      }
      const relativePath = text.slice(i + 1, j)
      if (relativePath.length > 0) {
        return { type: 'file', start: i, end: j, value: relativePath }
      }
    }
    i++
  }
  return null
}

function findNextSkillRef(text: string, from: number): Match | null {
  SKILL_MARKER_RE.lastIndex = from
  const match = SKILL_MARKER_RE.exec(text)
  if (!match) return null
  const skillName = match[1]?.trim() ?? ''
  if (!skillName) return null
  return {
    type: 'skill',
    start: match.index,
    end: match.index + match[0].length,
    value: skillName
  }
}

/**
 * Splits user message text into plain text, @fileRef segments and skill marker segments.
 */
export function parseUserMessageSegments(text: string): UserMessageSegment[] {
  const out: UserMessageSegment[] = []
  let cursor = 0

  while (cursor < text.length) {
    const nextFile = findNextFileRef(text, cursor)
    const nextSkill = findNextSkillRef(text, cursor)

    const next =
      nextFile == null
        ? nextSkill
        : nextSkill == null
          ? nextFile
          : nextFile.start <= nextSkill.start
            ? nextFile
            : nextSkill

    if (!next) {
      out.push({ type: 'text', value: text.slice(cursor) })
      break
    }

    if (next.start > cursor) {
      out.push({ type: 'text', value: text.slice(cursor, next.start) })
    }

    if (next.type === 'file') {
      out.push({ type: 'fileRef', relativePath: next.value })
    } else {
      out.push({ type: 'skillRef', skillName: next.value })
    }

    cursor = next.end
  }

  return out
}

export function stringifySkillMarker(skillName: string): string {
  return serializeSkillMarker(skillName)
}
