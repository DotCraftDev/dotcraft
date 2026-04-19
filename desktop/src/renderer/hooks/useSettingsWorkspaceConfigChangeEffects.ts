import { useEffect, useRef } from 'react'
import type { WorkspaceConfigChangedPayload } from '../utils/workspaceConfigChanged'

interface UseSettingsWorkspaceConfigChangeEffectsArgs {
  change: WorkspaceConfigChangedPayload | null
  changeSeq: number
  llmDirty: boolean
  mcpEnabled: boolean
  onExternalLlmChangeNotice: () => void
  reloadWorkspaceCore: () => Promise<void> | void
  reloadMcpData: () => Promise<void> | void
  clearServerChannels: () => void
}

export function useSettingsWorkspaceConfigChangeEffects({
  change,
  changeSeq,
  llmDirty,
  mcpEnabled,
  onExternalLlmChangeNotice,
  reloadWorkspaceCore,
  reloadMcpData,
  clearServerChannels
}: UseSettingsWorkspaceConfigChangeEffectsArgs): void {
  const lastHandledSeqRef = useRef(changeSeq)

  useEffect(() => {
    if (change == null || changeSeq === 0 || changeSeq <= lastHandledSeqRef.current) {
      return
    }

    lastHandledSeqRef.current = changeSeq

    const changedRegions = new Set(change.regions)
    const llmCoreChanged =
      changedRegions.has('workspace.model') ||
      changedRegions.has('workspace.apiKey') ||
      changedRegions.has('workspace.endpoint')
    const workspaceCoreChanged =
      llmCoreChanged ||
      changedRegions.has('welcomeSuggestions')

    if (workspaceCoreChanged) {
      if (llmCoreChanged && llmDirty && change.source !== 'workspace/config/update') {
        onExternalLlmChangeNotice()
      }
      void reloadWorkspaceCore()
    }

    if (changedRegions.has('mcp') && mcpEnabled) {
      void reloadMcpData()
    }

    if (changedRegions.has('externalChannel')) {
      clearServerChannels()
    }
  }, [
    change,
    changeSeq,
    clearServerChannels,
    llmDirty,
    mcpEnabled,
    onExternalLlmChangeNotice,
    reloadMcpData,
    reloadWorkspaceCore
  ])
}
