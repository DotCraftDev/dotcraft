import { describe, expect, it, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { WorkspaceSetupWizard } from '../components/WorkspaceSetupWizard'
import { LocaleProvider } from '../contexts/LocaleContext'
import type { WorkspaceStatusPayload } from '../../preload/api.d'

const settingsGet = vi.fn()
const runSetup = vi.fn()
const listSetupModels = vi.fn()

function renderWizard(workspaceStatus: WorkspaceStatusPayload) {
  return render(
    <LocaleProvider>
      <WorkspaceSetupWizard
        workspacePath="E:\\Git\\dotcraft"
        workspaceStatus={workspaceStatus}
        onCancel={() => {}}
      />
    </LocaleProvider>
  )
}

async function openConfigStep(): Promise<void> {
  fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
}

describe('WorkspaceSetupWizard', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    runSetup.mockResolvedValue(undefined)
    listSetupModels.mockResolvedValue({ kind: 'unsupported' })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: settingsGet
        },
        workspace: {
          listSetupModels,
          runSetup
        }
      }
    })
  })

  it('syncs untouched fields when user config defaults arrive later', async () => {
    const initialStatus: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'E:\\Git\\dotcraft',
      hasUserConfig: false
    }
    const updatedStatus: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'E:\\Git\\dotcraft',
      hasUserConfig: true,
      userConfigDefaults: {
        language: 'English',
        endpoint: 'https://litellm.example/v1',
        model: 'gpt-4.1',
        apiKeyPresent: true
      }
    }

    const view = renderWizard(initialStatus)
    await openConfigStep()

    expect((screen.getByLabelText('API endpoint') as HTMLInputElement).value).toBe(
      'https://api.openai.com/v1'
    )

    view.rerender(
      <LocaleProvider>
        <WorkspaceSetupWizard
          workspacePath="E:\\Git\\dotcraft"
          workspaceStatus={updatedStatus}
          onCancel={() => {}}
        />
      </LocaleProvider>
    )

    await waitFor(() => {
      expect((screen.getByLabelText('API endpoint') as HTMLInputElement).value).toBe(
        'https://litellm.example/v1'
      )
    })

    expect((screen.getByLabelText('Model') as HTMLInputElement).value).toBe('gpt-4.1')
    expect((screen.getByLabelText('Language') as HTMLSelectElement).value).toBe('English')
    expect(
      screen.getAllByText(
        'Inherited from your user settings. Editing this value only overrides this workspace.'
      ).length
    ).toBe(3)
    expect(
      screen.getByText('Leave this blank to keep using the API key from your user settings.')
    ).toBeInTheDocument()
  })

  it('does not overwrite fields the user already edited', async () => {
    const initialStatus: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'E:\\Git\\dotcraft',
      hasUserConfig: true,
      userConfigDefaults: {
        language: 'English',
        endpoint: 'https://first.example/v1',
        model: 'gpt-4o-mini',
        apiKeyPresent: true
      }
    }
    const updatedStatus: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'E:\\Git\\dotcraft',
      hasUserConfig: true,
      userConfigDefaults: {
        language: 'Chinese',
        endpoint: 'https://second.example/v1',
        model: 'gpt-4.1',
        apiKeyPresent: true
      }
    }

    const view = renderWizard(initialStatus)
    await openConfigStep()

    fireEvent.change(screen.getByLabelText('API endpoint'), {
      target: { value: 'https://workspace-only.example/v1' }
    })

    view.rerender(
      <LocaleProvider>
        <WorkspaceSetupWizard
          workspacePath="E:\\Git\\dotcraft"
          workspaceStatus={updatedStatus}
          onCancel={() => {}}
        />
      </LocaleProvider>
    )

    await waitFor(() => {
      expect((screen.getByLabelText('Model') as HTMLInputElement).value).toBe('gpt-4.1')
    })

    expect((screen.getByLabelText('API endpoint') as HTMLInputElement).value).toBe(
      'https://workspace-only.example/v1'
    )
    expect((screen.getByLabelText('Language') as HTMLSelectElement).value).toBe('Chinese')
  })

  it('renders model dropdown when model list is available', async () => {
    listSetupModels.mockResolvedValue({
      kind: 'success',
      models: ['deepseek-chat', 'gpt-4.1']
    })
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'E:\\Git\\dotcraft',
      hasUserConfig: true,
      userConfigDefaults: {
        language: 'English',
        endpoint: 'https://example.com/v1',
        model: 'gpt-4o-mini',
        apiKeyPresent: true
      }
    }

    renderWizard(status)
    await openConfigStep()

    const modelControl = await waitFor(() => {
      const control = screen.getByLabelText('Model')
      expect(control.tagName).toBe('SELECT')
      return control
    })
    expect((modelControl as HTMLSelectElement).value).toBe('gpt-4o-mini')
    fireEvent.change(modelControl, { target: { value: 'gpt-4.1' } })
    expect((modelControl as HTMLSelectElement).value).toBe('gpt-4.1')
  })

  it('falls back to manual model input when model list is unsupported', async () => {
    listSetupModels.mockResolvedValue({ kind: 'unsupported' })
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'E:\\Git\\dotcraft',
      hasUserConfig: false
    }

    renderWizard(status)
    await openConfigStep()

    const modelControl = await waitFor(() => {
      const control = screen.getByLabelText('Model')
      expect(control.tagName).toBe('INPUT')
      return control
    })
    expect((modelControl as HTMLInputElement).value).toBe('gpt-4o-mini')
  })

  it('shows loading text while model list is loading', async () => {
    listSetupModels.mockReturnValue(new Promise(() => {}))
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'E:\\Git\\dotcraft',
      hasUserConfig: false
    }

    renderWizard(status)
    await openConfigStep()

    expect(screen.getByRole('status')).toHaveTextContent('Loading available models...')
  })

  it('falls back to manual model input when model list is missing-key', async () => {
    listSetupModels.mockResolvedValue({ kind: 'missing-key' })
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'E:\\Git\\dotcraft',
      hasUserConfig: true,
      userConfigDefaults: {
        language: 'English',
        endpoint: 'https://example.com/v1',
        model: 'gpt-4o-mini',
        apiKeyPresent: false
      }
    }

    renderWizard(status)
    await openConfigStep()

    const modelControl = await waitFor(() => {
      const control = screen.getByLabelText('Model')
      expect(control.tagName).toBe('INPUT')
      return control
    })
    expect((modelControl as HTMLInputElement).value).toBe('gpt-4o-mini')
  })
})
