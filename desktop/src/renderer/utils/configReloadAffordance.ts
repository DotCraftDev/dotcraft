export type ReloadBehavior = 'hot' | 'subsystemRestart' | 'processRestart'

export interface FieldDescriptor {
  sectionPath?: string[]
  rootKey?: string
  key: string
  reload: ReloadBehavior | string
  subsystemKey?: string
}

export interface AffordanceInput {
  field: FieldDescriptor
  proxyActive: boolean
}

export type Affordance =
  | { kind: 'live' }
  | { kind: 'subsystemRestart'; subsystemKey: string }
  | { kind: 'processRestart' }
  | { kind: 'lockedByProxy'; reason: 'apiKey' | 'endpoint' }

function isRootAppConfigField(field: FieldDescriptor): boolean {
  return !field.rootKey && (!field.sectionPath || field.sectionPath.length === 0)
}

export function getConfigReloadAffordance(input: AffordanceInput): Affordance {
  const { field, proxyActive } = input

  if (proxyActive && isRootAppConfigField(field)) {
    if (field.key === 'ApiKey') {
      return { kind: 'lockedByProxy', reason: 'apiKey' }
    }
    if (field.key === 'EndPoint') {
      return { kind: 'lockedByProxy', reason: 'endpoint' }
    }
  }

  if (field.reload === 'hot') {
    return { kind: 'live' }
  }

  if (field.reload === 'subsystemRestart') {
    const subsystemKey = field.subsystemKey?.trim()
    if (subsystemKey) {
      return { kind: 'subsystemRestart', subsystemKey }
    }
    return { kind: 'processRestart' }
  }

  return { kind: 'processRestart' }
}
