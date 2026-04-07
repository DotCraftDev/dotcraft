import { useCallback, useMemo, useState } from 'react'
import type { ChannelId } from './channelDefs'

export interface QQChannelConfig {
  Enabled: boolean
  Host: string
  Port: number
  AccessToken: string
}

export interface WeComRobotConfig {
  Path: string
  Token: string
  AesKey: string
}

export interface WeComChannelConfig {
  Enabled: boolean
  Host: string
  Port: number
  Robots: WeComRobotConfig[]
}

export interface WeixinChannelConfig {
  enabled: boolean
  transport: 'websocket'
}

export interface TelegramChannelConfig {
  enabled: boolean
  transport: 'subprocess'
  command: string
  args: string[]
  workingDirectory?: string
  env: Record<string, string>
}

export interface ChannelsConfigState {
  qq: QQChannelConfig
  wecom: WeComChannelConfig
  weixin: WeixinChannelConfig
  telegram: TelegramChannelConfig
}

const DEFAULT_CONFIGS: ChannelsConfigState = {
  qq: { Enabled: false, Host: '127.0.0.1', Port: 6700, AccessToken: '' },
  wecom: { Enabled: false, Host: '0.0.0.0', Port: 9000, Robots: [] },
  weixin: { enabled: false, transport: 'websocket' },
  telegram: {
    enabled: false,
    transport: 'subprocess',
    command: 'python',
    args: ['-m', 'dotcraft_telegram'],
    workingDirectory: '',
    env: { TELEGRAM_BOT_TOKEN: '' }
  }
}

function configPath(workspacePath: string): string {
  return `${workspacePath.replace(/[\\/]+$/, '')}/.craft/config.json`
}

function isMissingFileError(err: unknown): boolean {
  const message = err instanceof Error ? err.message : String(err)
  return message.includes('ENOENT') || message.includes('not found')
}

function parseJson(text: string): Record<string, unknown> {
  if (!text.trim()) return {}
  const parsed = JSON.parse(text) as unknown
  if (parsed == null || typeof parsed !== 'object' || Array.isArray(parsed)) return {}
  return parsed as Record<string, unknown>
}

function asRecord(v: unknown): Record<string, unknown> {
  if (v == null || typeof v !== 'object' || Array.isArray(v)) return {}
  return v as Record<string, unknown>
}

function asString(v: unknown, fallback = ''): string {
  return typeof v === 'string' ? v : fallback
}

function asNumber(v: unknown, fallback: number): number {
  return typeof v === 'number' && Number.isFinite(v) ? v : fallback
}

function asBoolean(v: unknown, fallback = false): boolean {
  return typeof v === 'boolean' ? v : fallback
}

function parseStringArray(v: unknown): string[] {
  if (!Array.isArray(v)) return []
  return v.filter((item): item is string => typeof item === 'string')
}

function parseConfigs(root: Record<string, unknown>): ChannelsConfigState {
  const qq = asRecord(root.QQBot)
  const wecom = asRecord(root.WeComBot)
  const ext = asRecord(root.ExternalChannels)
  const weixin = asRecord(ext.weixin)
  const telegram = asRecord(ext.telegram)
  const telegramEnv = asRecord(telegram.env)
  const robotsRaw = Array.isArray(wecom.Robots) ? wecom.Robots : []
  const robots: WeComRobotConfig[] = robotsRaw
    .map((r) => asRecord(r))
    .map((r) => ({
      Path: asString(r.Path),
      Token: asString(r.Token),
      AesKey: asString(r.AesKey)
    }))

  return {
    qq: {
      Enabled: asBoolean(qq.Enabled, DEFAULT_CONFIGS.qq.Enabled),
      Host: asString(qq.Host, DEFAULT_CONFIGS.qq.Host),
      Port: asNumber(qq.Port, DEFAULT_CONFIGS.qq.Port),
      AccessToken: asString(qq.AccessToken, DEFAULT_CONFIGS.qq.AccessToken)
    },
    wecom: {
      Enabled: asBoolean(wecom.Enabled, DEFAULT_CONFIGS.wecom.Enabled),
      Host: asString(wecom.Host, DEFAULT_CONFIGS.wecom.Host),
      Port: asNumber(wecom.Port, DEFAULT_CONFIGS.wecom.Port),
      Robots: robots
    },
    weixin: {
      enabled: asBoolean(weixin.enabled, DEFAULT_CONFIGS.weixin.enabled),
      transport: 'websocket'
    },
    telegram: {
      enabled: asBoolean(telegram.enabled, DEFAULT_CONFIGS.telegram.enabled),
      transport: 'subprocess',
      command: asString(telegram.command, DEFAULT_CONFIGS.telegram.command),
      args: parseStringArray(telegram.args).length ? parseStringArray(telegram.args) : DEFAULT_CONFIGS.telegram.args,
      workingDirectory: asString(telegram.workingDirectory),
      env: {
        TELEGRAM_BOT_TOKEN: asString(
          telegramEnv.TELEGRAM_BOT_TOKEN,
          DEFAULT_CONFIGS.telegram.env.TELEGRAM_BOT_TOKEN
        )
      }
    }
  }
}

