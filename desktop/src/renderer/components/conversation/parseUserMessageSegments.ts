import type { InputPart } from '../../types/conversation'
import { serializeSkillMarker } from './richInputSerialization'
import { parseLeadingAttachedFileMarkers } from '../../utils/attachedFileMarkers'

export type UserMessageSegment =
  | { type: 'text'; value: string }
  | { type: 'attachedFile'; path: string; fileName: string }
  | { type: 'fileRef'; relativePath: string }
  | { type: 'commandRef'; commandText: string }
  | { type: 'skillRef'; skillName: string }

const LEGACY_SKILL_MARKER_RE = /\[\[Use Skill:\s*([^\]]+?)\]\]/g

interface Match {
  type: 'file' | 'command' | 'skill'
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
        j += 1
      }
      const relativePath = text.slice(i + 1, j)
      if (relativePath.length > 0) {
        return { type: 'file', start: i, end: j, value: relativePath }
      }
    }
    i += 1
  }
  return null
}

function findNextCommandRef(text: string, from: number): Match | null {
  let i = from
  while (i < text.length) {
    if (text[i] === '/' && (i === 0 || /\s/.test(text[i - 1]!))) {
      let j = i + 1
      while (j < text.length && !/\s/.test(text[j]!)) {
        j += 1
      }
      const token = text.slice(i, j)
      if (/^\/[a-z0-9][a-z0-9-]*$/i.test(token)) {
        return { type: 'command', start: i, end: j, value: token }
      }
    }
    i += 1
  }
  return null
}

function findNextSkillRef(text: string, from: number): Match | null {
  LEGACY_SKILL_MARKER_RE.lastIndex = from
  const legacyMatch = LEGACY_SKILL_MARKER_RE.exec(text)
  if (legacyMatch) {
    const skillName = legacyMatch[1]?.trim() ?? ''
    if (skillName.length > 0) {
      return {
        type: 'skill',
        start: legacyMatch.index,
        end: legacyMatch.index + legacyMatch[0].length,
        value: skillName
      }
    }
  }

  let i = from
  while (i < text.length) {
    if (text[i] === '$' && (i === 0 || /\s/.test(text[i - 1]!))) {
      let j = i + 1
      while (j < text.length && /[a-z0-9_-]/i.test(text[j]!)) {
        j += 1
      }
      const skillName = text.slice(i + 1, j)
      if (skillName.length > 0) {
        return { type: 'skill', start: i, end: j, value: skillName }
      }
    }
    i += 1
  }

  return null
}

/**
 * Splits user message text into plain text, @fileRef segments, slash-command
 * fallback segments, and skill marker segments.
 */
export function parseUserMessageSegments(text: string): UserMessageSegment[] {
  const out: UserMessageSegment[] = []
  const { files, bodyText } = parseLeadingAttachedFileMarkers(text)
  for (const file of files) {
    out.push({
      type: 'attachedFile',
      path: file.path,
      fileName: file.fileName
    })
  }

  const source = bodyText
  let cursor = 0

  while (cursor < source.length) {
    const next = [findNextFileRef(source, cursor), findNextCommandRef(source, cursor), findNextSkillRef(source, cursor)]
      .filter((candidate): candidate is Match => candidate != null)
      .sort((a, b) => a.start - b.start)[0]

    if (!next) {
      if (source.length > cursor) {
        out.push({ type: 'text', value: source.slice(cursor) })
      }
      break
    }

    if (next.start > cursor) {
      out.push({ type: 'text', value: source.slice(cursor, next.start) })
    }

    if (next.type === 'file') {
      out.push({ type: 'fileRef', relativePath: next.value })
    } else if (next.type === 'command') {
      out.push({ type: 'commandRef', commandText: next.value })
    } else {
      out.push({ type: 'skillRef', skillName: next.value })
    }

    cursor = next.end
  }

  return out
}

export function segmentsFromNativeInputParts(parts: InputPart[]): UserMessageSegment[] {
  const out: UserMessageSegment[] = []
  for (const part of parts) {
    switch (part.type) {
      case 'text': {
        const { files, bodyText } = parseLeadingAttachedFileMarkers(part.text)
        for (const file of files) {
          out.push({ type: 'attachedFile', path: file.path, fileName: file.fileName })
        }
        if (bodyText.length > 0) {
          out.push(...parseUserMessageSegments(bodyText))
        }
        break
      }
      case 'fileRef':
        out.push({ type: 'fileRef', relativePath: part.displayPath ?? part.path })
        break
      case 'commandRef':
        {
          const name = typeof part.name === 'string' ? part.name.trim() : ''
          if (!name) {
            if (typeof part.rawText === 'string' && part.rawText.trim() !== '') {
              out.push({ type: 'text', value: part.rawText })
            }
            break
          }

          out.push({ type: 'commandRef', commandText: `/${name}` })

          let argsText = typeof part.argsText === 'string' ? part.argsText.trim() : ''
          if (argsText.length === 0 && typeof part.rawText === 'string') {
            const normalizedRaw = part.rawText.trim()
            const firstWhitespace = normalizedRaw.search(/\s/)
            if (firstWhitespace >= 0) {
              argsText = normalizedRaw.slice(firstWhitespace + 1).trim()
            }
          }
          if (argsText.length > 0) {
            out.push({ type: 'text', value: ` ${argsText}` })
          }
        }
        break
      case 'skillRef':
        out.push({ type: 'skillRef', skillName: part.name })
        break
      default:
        break
    }
  }
  return out
}

export function stringifySkillMarker(skillName: string): string {
  return serializeSkillMarker(skillName)
}
