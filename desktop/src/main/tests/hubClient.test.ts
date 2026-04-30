import { beforeEach, describe, expect, it, vi } from 'vitest'
import { findSseBoundary, HubClient } from '../HubClient'

const fsMocks = vi.hoisted(() => ({
  existsSync: vi.fn(() => true),
  readFileSync: vi.fn(() => JSON.stringify({
    pid: 1234,
    apiBaseUrl: 'http://127.0.0.1:8123',
    token: 'hub-token',
    startedAt: '',
    version: ''
  }))
}))

vi.mock('fs', () => fsMocks)
vi.mock('os', () => ({
  homedir: () => 'C:/Users/test'
}))

describe('HubClient SSE parsing', () => {
  it('detects LF and CRLF event boundaries', () => {
    expect(findSseBoundary('data: {}\n\n')?.sequence).toBe('\n\n')
    expect(findSseBoundary('data: {}\r\n\r\n')?.sequence).toBe('\r\n\r\n')
  })
})

describe('HubClient AppServer management', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    fsMocks.existsSync.mockReturnValue(true)
    fsMocks.readFileSync.mockReturnValue(JSON.stringify({
      pid: 1234,
      apiBaseUrl: 'http://127.0.0.1:8123',
      token: 'hub-token',
      startedAt: '',
      version: ''
    }))
    vi.spyOn(process, 'kill').mockImplementation(() => true)
  })

  it('sends APIProxy sidecar options when ensuring AppServer', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce({ ok: true })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          workspacePath: 'E:/repo',
          canonicalWorkspacePath: 'E:/repo',
          state: 'running',
          endpoints: {
            appServerWebSocket: 'ws://127.0.0.1:9000/ws',
            apiProxy: 'http://127.0.0.1:8317/v1'
          },
          serviceStatus: {
            apiProxy: { state: 'running', url: 'http://127.0.0.1:8317/v1' }
          },
          startedByHub: true
        })
      })
    vi.stubGlobal('fetch', fetchMock)

    await new HubClient().ensureAppServer('E:/repo', {
      apiProxy: {
        enabled: true,
        binaryPath: 'E:/bin/cliproxyapi.exe',
        configPath: 'C:/Users/test/proxy/config.yaml',
        endpoint: 'http://127.0.0.1:8317/v1',
        apiKey: 'proxy-key'
      }
    })

    const ensureInit = fetchMock.mock.calls[1][1] as RequestInit
    expect(JSON.parse(String(ensureInit.body))).toMatchObject({
      workspacePath: 'E:/repo',
      apiProxy: {
        enabled: true,
        binaryPath: 'E:/bin/cliproxyapi.exe',
        configPath: 'C:/Users/test/proxy/config.yaml',
        endpoint: 'http://127.0.0.1:8317/v1',
        apiKey: 'proxy-key'
      }
    })
  })
})