async function readRoot(workspacePath: string): Promise<Record<string, unknown>> {
  const path = configPath(workspacePath)
  try {
    const raw = await window.api.file.readFile(path)
    return parseJson(raw)
  } catch (err) {
    if (isMissingFileError(err)) return {}
    throw err
  }
}

async function writeRoot(workspacePath: string, root: Record<string, unknown>): Promise<void> {
  const path = configPath(workspacePath)
  await window.api.file.writeFile(path, `${JSON.stringify(root, null, 2)}\n`)
}

function mergeChannelConfig(
  root: Record<string, unknown>,
  channelId: ChannelId,
  config: ChannelsConfigState
): Record<string, unknown> {
  const next = { ...root }

  if (channelId === 'qq') {
    next.QQBot = {
      ...asRecord(next.QQBot),
      ...config.qq
    }
    return next
  }

  if (channelId === 'wecom') {
    next.WeComBot = {
      ...asRecord(next.WeComBot),
      ...config.wecom
    }
    return next
  }

  const ext = { ...asRecord(next.ExternalChannels) }
  if (channelId === 'weixin') {
    ext.weixin = {
      ...asRecord(ext.weixin),
      ...config.weixin,
      transport: 'websocket'
    }
  } else if (channelId === 'telegram') {
    ext.telegram = {
      ...asRecord(ext.telegram),
      ...config.telegram,
      transport: 'subprocess'
    }
  }
  next.ExternalChannels = ext
  return next
}

export function useChannelConfig(workspacePath: string): {
  loading: boolean
  savingChannelId: ChannelId | null
  error: string | null
  config: ChannelsConfigState
  setChannelConfig: (channelId: ChannelId, nextValue: unknown) => void
  reload: () => Promise<void>
  saveChannel: (channelId: ChannelId) => Promise<void>
} {
  const [config, setConfig] = useState<ChannelsConfigState>(DEFAULT_CONFIGS)
  const [loading, setLoading] = useState(true)
  const [savingChannelId, setSavingChannelId] = useState<ChannelId | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    if (!workspacePath) return
    setLoading(true)
    setError(null)
    try {
      const root = await readRoot(workspacePath)
      setConfig(parseConfigs(root))
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setLoading(false)
    }
  }, [workspacePath])

  const setChannelConfig = useCallback((channelId: ChannelId, nextValue: unknown) => {
    setConfig((prev) => ({
      ...prev,
      [channelId]: nextValue
    }))
  }, [])

  const saveChannel = useCallback(
    async (channelId: ChannelId) => {
      if (!workspacePath) return
      setSavingChannelId(channelId)
      setError(null)
      try {
        const root = await readRoot(workspacePath)
        const nextRoot = mergeChannelConfig(root, channelId, config)
        await writeRoot(workspacePath, nextRoot)
      } catch (err) {
        setError(err instanceof Error ? err.message : String(err))
        throw err
      } finally {
        setSavingChannelId(null)
      }
    },
    [workspacePath, config]
  )

  return useMemo(
    () => ({
      loading,
      savingChannelId,
      error,
      config,
      setChannelConfig,
      reload,
      saveChannel
    }),
    [loading, savingChannelId, error, config, setChannelConfig, reload, saveChannel]
  )
}
