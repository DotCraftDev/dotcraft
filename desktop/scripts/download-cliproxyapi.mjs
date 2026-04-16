#!/usr/bin/env node
import { mkdirSync, writeFileSync, chmodSync, copyFileSync, rmSync, existsSync, readdirSync, statSync } from 'node:fs'
import { join, basename } from 'node:path'
import { tmpdir } from 'node:os'
import { execFileSync } from 'node:child_process'

const REPO_API = 'https://api.github.com/repos/router-for-me/CLIProxyAPI/releases/latest'
const TARGET_DIR = join(process.cwd(), 'resources', 'bin')
const PLATFORM_MAP = {
  win32: 'windows',
  darwin: 'darwin',
  linux: 'linux'
}
const ARCH_MAP = {
  x64: 'amd64',
  arm64: 'arm64'
}

function ensureSupported() {
  const platform = PLATFORM_MAP[process.platform]
  const arch = ARCH_MAP[process.arch]
  if (!platform || !arch) {
    throw new Error(`Unsupported platform/arch: ${process.platform}/${process.arch}`)
  }
  return {
    platform,
    arch,
    exeName: process.platform === 'win32' ? 'cliproxyapi.exe' : 'cliproxyapi'
  }
}

function recursiveFindBinary(dirPath, names) {
  const entries = readdirSync(dirPath)
  for (const entry of entries) {
    const fullPath = join(dirPath, entry)
    const st = statSync(fullPath)
    if (st.isDirectory()) {
      const found = recursiveFindBinary(fullPath, names)
      if (found) return found
      continue
    }
    if (names.has(entry.toLowerCase())) {
      return fullPath
    }
  }
  return null
}

async function downloadFile(url, outPath) {
  const res = await fetch(url, {
    headers: {
      'User-Agent': 'dotcraft-desktop-build'
    }
  })
  if (!res.ok) {
    throw new Error(`Failed to download asset (${res.status}): ${url}`)
  }
  const data = Buffer.from(await res.arrayBuffer())
  writeFileSync(outPath, data)
}

function extractArchive(archivePath, outputDir) {
  if (archivePath.endsWith('.zip')) {
    execFileSync('powershell', [
      '-NoProfile',
      '-Command',
      `Expand-Archive -LiteralPath '${archivePath}' -DestinationPath '${outputDir}' -Force`
    ], { stdio: 'inherit' })
    return
  }
  execFileSync('tar', ['-xzf', archivePath, '-C', outputDir], { stdio: 'inherit' })
}

async function main() {
  const { platform, arch, exeName } = ensureSupported()
  const suffix = `_${platform}_${arch}`

  const releaseRes = await fetch(REPO_API, {
    headers: {
      'User-Agent': 'dotcraft-desktop-build'
    }
  })
  if (!releaseRes.ok) {
    throw new Error(`Failed to query latest CLIProxyAPI release (${releaseRes.status})`)
  }
  const release = await releaseRes.json()
  const assets = Array.isArray(release.assets) ? release.assets : []
  const asset = assets.find((item) => typeof item?.name === 'string' && item.name.includes(suffix) && (item.name.endsWith('.zip') || item.name.endsWith('.tar.gz')))
  if (!asset?.browser_download_url || !asset?.name) {
    throw new Error(`Could not find release asset matching ${suffix}`)
  }

  const workDir = join(tmpdir(), `dotcraft-cliproxy-${Date.now()}`)
  const archivePath = join(workDir, asset.name)
  const extractDir = join(workDir, 'extract')
  mkdirSync(extractDir, { recursive: true })

  console.log(`[cliproxyapi] Downloading ${asset.name}`)
  await downloadFile(asset.browser_download_url, archivePath)
  console.log('[cliproxyapi] Extracting archive')
  extractArchive(archivePath, extractDir)

  const candidates = new Set(
    process.platform === 'win32'
      ? ['cliproxyapi.exe', 'cli-proxy-api.exe']
      : ['cliproxyapi', 'cli-proxy-api']
  )
  const binaryPath = recursiveFindBinary(extractDir, candidates)
  if (!binaryPath) {
    throw new Error('Could not find CLIProxyAPI executable in extracted archive')
  }

  mkdirSync(TARGET_DIR, { recursive: true })
  const targetPath = join(TARGET_DIR, exeName)
  copyFileSync(binaryPath, targetPath)
  if (process.platform !== 'win32') {
    chmodSync(targetPath, 0o755)
  }
  console.log(`[cliproxyapi] Installed ${basename(binaryPath)} -> ${targetPath}`)

  if (existsSync(workDir)) {
    rmSync(workDir, { recursive: true, force: true })
  }
}

main().catch((err) => {
  console.error('[cliproxyapi] Download failed:', err instanceof Error ? err.message : String(err))
  process.exit(1)
})
