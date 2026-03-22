import { create } from 'zustand'

export interface SkillEntry {
  name: string
  description: string
  source: 'builtin' | 'workspace' | 'user'
  available: boolean
  unavailableReason?: string | null
  enabled: boolean
  path: string
  metadata?: Record<string, string> | null
}

interface SkillsState {
  skills: SkillEntry[]
  loading: boolean
  error: string | null
  selectedSkillName: string | null
  skillContent: string | null
  contentLoading: boolean

  fetchSkills(): Promise<void>
  selectSkill(name: string): Promise<void>
  clearSelection(): void
  toggleSkillEnabled(name: string, enabled: boolean): Promise<void>
}

export const useSkillsStore = create<SkillsState>((set, get) => ({
  skills: [],
  loading: false,
  error: null,
  selectedSkillName: null,
  skillContent: null,
  contentLoading: false,

  async fetchSkills() {
    set({ loading: true, error: null })
    try {
      const result = (await window.api.appServer.sendRequest('skills/list', {
        includeUnavailable: true
      })) as { skills?: SkillEntry[] }
      set({ skills: result.skills ?? [], loading: false })
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      set({ error: msg, loading: false })
    }
  },

  async selectSkill(name: string) {
    set({ selectedSkillName: name, skillContent: null, contentLoading: true })
    try {
      const result = (await window.api.appServer.sendRequest('skills/read', {
        name
      })) as { content?: string }
      set({ skillContent: result.content ?? '', contentLoading: false })
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      set({ skillContent: `Error loading skill: ${msg}`, contentLoading: false })
    }
  },

  clearSelection() {
    set({ selectedSkillName: null, skillContent: null, contentLoading: false })
  },

  async toggleSkillEnabled(name: string, enabled: boolean) {
    try {
      const result = (await window.api.appServer.sendRequest('skills/setEnabled', {
        name,
        enabled
      })) as { skill?: SkillEntry }
      const updated = result.skill
      if (updated) {
        set((state) => ({
          skills: state.skills.map((s) => (s.name === updated.name ? { ...s, ...updated } : s))
        }))
      } else {
        await get().fetchSkills()
      }
    } catch (e: unknown) {
      console.error('skills/setEnabled failed:', e)
      throw e
    }
  }
}))
