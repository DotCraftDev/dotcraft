import { create } from 'zustand'
import { useReviewPanelStore } from './reviewPanelStore'

/** Polling interval for task list while Automations view is mounted (ms). */
const AUTOMATIONS_POLL_MS = 15_000

let pollTimer: ReturnType<typeof setInterval> | null = null

export type AutomationTaskStatus =
  | 'pending'
  | 'dispatched'
  | 'agent_running'
  | 'agent_completed'
  | 'awaiting_review'
  | 'approved'
  | 'rejected'
  | 'failed'

/**
 * Wire projection of `CronSchedule` reused by automation tasks. Mirrors C# `AutomationScheduleWire`.
 * `kind` is one of `once` (not persisted — sentinel for "no schedule") | `every` | `at` | `daily`.
 */
export interface AutomationSchedule {
  kind: 'once' | 'every' | 'at' | 'daily' | string
  atMs?: number
  everyMs?: number
  initialDelayMs?: number
  dailyHour?: number
  dailyMinute?: number
  expr?: string
  tz?: string
}

export interface AutomationThreadBinding {
  threadId: string
  mode?: string
}

export interface AutomationTask {
  id: string
  title: string
  status: AutomationTaskStatus
  sourceName: string
  threadId: string | null
  description?: string
  agentSummary?: string | null
  /** Wire: `workspaceScope` (default) or `fullAuto`; legacy `autoApprove` / `default`. */
  approvalPolicy?: string | null
  createdAt: string
  updatedAt: string
  /** Optional recurring schedule; when absent the task is one-shot (legacy behavior). */
  schedule?: AutomationSchedule | null
  /** Optional binding to a pre-existing thread (turns submit directly into it). */
  threadBinding?: AutomationThreadBinding | null
  /** True = awaitingReview after run; false = skip review (loop on schedule). */
  requireApproval?: boolean | null
  /** ISO 8601 UTC. Null when the task has no schedule or is ready to dispatch immediately. */
  nextRunAt?: string | null
}

/**
 * Built-in local task template, fetched once per view mount and used as a preset source for the New Task dialog.
 */
export interface AutomationTemplate {
  id: string
  title: string
  description?: string
  icon?: string
  category?: string
  workflowMarkdown: string
  defaultSchedule?: AutomationSchedule | null
  defaultWorkspaceMode?: 'project' | 'isolated' | string | null
  defaultApprovalPolicy?: 'workspaceScope' | 'fullAuto' | string | null
  defaultRequireApproval?: boolean | null
  needsThreadBinding?: boolean | null
  defaultTitle?: string | null
  defaultDescription?: string | null
  /** True for user-authored templates (editable + deletable); false/absent for built-ins. */
  isUser?: boolean
  /** ISO-8601 UTC, only present for user templates. */
  createdAt?: string | null
  /** ISO-8601 UTC, only present for user templates. */
  updatedAt?: string | null
}

/** Authoring payload for creating / updating a user template. */
export interface SaveTemplateInput {
  /** Absent for new templates; present to update an existing user template in place. */
  id?: string
  title: string
  description?: string | null
  icon?: string | null
  category?: string | null
  workflowMarkdown: string
  defaultSchedule?: AutomationSchedule | null
  defaultWorkspaceMode?: 'project' | 'isolated' | null
  defaultApprovalPolicy?: 'workspaceScope' | 'fullAuto' | null
  defaultRequireApproval?: boolean
  needsThreadBinding?: boolean
  defaultTitle?: string | null
  defaultDescription?: string | null
}

export interface CreateTaskInput {
  title: string
  description: string
  workflowTemplate?: string
  approvalPolicy?: 'workspaceScope' | 'fullAuto'
  workspaceMode?: 'project' | 'isolated'
  schedule?: AutomationSchedule | null
  threadBinding?: AutomationThreadBinding | null
  requireApproval?: boolean
  templateId?: string
}

export type SourceFilter = 'all' | 'local' | 'github'

