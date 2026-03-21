import { describe, it, expect, beforeEach } from 'vitest'
import { createServerRequestBridge } from '../ipcBridge'

// ---------------------------------------------------------------------------
// ipcBridge — server-request bridge tests
//
// The bridge creates a pending Promise per request (identified by bridgeId),
// which resolves when the Renderer sends back a response via
// appserver:server-response. These tests verify the pending-map logic directly
// (without standing up a real Electron IPC environment).
// ---------------------------------------------------------------------------

describe('createServerRequestBridge', () => {
  it('returns a unique bridgeId for each call', () => {
    const a = createServerRequestBridge()
    const b = createServerRequestBridge()
    expect(a.bridgeId).not.toBe(b.bridgeId)
  })

  it('returns a promise that is pending until resolved externally', async () => {
    const { promise } = createServerRequestBridge()
    let settled = false
    void promise.then(() => { settled = true })
    await new Promise((r) => setTimeout(r, 10))
    expect(settled).toBe(false)
  })

  it('bridge IDs are numeric strings in ascending order', () => {
    const ids = [
      createServerRequestBridge().bridgeId,
      createServerRequestBridge().bridgeId,
      createServerRequestBridge().bridgeId
    ]
    const nums = ids.map(Number)
    expect(nums[0]).toBeLessThan(nums[1])
    expect(nums[1]).toBeLessThan(nums[2])
  })
})

// ---------------------------------------------------------------------------
// WireProtocolClient — bidirectional request routing
// (covered in WireProtocolClient.test.ts, but verified here as integration)
// ---------------------------------------------------------------------------

import { Readable, Writable, PassThrough } from 'stream'
import { WireProtocolClient } from '../WireProtocolClient'

describe('WireProtocolClient bidirectional routing', () => {
  it('server request handler result is sent back as JSON-RPC response with original id', async () => {
    const toServer = new PassThrough()
    const fromServer = new PassThrough()
    const client = new WireProtocolClient(
      fromServer as unknown as Readable,
      toServer as unknown as Writable
    )

    const responseLines: string[] = []
    toServer.on('data', (chunk: Buffer) => {
      chunk.toString('utf8').split('\n').filter(Boolean).forEach((l) => responseLines.push(l))
    })

    // Register a handler that simulates the approval bridge: returns the decision
    client.onServerRequest(async (_method, params) => {
      const p = params as Record<string, unknown>
      return { decision: p.defaultDecision ?? 'accept' }
    })

    // AppServer sends a server-initiated request
    fromServer.push(
      JSON.stringify({
        jsonrpc: '2.0',
        id: 42,
        method: 'item/approval/request',
        params: { approvalType: 'shell', operation: 'rm -rf /tmp', defaultDecision: 'decline' }
      }) + '\n'
    )

    await new Promise((r) => setTimeout(r, 20))

    // Filter out any initialize or other requests from the response lines
    const approvalResponse = responseLines
      .map((l) => JSON.parse(l))
      .find((m) => m.id === 42 && 'result' in m)

    expect(approvalResponse).toBeDefined()
    expect(approvalResponse).toMatchObject({
      jsonrpc: '2.0',
      id: 42,
      result: { decision: 'decline' }
    })

    client.dispose()
    toServer.destroy()
    fromServer.destroy()
  })

  it('sends an error response when handler throws', async () => {
    const toServer = new PassThrough()
    const fromServer = new PassThrough()
    const client = new WireProtocolClient(
      fromServer as unknown as Readable,
      toServer as unknown as Writable
    )

    const responseLines: string[] = []
    toServer.on('data', (chunk: Buffer) => {
      chunk.toString('utf8').split('\n').filter(Boolean).forEach((l) => responseLines.push(l))
    })

    client.onServerRequest(async () => {
      throw new Error('Bridge unavailable')
    })

    fromServer.push(
      JSON.stringify({ jsonrpc: '2.0', id: 77, method: 'item/approval/request', params: {} }) + '\n'
    )

    await new Promise((r) => setTimeout(r, 20))

    const errorResponse = responseLines
      .map((l) => JSON.parse(l))
      .find((m) => m.id === 77 && 'error' in m)

    expect(errorResponse).toBeDefined()
    expect(errorResponse.error.code).toBe(-32603)

    client.dispose()
    toServer.destroy()
    fromServer.destroy()
  })
})
