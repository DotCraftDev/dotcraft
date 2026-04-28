export type SkillMarketProviderId = 'skillhub' | 'clawhub'

export interface SkillMarketSearchRequest {
  query: string
  provider?: SkillMarketProviderId | 'all'
  page?: number
  limit?: number
}

export interface SkillMarketDetailRequest {
  provider: SkillMarketProviderId
  slug: string
}

export interface SkillMarketInstallRequest {
  provider: SkillMarketProviderId
  slug: string
  version?: string
  overwrite?: boolean
}

export interface MarketSkillSummary {
  provider: SkillMarketProviderId
  slug: string
  name: string
  description?: string
  version?: string
  author?: string
  downloads?: number
  rating?: number
  tags?: string[]
  sourceUrl?: string
  installed?: boolean
  updateAvailable?: boolean
}

export interface MarketSkillDetail extends MarketSkillSummary {
  readme?: string
  files?: Array<{ path: string; size?: number }>
  versions?: string[]
}

export interface MarketInstallResult {
  skillName: string
  targetDir: string
  version?: string
  overwritten: boolean
}

export interface SkillMarketSearchResult {
  skills: MarketSkillSummary[]
}