interface AutomationsState {
  tasks: AutomationTask[]
  loading: boolean
  error: string | null
  selectedTaskId: string | null
  filterSource: SourceFilter
  /** Cached built-in templates (lazy-loaded on first fetchTemplates call). */
  templates: AutomationTemplate[]
  templatesLoaded: boolean

  /** Full refresh (shows loading). Use for initial load and explicit user refresh. */
  fetchTasks(options?: { silent?: boolean }): Promise<void>
  /** Starts periodic silent refresh; call from AutomationsView on mount. */
  startPolling(): void
  /** Stops periodic refresh; call on unmount. */
  stopPolling(): void
  createTask(input: CreateTaskInput): Promise<void>
  approveTask(taskId: string, sourceName: string): Promise<void>
  rejectTask(taskId: string, sourceName: string, reason?: string): Promise<void>
  deleteTask(task: AutomationTask): Promise<void>
  /** Updates the task's thread binding. Pass null to unbind. */
  updateBinding(
    task: AutomationTask,
    binding: AutomationThreadBinding | null
  ): Promise<AutomationTask>
  /** Fetches and caches the built-in + user local templates. No-op if already loaded. */
  fetchTemplates(force?: boolean): Promise<void>
  /** Creates or updates a user-authored template. Refreshes the cached templates list. */
  saveTemplate(input: SaveTemplateInput): Promise<AutomationTemplate>
  /** Deletes a user-authored template. Built-in ids are rejected by the server. */
  deleteTemplate(id: string): Promise<void>
  selectTask(taskId: string | null): void
  setFilterSource(filter: SourceFilter): void
  upsertTask(task: AutomationTask): void
  removeTask(taskId: string, sourceName: string): void
}

