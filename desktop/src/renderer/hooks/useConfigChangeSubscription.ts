import { useEffect, useRef } from 'react'

export interface WorkspaceConfigChangedPayload {
  source: string
  regions: string[]
  changedAt: string
}

export interface ConfigChangeActions {
  onWorkspaceModelChanged?: (payload: WorkspaceConfigChangedPayload) => void
  onWorkspaceApiKeyChanged?: (payload: WorkspaceConfigChangedPayload) => void
  onWorkspaceEndPointChanged?: (payload: WorkspaceConfigChangedPayload) => void
  onSkillsChanged?: (payload: WorkspaceConfigChangedPayload) => void
  onMcpChanged?: (payload: WorkspaceConfigChangedPayload) => void
  onExternalChannelChanged?: (payload: WorkspaceConfigChangedPayload) => void
}

const DEDUPE_WINDOW_MS = 1000

/**
 * Subscribes to workspace/configChanged and dispatches by region.
 */
export function useConfigChangeSubscription(actions: ConfigChangeActions): void {
  const actionsRef = useRef(actions)
  actionsRef.current = actions
  const dedupeRef = useRef<Map<string, number>>(new Map())

  useEffect(() => {
    const unsubscribe = window.api.appServer.onNotification((payload) => {
      if (payload.method !== 'workspace/configChanged') return
      const raw = (payload.params ?? {}) as Partial<WorkspaceConfigChangedPayload>
      const source = typeof raw.source === 'string' ? raw.source : ''
      const regions = Array.isArray(raw.regions) ? raw.regions.filter((r): r is string => typeof r === 'string') : []
      if (regions.length === 0) return
      const changedAt = typeof raw.changedAt === 'string' ? raw.changedAt : new Date().toISOString()
      const event: WorkspaceConfigChangedPayload = { source, regions, changedAt }

      const changedAtMs = Date.parse(changedAt)
      for (const region of regions) {
        const dedupeKey = `${source}:${region}`
        const previous = dedupeRef.current.get(dedupeKey)
        if (previous != null && Number.isFinite(changedAtMs) && changedAtMs - previous <= DEDUPE_WINDOW_MS) {
          continue
        }
        if (Number.isFinite(changedAtMs)) {
          dedupeRef.current.set(dedupeKey, changedAtMs)
        }
        switch (region) {
          case 'workspace.model':
            actionsRef.current.onWorkspaceModelChanged?.(event)
            break
          case 'workspace.apiKey':
            actionsRef.current.onWorkspaceApiKeyChanged?.(event)
            break
          case 'workspace.endpoint':
            actionsRef.current.onWorkspaceEndPointChanged?.(event)
            break
          case 'skills':
            actionsRef.current.onSkillsChanged?.(event)
            break
          case 'mcp':
            actionsRef.current.onMcpChanged?.(event)
            break
          case 'externalChannel':
            actionsRef.current.onExternalChannelChanged?.(event)
            break
          default:
            break
        }
      }
    })

    return () => {
      unsubscribe()
      dedupeRef.current.clear()
    }
  }, [])
}
