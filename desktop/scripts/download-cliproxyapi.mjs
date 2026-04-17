#!/usr/bin/env node
import { mkdirSync, writeFileSync, chmodSync, copyFileSync, rmSync, existsSync, readdirSync, statSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { join, basename, dirname } from 'node:path'
import { tmpdir } from 'node:os'
import { execFileSync } from 'node:child_process'

const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)
const versionConfig = JSON.parse(readFileSync(join(__dirname, 'cliproxyapi-version.json'), 'utf8'))
const TARGET_DIR = join(process.cwd(), 'resources', 'bin')
const VERSION_MARKER_FILE = 'cliproxyapi.version'
const PLATFORM_MAP = {
  win32: 'windows',
  darwin: 'darwin',
  linux: 'linux'
}
const ARCH_MAP = {
  x64: 'amd64',
  arm64: 'arm64'
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function isRetryableHttpStatus(status) {
  return status === 403 || status === 429 || status >= 500
}

function assetFileName({ platform, arch }) {
  const pattern = versionConfig.assetNamePattern
  if (typeof pattern !== 'string' || !pattern) {
    throw new Error('cliproxyapi-version.json must define assetNamePattern')
  }
  const ext = process.platform === 'win32' ? 'zip' : 'tar.gz'
  const versionWithoutV = versionConfig.version.replace(/^v/, '')
  return pattern
    .replace('{versionWithoutV}', versionWithoutV)
    .replace('{platform}', platform)
    .replace('{arch}', arch)
    .replace('{ext}', ext)
}

function releaseDownloadUrl(assetName) {
  return `https://github.com/${versionConfig.ownerRepo}/releases/download/${versionConfig.version}/${assetName}`
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

function shouldForceDownload() {
  return process.argv.includes('--force') || process.env.CLIPROXYAPI_FORCE_DOWNLOAD === '1'
}

function getTargetPaths(exeName) {
  return {
    binaryPath: join(TARGET_DIR, exeName),
    versionPath: join(TARGET_DIR, VERSION_MARKER_FILE)
  }
}

function readInstalledVersion(versionPath) {
  if (!existsSync(versionPath)) {
    return null
  }

  return readFileSync(versionPath, 'utf8').trim() || null
}

function isDesiredBinaryAlreadyInstalled(binaryPath, versionPath) {
  return existsSync(binaryPath) && readInstalledVersion(versionPath) === versionConfig.version
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
  const delaysMs = [1000, 2000, 4000]
  let lastErr
  for (let attempt = 0; attempt <= delaysMs.length; attempt++) {
    try {
      const res = await fetch(url, {
        headers: {
          'User-Agent': 'dotcraft-desktop-build'
        }
      })
      if (res.ok) {
        const data = Buffer.from(await res.arrayBuffer())
        writeFileSync(outPath, data)
        return
      }
      const msg = `Failed to download asset (${res.status}): ${url}`
      if (!isRetryableHttpStatus(res.status)) {
        throw new Error(msg)
      }
      lastErr = new Error(msg)
    } catch (err) {
      if (err instanceof Error && err.message.startsWith('Failed to download asset (')) {
        const m = err.message.match(/Failed to download asset \((\d+)\)/)
        if (m && !isRetryableHttpStatus(parseInt(m[1], 10))) {
          throw err
        }
        lastErr = err
      } else {
        lastErr = err instanceof Error ? err : new Error(String(err))
      }
    }
    if (attempt < delaysMs.length) {
      await sleep(delaysMs[attempt])
    }
  }
  throw lastErr instanceof Error ? lastErr : new Error(String(lastErr))
}

function extractArchive(archivePath, outputDir) {
  if (archivePath.endsWith('.zip')) {
    if (process.platform === 'win32') {
      execFileSync('powershell', [
        '-NoProfile',
        '-Command',
        `Expand-Archive -LiteralPath '${archivePath.replace(/'/g, "''")}' -DestinationPath '${outputDir.replace(/'/g, "''")}' -Force`
      ], { stdio: 'inherit' })
    } else {
      execFileSync('unzip', ['-o', archivePath, '-d', outputDir], { stdio: 'inherit' })
    }
    return
  }
  execFileSync('tar', ['-xzf', archivePath, '-C', outputDir], { stdio: 'inherit' })
}


async function main() {
  const { platform, arch, exeName } = ensureSupported()
  const { binaryPath: targetPath, versionPath } = getTargetPaths(exeName)
  const forceDownload = shouldForceDownload()

  if (!forceDownload && isDesiredBinaryAlreadyInstalled(targetPath, versionPath)) {
    console.log(`[cliproxyapi] Reusing bundled ${exeName} at ${targetPath} (${versionConfig.version})`)
    return
  }

  const assetName = assetFileName({ platform, arch })
  const downloadUrl = releaseDownloadUrl(assetName)

  const workDir = join(tmpdir(), `dotcraft-cliproxy-${Date.now()}`)
  const archivePath = join(workDir, assetName)
  const extractDir = join(workDir, 'extract')
  mkdirSync(extractDir, { recursive: true })

  try {
    console.log(`[cliproxyapi] Downloading ${assetName} for ${versionConfig.version}`)
    await downloadFile(downloadUrl, archivePath)
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
    copyFileSync(binaryPath, targetPath)
    if (process.platform !== 'win32') {
      chmodSync(targetPath, 0o755)
    }
    writeFileSync(versionPath, `${versionConfig.version}\n`)
    console.log(`[cliproxyapi] Installed ${basename(binaryPath)} -> ${targetPath}`)
    console.log(`[cliproxyapi] Recorded version marker at ${versionPath}`)
  } finally {
    if (existsSync(workDir)) {
      rmSync(workDir, { recursive: true, force: true })
    }
  }
}

main().catch((err) => {
  console.error('[cliproxyapi] Download failed:', err instanceof Error ? err.message : String(err))
  process.exit(1)
})
