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
})
