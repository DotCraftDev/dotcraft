import type { ComposerFileAttachment } from '../types/conversation'

const ATTACHED_FILE_PREFIX = '[[Attached File: '
const ATTACHED_FILE_SUFFIX = ']]'

export interface ParsedAttachedFileMarkers {
  files: ComposerFileAttachment[]
  bodyText: string
}

export function serializeAttachedFileMarker(path: string): string {
  return `${ATTACHED_FILE_PREFIX}${path}${ATTACHED_FILE_SUFFIX}`
}

export function serializeAttachedFileMarkers(
  files: ComposerFileAttachment[],
  text: string
): string {
  const normalizedFiles = files
    .map((file) => ({
      path: file.path.trim(),
      fileName: file.fileName.trim() || file.path.trim().split(/[/\\]/).pop() || file.path.trim()
    }))
    .filter((file) => file.path.length > 0)

  const normalizedText = text.trim()
  if (normalizedFiles.length === 0) {
    return normalizedText
  }

  const markerBlock = normalizedFiles
    .map((file) => serializeAttachedFileMarker(file.path))
    .join('\n')

  return normalizedText.length > 0
    ? `${markerBlock}\n\n${normalizedText}`
    : markerBlock
}

export function parseLeadingAttachedFileMarkers(text: string): ParsedAttachedFileMarkers {
  const normalized = text.replace(/\r\n/g, '\n')
  const lines = normalized.split('\n')
  const files: ComposerFileAttachment[] = []
  let index = 0

  while (index < lines.length) {
    const line = lines[index] ?? ''
    const match = /^\[\[Attached File:\s+(.+)\]\]$/.exec(line)
    if (!match) break
    const path = match[1]?.trim() ?? ''
    if (!path) break
    files.push({
      path,
      fileName: path.split(/[/\\]/).pop() ?? path
    })
    index += 1
  }

  if (files.length === 0) {
    return { files: [], bodyText: text }
  }

  if (index < lines.length && lines[index] === '') {
    index += 1
  }

  return {
    files,
    bodyText: lines.slice(index).join('\n')
  }
}

export function expandAttachedFileMarkersForModel(text: string): string {
  const { files, bodyText } = parseLeadingAttachedFileMarkers(text)
  if (files.length === 0) return text

  const pathBlock = files.map((file) => file.path).join('\n')
  return bodyText.length > 0 ? `${pathBlock}\n\n${bodyText}` : pathBlock
}

