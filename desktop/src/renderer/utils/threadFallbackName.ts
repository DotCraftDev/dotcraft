interface ThreadFallbackNameArgs {
  visibleText: string
  imagesCount: number
  filesCount: number
  fallbackThreadName: string
  imageFallbackThreadName?: string
  fileFallbackThreadName?: string
  attachmentFallbackThreadName?: string
}

export function getFallbackThreadName({
  visibleText,
  imagesCount,
  filesCount,
  fallbackThreadName,
  imageFallbackThreadName,
  fileFallbackThreadName,
  attachmentFallbackThreadName
}: ThreadFallbackNameArgs): string {
  const trimmed = visibleText.trim()
  if (trimmed.length > 0) {
    return trimmed.length > 50 ? `${trimmed.slice(0, 50)}...` : trimmed
  }

  const hasImages = imagesCount > 0
  const hasFiles = filesCount > 0

  if (hasImages && hasFiles) {
    return attachmentFallbackThreadName ?? fallbackThreadName
  }
  if (hasFiles) {
    return fileFallbackThreadName ?? fallbackThreadName
  }
  if (hasImages) {
    return imageFallbackThreadName ?? fallbackThreadName
  }

  return fallbackThreadName
}
