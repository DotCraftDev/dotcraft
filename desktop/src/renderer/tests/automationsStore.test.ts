import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAutomationsStore } from '../stores/automationsStore'

describe('automationsStore templates', () => {
  const sendRequest = vi.fn()

  beforeEach(() => {
    sendRequest.mockReset()
    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: {}
    })
    Object.defineProperty(globalThis.window, 'api', {
      configurable: true,
      value: {
        appServer: {
          sendRequest
        }
      }
    })

    useAutomationsStore.setState({
      tasks: [],
      loading: false,
      error: null,
      selectedTaskId: null,
      statusFilter: 'all',
      templates: [],
      templatesLoaded: false,
      templatesLocale: undefined
    })
  })

  it('passes the requested locale when fetching templates', async () => {
    sendRequest.mockResolvedValueOnce({
      templates: [
        {
          id: 'scan-commits-for-bugs',
          title: '扫描近期提交中的潜在缺陷',
          workflowMarkdown: '---\n---'
        }
      ]
    })

    await useAutomationsStore.getState().fetchTemplates('zh-Hans')

    expect(sendRequest).toHaveBeenCalledWith('automation/template/list', {
      locale: 'zh-Hans'
    })
    expect(useAutomationsStore.getState().templatesLocale).toBe('zh-Hans')
    expect(useAutomationsStore.getState().templates[0]?.title).toBe(
      '扫描近期提交中的潜在缺陷'
    )
  })

  it('refetches when locale changes but reuses the same-locale cache', async () => {
    sendRequest
      .mockResolvedValueOnce({
        templates: [{ id: 'weekly-report', title: 'Weekly activity report', workflowMarkdown: '' }]
      })
      .mockResolvedValueOnce({
        templates: [{ id: 'weekly-report', title: '每周活动报告', workflowMarkdown: '' }]
      })

    await useAutomationsStore.getState().fetchTemplates('en')
    await useAutomationsStore.getState().fetchTemplates('en')
    await useAutomationsStore.getState().fetchTemplates('zh-Hans')

    expect(sendRequest).toHaveBeenCalledTimes(2)
    expect(sendRequest).toHaveBeenNthCalledWith(1, 'automation/template/list', {
      locale: 'en'
    })
    expect(sendRequest).toHaveBeenNthCalledWith(2, 'automation/template/list', {
      locale: 'zh-Hans'
    })
    expect(useAutomationsStore.getState().templates[0]?.title).toBe('每周活动报告')
  })

  it('does not send task-level review fields when creating tasks', async () => {
    sendRequest.mockResolvedValueOnce({}).mockResolvedValueOnce({ tasks: [] })

    await useAutomationsStore.getState().createTask({
      title: 'Ship cleanup',
      description: 'Remove stale review gates',
      approvalPolicy: 'workspaceScope',
      workspaceMode: 'project'
    })

    expect(sendRequest).toHaveBeenNthCalledWith(1, 'automation/task/create', {
      title: 'Ship cleanup',
      description: 'Remove stale review gates',
      approvalPolicy: 'workspaceScope',
      workspaceMode: 'project'
    })
    expect(sendRequest.mock.calls[0][1]).not.toHaveProperty('requireApproval')
  })

  it('does not expose approve or reject task actions', () => {
    const state = useAutomationsStore.getState() as unknown as Record<string, unknown>

    expect('approveTask' in state).toBe(false)
    expect('rejectTask' in state).toBe(false)
  })

  it('does not send template-level default review fields when saving templates', async () => {
    sendRequest.mockResolvedValueOnce({
      template: {
        id: 'cleanup',
        title: 'Cleanup',
        workflowMarkdown: '---\n---'
      }
    })

    await useAutomationsStore.getState().saveTemplate({
      title: 'Cleanup',
      workflowMarkdown: '---\n---',
      defaultApprovalPolicy: 'workspaceScope',
      needsThreadBinding: false
    })

    expect(sendRequest).toHaveBeenCalledWith('automation/template/save', {
      title: 'Cleanup',
      workflowMarkdown: '---\n---',
      needsThreadBinding: false,
      defaultApprovalPolicy: 'workspaceScope'
    })
    expect(sendRequest.mock.calls[0][1]).not.toHaveProperty('defaultRequireApproval')
  })
})
