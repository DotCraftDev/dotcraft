#!/usr/bin/env node
import fs from 'node:fs/promises'
import path from 'node:path'
import { createRequire } from 'node:module'
import { pathToFileURL } from 'node:url'

function parseArgs(argv) {
  const args = {
    out: 'references/svg-preview',
    title: 'SVG Preview',
    sizes: [16, 20, 32, 48, 64],
    files: []
  }

  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i]
    if (arg === '--out') {
      args.out = argv[++i]
    } else if (arg === '--title') {
      args.title = argv[++i]
    } else if (arg === '--sizes') {
      args.sizes = argv[++i].split(',').map((value) => Number(value.trim())).filter(Boolean)
    } else {
      args.files.push(arg)
    }
  }

  if (args.files.length === 0) {
    throw new Error('Provide at least one SVG file path.')
  }
  return args
}

function escapeHtml(value) {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

function buildHtml({ title, sizes, files }) {
  const rows = files.map((file) => {
    const abs = path.resolve(file)
    const src = pathToFileURL(abs).href
    const name = path.basename(file)
    const previews = sizes
      .map((size) => `<img src="${src}" style="width:${size}px;height:${size}px" alt="">`)
      .join('')
    return `
      <section class="row">
        <div class="previews">${previews}</div>
        <div class="meta">
          <div class="name">${escapeHtml(name)}</div>
          <div class="path">${escapeHtml(abs)}</div>
        </div>
      </section>`
  }).join('')

  return `<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8">
    <title>${escapeHtml(title)}</title>
    <style>
      :root { color-scheme: dark; font-family: Inter, "Segoe UI", sans-serif; background: #101010; color: #f3f3f3; }
      body { margin: 0; padding: 28px; background: #101010; }
      .wrap { max-width: 980px; }
      h1 { margin: 0 0 18px; font-size: 18px; line-height: 1.25; }
      .row { display: flex; align-items: center; gap: 18px; padding: 14px 0; border-bottom: 1px solid #262626; }
      .previews { display: flex; align-items: center; gap: 14px; min-width: 260px; }
      .previews img { object-fit: contain; image-rendering: auto; }
      .meta { min-width: 0; }
      .name { font-size: 13px; font-weight: 700; }
      .path { margin-top: 4px; max-width: 650px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: #a6a6a6; font-size: 11px; }
      .swatches { display: grid; grid-template-columns: 1fr 1fr; margin-top: 24px; border: 1px solid #262626; border-radius: 10px; overflow: hidden; }
      .swatch { padding: 18px; min-height: 70px; display: flex; gap: 14px; align-items: center; }
      .light { background: #f7f7f7; color: #111; }
      .dark { background: #161616; color: #eee; }
      .swatch img { width: 32px; height: 32px; }
    </style>
  </head>
  <body>
    <main class="wrap">
      <h1>${escapeHtml(title)}</h1>
      ${rows}
      <div class="swatches">
        <div class="swatch dark">${files.map((file) => `<img src="${pathToFileURL(path.resolve(file)).href}" alt="">`).join('')}</div>
        <div class="swatch light">${files.map((file) => `<img src="${pathToFileURL(path.resolve(file)).href}" alt="">`).join('')}</div>
      </div>
    </main>
  </body>
</html>`
}

async function main() {
  const args = parseArgs(process.argv.slice(2))
  await fs.mkdir(args.out, { recursive: true })
  const htmlPath = path.join(args.out, 'preview.html')
  const pngPath = path.join(args.out, 'preview.png')
  await fs.writeFile(htmlPath, buildHtml(args), 'utf8')

  const requireFromCwd = createRequire(path.join(process.cwd(), 'package.json'))
  const { chromium } = requireFromCwd('playwright')
  const browser = await chromium.launch({ channel: 'msedge', headless: true })
  const page = await browser.newPage({ viewport: { width: 1040, height: 720 }, deviceScaleFactor: 1 })
  await page.goto(pathToFileURL(path.resolve(htmlPath)).href)
  await page.screenshot({ path: pngPath, fullPage: true })
  await browser.close()

  console.log(`HTML: ${htmlPath}`)
  console.log(`PNG:  ${pngPath}`)
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error))
  process.exit(1)
})
