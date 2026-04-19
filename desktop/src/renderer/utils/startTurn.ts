import type { ComposerFileAttachment, ImageAttachment } from '../types/conversation'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import type { InputPart } from '../types/conversation'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { serializeAttachedFileMarkers } from './attachedFileMarkers'

interface StartTurnParams {
  threadId: string
  workspacePath: string
  text: string
  images?: ImageAttachment[]
  files?: ComposerFileAttachment[]
  fallbackThreadName: string
  includeUserPreview?: boolean
  renameThreadFromText?: boolean
}

/**
 * Start a turn with optimistic UI and promote local turn ID when server responds.
 * Returns true when the turn/start RPC is issued, false when there is no input.
 */
export async function startTurnWithOptimisticUI({
  threadId,
  workspacePath,
  text,
  images = [],
  files = [],
  fallbackThreadName,
  includeUserPreview = true,
  renameThreadFromText = true
}: StartTurnParams): Promise<boolean> {
  const visibleText = text.trim()
  const serializedText = serializeAttachedFileMarkers(files, visibleText)
  const inputParts: InputPart[] = []
  if (serializedText.length > 0) {
    inputParts.push({ type: 'text', text: serializedText })
  }
  for (const img of images) {
    inputParts.push({
      type: 'localImage',
      path: img.tempPath,
      mimeType: img.mimeType,
      fileName: img.fileName
    })
  }
  if (inputParts.length === 0) {
    return false
  }

  if (renameThreadFromText) {
    const threadEntry = useThreadStore.getState().threadList.find((t) => t.id === threadId)
    if (!threadEntry?.displayName) {
      const autoName =
        visibleText.length > 50
          ? `${visibleText.slice(0, 50)}...`
          : visibleText || fallbackThreadName
      useThreadStore.getState().renameThread(threadId, autoName)
    }
  }

  const optimisticTurnId = `local-turn-${Date.now()}`
  const optimisticNow = new Date().toISOString()
  const optimisticItems: ConversationItem[] = includeUserPreview
    ? [{
      id: `local-${Date.now()}`,
      type: 'userMessage',
      status: 'completed',
      text: serializedText,
      imageDataUrls: images.map((i) => i.dataUrl),
      images: images.map((i) => ({
        path: i.tempPath,
        mimeType: i.mimeType,
        fileName: i.fileName
      })),
      createdAt: optimisticNow,
      completedAt: optimisticNow
    }]
    : []

  const optimisticTurn: ConversationTurn = {
    id: optimisticTurnId,
    threadId,
    status: 'running',
    items: optimisticItems,
    startedAt: optimisticNow
  }
  useConversationStore.getState().addOptimisticTurn(optimisticTurn)

  try {
    const result = await window.api.appServer.sendRequest('turn/start', {
      threadId,
      input: inputParts,
      identity: {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: `workspace:${workspacePath}`,
        workspacePath
      }
    })
    const res = result as { turn?: { id?: string } }
    if (res.turn?.id) {
      useConversationStore.getState().promoteOptimisticTurn(optimisticTurnId, res.turn.id)
    }
  } catch (err) {
    console.error('turn/start failed:', err)
    useConversationStore.getState().removeOptimisticTurn(optimisticTurnId)
  }

  return true
}
