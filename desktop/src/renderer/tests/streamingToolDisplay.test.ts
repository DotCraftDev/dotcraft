import { describe, expect, it } from 'vitest'
import {
  BUILTIN_TOOLS,
  extractPartialJsonStringValue,
  getStreamingToolDisplay,
  isBuiltinTool
} from '../utils/toolCallDisplay'

describe('getStreamingToolDisplay', () => {
  it('returns generic external placeholder for MCP-style tool names', () => {
    const display = getStreamingToolDisplay(
      'acme_mcp_tool',
      '{"query":"secret"',
      'en'
    )
    expect(display.label).toBe('Generating parameters for acme_mcp_tool...')
    expect(display.parsedPreview).toBeUndefined()
  })

  it('renders WriteFile streaming label with parsed path', () => {
    const display = getStreamingToolDisplay(
      'WriteFile',
      '{"path":"src/demo.rs","content":"let x',
      'en'
    )
    expect(display.label).toBe('Writing to demo.rs...')
    expect(display.parsedPreview?.path).toBe('src/demo.rs')
    expect(display.parsedPreview?.content).toBe('let x')
  })

  it('renders EditFile streaming label using newText', () => {
    const display = getStreamingToolDisplay(
      'EditFile',
      '{"path":"a/b.ts","oldText":"foo","newText":"bar',
      'en'
    )
    expect(display.label).toBe('Editing b.ts...')
    expect(display.parsedPreview?.content).toBe('bar')
  })

  it('renders Exec streaming label with command first line', () => {
    const display = getStreamingToolDisplay('Exec', '{"command":"npm test', 'en')
    expect(display.label).toBe('Running: npm test')
  })

  it('renders GrepFiles streaming label with pattern and path', () => {
    const display = getStreamingToolDisplay(
      'GrepFiles',
      '{"pattern":"TODO","path":"src',
      'en'
    )
    expect(display.label).toBe('Searching "TODO" in src...')
  })

  it('renders CreatePlan streaming label with title and exposes draft preview', () => {
    const display = getStreamingToolDisplay(
      'CreatePlan',
      '{"title":"Ship feature X","overview":"Not yet',
      'en'
    )
    expect(display.label).toBe('Drafting plan: Ship feature X...')
    expect(display.parsedPreview?.planDraft?.title).toBe('Ship feature X')
    expect(display.parsedPreview?.planDraft?.overview).toBe('Not yet')
  })

  it('falls back to generic draft label when title is missing', () => {
    const display = getStreamingToolDisplay('CreatePlan', '{"overview":', 'en')
    expect(display.label).toBe('Drafting plan...')
  })

  it('renders WebSearch streaming label', () => {
    const display = getStreamingToolDisplay('WebSearch', '{"query":"rust streams', 'en')
    expect(display.label).toBe('Searching the web for "rust streams"...')
  })

  it('renders SpawnAgent streaming label from the new argument names', () => {
    const display = getStreamingToolDisplay(
      'SpawnAgent',
      '{"prompt":"Build tests","agentNickname":"tester","profile":"native"',
      'en'
    )
    expect(display.label).toBe('Spawning agent: tester...')
  })

  it('renders generic builtin label for recognised but unsupported tool', () => {
    const display = getStreamingToolDisplay('CommitSuggest', '{}', 'en')
    expect(display.label).toBe('Preparing commit message...')
  })

  it('honours zh-Hans locale for streaming labels', () => {
    const display = getStreamingToolDisplay('WebFetch', '{"url":"https://a', 'zh-Hans')
    expect(display.label).toBe('正在获取 https://a...')
  })
})

describe('isBuiltinTool / BUILTIN_TOOLS', () => {
  it('recognises PascalCase built-in tool names', () => {
    expect(isBuiltinTool('ReadFile')).toBe(true)
    expect(isBuiltinTool('CreatePlan')).toBe(true)
    expect(isBuiltinTool('SpawnAgent')).toBe(true)
    expect(isBuiltinTool('acme_mcp_tool')).toBe(false)
  })

  it('exposes a non-empty BUILTIN_TOOLS set', () => {
    expect(BUILTIN_TOOLS.size).toBeGreaterThan(5)
    expect(BUILTIN_TOOLS.has('WriteFile')).toBe(true)
  })
})

describe('extractPartialJsonStringValue', () => {
  it('returns unterminated string value when delta is mid-stream', () => {
    expect(extractPartialJsonStringValue('{"path":"src/main.rs","content":"hel', 'path'))
      .toBe('src/main.rs')
    expect(extractPartialJsonStringValue('{"path":"src/main.rs","content":"hel', 'content'))
      .toBe('hel')
  })

  it('returns null when key is missing', () => {
    expect(extractPartialJsonStringValue('{"path":"a"}', 'content')).toBeNull()
  })
})
