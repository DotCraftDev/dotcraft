import { afterEach, describe, expect, it } from 'vitest'
import { mkdir, mkdtemp, rm, writeFile } from 'fs/promises'
import { existsSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { cleanupWorkspaceCache } from '../workspaceComposerIpc'

describe('workspace composer cache cleanup', () => {
  let tempRoot = ''

  afterEach(async () => {
    if (tempRoot) await rm(tempRoot, { recursive: true, force: true })
    tempRoot = ''
  })

  it('removes invalid known cache files', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-workspace-cache-'))
    const cacheDir = join(tempRoot, '.craft', 'cache')
    await mkdir(cacheDir, { recursive: true })
    const fileIndex = join(cacheDir, 'desktop-file-index-v1.json')
    const suggestions = join(cacheDir, 'welcome-suggestions.json')
    await writeFile(fileIndex, '{"schemaVersion":999}', 'utf8')
    await writeFile(suggestions, '{"schemaVersion":999}', 'utf8')

    await cleanupWorkspaceCache(tempRoot)

    expect(existsSync(fileIndex)).toBe(false)
    expect(existsSync(suggestions)).toBe(false)
  })
})