export const useAutomationsStore = create<AutomationsState>((set, get) => ({
  tasks: [],
  loading: false,
  error: null,
  selectedTaskId: null,
  filterSource: 'all',
  templates: [],
  templatesLoaded: false,

  async fetchTasks(options?: { silent?: boolean }) {
    const silent = options?.silent === true
    if (!silent) set({ loading: true, error: null })
    try {
      const result = (await window.api.appServer.sendRequest('automation/task/list', {})) as {
        tasks?: AutomationTask[]
      }
      set({ tasks: result.tasks ?? [], loading: false })
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      if (!silent) set({ error: msg, loading: false })
      else set({ loading: false })
    }
  },

  startPolling() {
    if (pollTimer != null) return
    pollTimer = setInterval(() => {
      void useAutomationsStore.getState().fetchTasks({ silent: true })
    }, AUTOMATIONS_POLL_MS)
  },

  stopPolling() {
    if (pollTimer != null) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  },

  async createTask(input: CreateTaskInput) {
    const params: Record<string, unknown> = {
      title: input.title,
      description: input.description,
      approvalPolicy: input.approvalPolicy ?? 'workspaceScope',
      workspaceMode: input.workspaceMode ?? 'project'
    }
    if (input.workflowTemplate) params.workflowTemplate = input.workflowTemplate
    if (input.schedule && input.schedule.kind !== 'once') params.schedule = input.schedule
    if (input.threadBinding && input.threadBinding.threadId) params.threadBinding = input.threadBinding
    if (typeof input.requireApproval === 'boolean') params.requireApproval = input.requireApproval
    if (input.templateId) params.templateId = input.templateId
    await window.api.appServer.sendRequest('automation/task/create', params)
    await get().fetchTasks()
  },

  async approveTask(taskId: string, sourceName: string) {
    await window.api.appServer.sendRequest('automation/task/approve', { taskId, sourceName })
    await get().fetchTasks()
  },

  async rejectTask(taskId: string, sourceName: string, reason?: string) {
    const params: Record<string, unknown> = { taskId, sourceName }
    if (reason) params.reason = reason
    await window.api.appServer.sendRequest('automation/task/reject', params)
    await get().fetchTasks()
  },

  async updateBinding(task: AutomationTask, binding: AutomationThreadBinding | null) {
    const params: Record<string, unknown> = {
      taskId: task.id,
      sourceName: task.sourceName
    }
    if (binding && binding.threadId) {
      params.threadBinding = {
        threadId: binding.threadId,
        mode: binding.mode ?? 'run-in-thread'
      }
    }
    const result = (await window.api.appServer.sendRequest(
      'automation/task/updateBinding',
      params
    )) as { task?: AutomationTask }
    const updated = result.task ?? { ...task, threadBinding: binding }
    get().upsertTask(updated)
    return updated
  },

  async fetchTemplates(force?: boolean) {
    if (!force && get().templatesLoaded) return
    try {
      const result = (await window.api.appServer.sendRequest(
        'automation/template/list',
        {}
      )) as { templates?: AutomationTemplate[] }
      set({ templates: result.templates ?? [], templatesLoaded: true })
    } catch {
      set({ templates: [], templatesLoaded: true })
    }
  },

  async saveTemplate(input: SaveTemplateInput) {
    const params: Record<string, unknown> = {
      title: input.title,
      workflowMarkdown: input.workflowMarkdown,
      defaultRequireApproval: input.defaultRequireApproval ?? false,
      needsThreadBinding: input.needsThreadBinding ?? false
    }
    if (input.id) params.id = input.id
    if (input.description != null && input.description !== '')
      params.description = input.description
    if (input.icon != null && input.icon !== '') params.icon = input.icon
    if (input.category != null && input.category !== '') params.category = input.category
    if (input.defaultSchedule && input.defaultSchedule.kind !== 'once')
      params.defaultSchedule = input.defaultSchedule
    if (input.defaultWorkspaceMode) params.defaultWorkspaceMode = input.defaultWorkspaceMode
    if (input.defaultApprovalPolicy) params.defaultApprovalPolicy = input.defaultApprovalPolicy
    if (input.defaultTitle != null && input.defaultTitle !== '')
      params.defaultTitle = input.defaultTitle
    if (input.defaultDescription != null && input.defaultDescription !== '')
      params.defaultDescription = input.defaultDescription

    const result = (await window.api.appServer.sendRequest(
      'automation/template/save',
      params
    )) as { template?: AutomationTemplate }
    const saved = result.template
    if (!saved) throw new Error('Server did not return the saved template')

    set((state) => {
      const idx = state.templates.findIndex((t) => t.id === saved.id)
      if (idx >= 0) {
        const updated = [...state.templates]
        updated[idx] = saved
        return { templates: updated }
      }
      return { templates: [...state.templates, saved] }
    })
    return saved
  },

  async deleteTemplate(id: string) {
    await window.api.appServer.sendRequest('automation/template/delete', { id })
    set((state) => ({ templates: state.templates.filter((t) => t.id !== id) }))
  },

  async deleteTask(task: AutomationTask) {
    await window.api.appServer.sendRequest('automation/task/delete', {
      taskId: task.id,
      sourceName: task.sourceName
    })
    if (task.threadId) {
      try {
        await window.api.appServer.sendRequest('thread/delete', { threadId: task.threadId })
      } catch {
        // Thread may already be gone; task folder is already removed.
      }
    }
    get().removeTask(task.id, task.sourceName)
    if (get().selectedTaskId === task.id) {
      set({ selectedTaskId: null })
      useReviewPanelStore.getState().destroyReviewPanel()
    }
  },

  removeTask(taskId: string, sourceName: string) {
    set((state) => ({
      tasks: state.tasks.filter(
        (t) => !(t.id === taskId && t.sourceName === sourceName)
      )
    }))
  },

  selectTask(taskId: string | null) {
    set({ selectedTaskId: taskId })
  },

  setFilterSource(filter: SourceFilter) {
    set({ filterSource: filter })
  },

  upsertTask(task: AutomationTask) {
    set((state) => {
      const idx = state.tasks.findIndex(
        (t) => t.id === task.id && t.sourceName === task.sourceName
      )
      if (idx >= 0) {
        const updated = [...state.tasks]
        updated[idx] = { ...updated[idx], ...task }
        return { tasks: updated }
      }
      return { tasks: [task, ...state.tasks] }
    })
  }
}))
