import { useState, useEffect, useRef, useMemo } from 'react'
import { createPortal } from 'react-dom'
import { addToast } from '../../stores/toastStore'
import { applyTheme, resolveTheme, type ThemeMode } from '../../utils/theme'
import { normalizeLocale, type AppLocale } from '../../../shared/locales'
import { useSetUiLocale, useT } from '../../contexts/LocaleContext'
import type { MessageKey } from '../../../shared/locales'
import { ensureVisibleChannelsSeeded } from '../../utils/visibleChannelsDefaults'

/** Wire shape from `channel/list` (appserver-protocol.md §4.3.1). */
interface ChannelInfoWire {
  name: string
  category: string
}

const CATEGORY_ORDER = ['builtin', 'social', 'system', 'external'] as const

const CATEGORY_LABEL_KEY: Record<string, MessageKey> = {
  builtin: 'settings.channelCategory.builtin',
  social: 'settings.channelCategory.social',
  system: 'settings.channelCategory.system',
  external: 'settings.channelCategory.external'
}

function formatChannelChipLabel(name: string): string {
  return name.toUpperCase()
}

interface SettingsDialogProps {
  onClose: () => void
  /** Called after visible channel preferences are saved (reload thread list). */
  onVisibleChannelsUpdated?: () => void
}

type ConnectionMode = 'stdio' | 'websocket' | 'stdioAndWebSocket' | 'remote'
const DEFAULT_WS_HOST = '127.0.0.1'
const DEFAULT_WS_PORT = 9100

/**
 * Settings modal dialog.
 * Allows configuring AppServer binary path and displays app info.
 * Spec M7-7, §17.1 (Ctrl+,)
 */
export function SettingsDialog({
  onClose,
  onVisibleChannelsUpdated
}: SettingsDialogProps): JSX.Element {
  const t = useT()
  const setUiLocale = useSetUiLocale()
  const [binaryPath, setBinaryPath] = useState('')
  const [connectionMode, setConnectionMode] = useState<ConnectionMode>('stdio')
  const [wsHost, setWsHost] = useState(DEFAULT_WS_HOST)
  const [wsPort, setWsPort] = useState(String(DEFAULT_WS_PORT))
  const [remoteUrl, setRemoteUrl] = useState('')
  const [remoteToken, setRemoteToken] = useState('')
  const [theme, setTheme] = useState<ThemeMode>('dark')
  const [locale, setLocale] = useState<AppLocale>(normalizeLocale(undefined))
  const [version, setVersion] = useState('')
  const [saving, setSaving] = useState(false)
  /** Mirrors machine-local `visibleChannels` (see ensureVisibleChannelsSeeded). */
  const [visibleChannels, setVisibleChannels] = useState<string[]>([])
  const [serverChannels, setServerChannels] = useState<ChannelInfoWire[] | null>(null)
  const [channelListError, setChannelListError] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    inputRef.current?.focus()
    window.api.settings
      .get()
      .then(async (s) => {
        setBinaryPath(s.appServerBinaryPath ?? '')
        setConnectionMode((s.connectionMode ?? 'stdio') as ConnectionMode)
        setWsHost(s.webSocket?.host ?? DEFAULT_WS_HOST)
        setWsPort(String(s.webSocket?.port ?? DEFAULT_WS_PORT))
        setRemoteUrl(s.remote?.url ?? '')
        setRemoteToken(s.remote?.token ?? '')
        setTheme(resolveTheme(s.theme))
        setLocale(normalizeLocale(s.locale))
        setVisibleChannels(await ensureVisibleChannelsSeeded(s))
      })
      .catch(() => {})
    setVersion(typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : '0.1.0')

    function handleKeyDown(e: KeyboardEvent): void {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  useEffect(() => {
    let cancelled = false
    setServerChannels(null)
    setChannelListError(false)
    window.api.appServer
      .sendRequest('channel/list', {})
      .then((res) => {
        if (cancelled) return
        const r = res as { channels?: ChannelInfoWire[] }
        setServerChannels(r.channels ?? [])
        setChannelListError(false)
      })
      .catch(() => {
        if (!cancelled) {
          setServerChannels([])
          setChannelListError(true)
        }
      })
    return () => {
      cancelled = true
    }
  }, [])

  const channelsByCategory = useMemo(() => {
    const list = serverChannels ?? []
    const map = new Map<string, ChannelInfoWire[]>()
    for (const c of list) {
      const cat = c.category || 'builtin'
      const arr = map.get(cat) ?? []
      arr.push(c)
      map.set(cat, arr)
    }
    return map
  }, [serverChannels])

  async function handleThemeChange(next: ThemeMode): Promise<void> {
    setTheme(next)
    applyTheme(next)
    try {
      await window.api.settings.set({ theme: next })
    } catch (err) {
      addToast(
        t('settings.saveThemeFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  async function handleLocaleChange(next: AppLocale): Promise<void> {
    const normalized = normalizeLocale(next)
    const prev = locale
    setLocale(normalized)
    try {
      await window.api.settings.set({ locale: normalized })
      setUiLocale(normalized)
    } catch (err) {
      setLocale(prev)
      addToast(
        t('settings.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  async function setVisibleChannelsAndPersist(next: string[]): Promise<void> {
    setVisibleChannels(next)
    try {
      await window.api.settings.set({ visibleChannels: next })
      onVisibleChannelsUpdated?.()
    } catch (err) {
      addToast(
        t('settings.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  function toggleVisibleChannel(channel: string, checked: boolean): void {
    const base = visibleChannels
    const next = checked
      ? Array.from(new Set([...base, channel]))
      : base.filter((c) => c !== channel)
    void setVisibleChannelsAndPersist(next)
  }

  async function handleSave(): Promise<void> {
    setSaving(true)
    try {
      const parsedPort = Number.parseInt(wsPort.trim(), 10)
      const normalizedPort =
        Number.isInteger(parsedPort) && parsedPort > 0 && parsedPort <= 65535
          ? parsedPort
          : DEFAULT_WS_PORT
      await window.api.settings.set({
        appServerBinaryPath: binaryPath.trim() || undefined,
        connectionMode,
        webSocket: {
          host: wsHost.trim() || DEFAULT_WS_HOST,
          port: normalizedPort
        },
        remote: {
          url: remoteUrl.trim() || undefined,
          token: remoteToken.trim() || undefined
        }
      })
      addToast(t('settings.savedToast'), 'success')
      onClose()
    } catch (err) {
      addToast(
        t('settings.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setSaving(false)
    }
  }

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={t('settings.title')}
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 20000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'var(--overlay-scrim)'
      }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose()
      }}
    >
      <div
        style={{
          backgroundColor: 'var(--bg-secondary)',
          borderRadius: '10px',
          boxShadow: 'var(--shadow-level-3)',
          padding: '24px',
          width: '420px',
          maxWidth: 'calc(100vw - 48px)',
          maxHeight: 'calc(100vh - 48px)',
          overflowY: 'auto'
        }}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <h2
          style={{
            margin: '0 0 20px',
            fontSize: '15px',
            fontWeight: 600,
            color: 'var(--text-primary)'
          }}
        >
          {t('settings.title')}
        </h2>

        <div style={{ marginBottom: '16px' }}>
          <label
            htmlFor="settings-connection-mode"
            style={{
              display: 'block',
              fontSize: '12px',
              fontWeight: 500,
              color: 'var(--text-secondary)',
              marginBottom: '6px'
            }}
          >
            {t('settings.connectionMode')}
          </label>
          <select
            id="settings-connection-mode"
            value={connectionMode}
            onChange={(e) => {
              setConnectionMode(e.target.value as ConnectionMode)
            }}
            style={{
              padding: '7px 10px',
              fontSize: '13px',
              borderRadius: '6px',
              border: '1px solid var(--border-default)',
              background: 'var(--bg-primary)',
              color: 'var(--text-primary)',
              cursor: 'pointer',
              width: '100%'
            }}
          >
            <option value="stdio">{t('settings.connectionMode.stdio')}</option>
            <option value="websocket">{t('settings.connectionMode.websocket')}</option>
            <option value="stdioAndWebSocket">{t('settings.connectionMode.stdioAndWebSocket')}</option>
            <option value="remote">{t('settings.connectionMode.remote')}</option>
          </select>
          <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
            {t('settings.connectionModeHint')}
          </div>
        </div>

        {(connectionMode === 'websocket' || connectionMode === 'stdioAndWebSocket') && (
          <div style={{ marginBottom: '16px' }}>
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: '1fr 120px',
                gap: '8px'
              }}
            >
              <div>
                <label
                  htmlFor="settings-ws-host"
                  style={{
                    display: 'block',
                    fontSize: '12px',
                    fontWeight: 500,
                    color: 'var(--text-secondary)',
                    marginBottom: '6px'
                  }}
                >
                  {t('settings.wsHost')}
                </label>
                <input
                  id="settings-ws-host"
                  type="text"
                  value={wsHost}
                  onChange={(e) => setWsHost(e.target.value)}
                  placeholder={DEFAULT_WS_HOST}
                  style={{
                    width: '100%',
                    boxSizing: 'border-box',
                    padding: '7px 10px',
                    fontSize: '13px',
                    borderRadius: '6px',
                    border: '1px solid var(--border-default)',
                    background: 'var(--bg-primary)',
                    color: 'var(--text-primary)',
                    outline: 'none',
                    fontFamily: 'var(--font-mono)'
                  }}
                />
              </div>
              <div>
                <label
                  htmlFor="settings-ws-port"
                  style={{
                    display: 'block',
                    fontSize: '12px',
                    fontWeight: 500,
                    color: 'var(--text-secondary)',
                    marginBottom: '6px'
                  }}
                >
                  {t('settings.wsPort')}
                </label>
                <input
                  id="settings-ws-port"
                  type="number"
                  value={wsPort}
                  onChange={(e) => setWsPort(e.target.value)}
                  placeholder={String(DEFAULT_WS_PORT)}
                  min={1}
                  max={65535}
                  style={{
                    width: '100%',
                    boxSizing: 'border-box',
                    padding: '7px 10px',
                    fontSize: '13px',
                    borderRadius: '6px',
                    border: '1px solid var(--border-default)',
                    background: 'var(--bg-primary)',
                    color: 'var(--text-primary)',
                    outline: 'none',
                    fontFamily: 'var(--font-mono)'
                  }}
                />
              </div>
            </div>
            <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
              {t('settings.wsHint')}
            </div>
          </div>
        )}

        {connectionMode === 'remote' && (
          <div style={{ marginBottom: '16px' }}>
            <label
              htmlFor="settings-remote-url"
              style={{
                display: 'block',
                fontSize: '12px',
                fontWeight: 500,
                color: 'var(--text-secondary)',
                marginBottom: '6px'
              }}
            >
              {t('settings.remoteUrl')}
            </label>
            <input
              id="settings-remote-url"
              type="text"
              value={remoteUrl}
              onChange={(e) => setRemoteUrl(e.target.value)}
              placeholder="ws://127.0.0.1:9100/ws"
              style={{
                width: '100%',
                boxSizing: 'border-box',
                padding: '7px 10px',
                fontSize: '13px',
                borderRadius: '6px',
                border: '1px solid var(--border-default)',
                background: 'var(--bg-primary)',
                color: 'var(--text-primary)',
                outline: 'none',
                fontFamily: 'var(--font-mono)'
              }}
            />
            <label
              htmlFor="settings-remote-token"
              style={{
                display: 'block',
                fontSize: '12px',
                fontWeight: 500,
                color: 'var(--text-secondary)',
                marginTop: '10px',
                marginBottom: '6px'
              }}
            >
              {t('settings.remoteToken')}
            </label>
            <input
              id="settings-remote-token"
              type="password"
              value={remoteToken}
              onChange={(e) => setRemoteToken(e.target.value)}
              placeholder={t('settings.remoteTokenPlaceholder')}
              style={{
                width: '100%',
                boxSizing: 'border-box',
                padding: '7px 10px',
                fontSize: '13px',
                borderRadius: '6px',
                border: '1px solid var(--border-default)',
                background: 'var(--bg-primary)',
                color: 'var(--text-primary)',
                outline: 'none',
                fontFamily: 'var(--font-mono)'
              }}
            />
          </div>
        )}

        {/* AppServer binary path */}
        <div style={{ marginBottom: '16px' }}>
          <label
            htmlFor="settings-binary-path"
            style={{
              display: 'block',
              fontSize: '12px',
              fontWeight: 500,
              color: 'var(--text-secondary)',
              marginBottom: '6px'
            }}
          >
            {t('settings.appServerBinary')}
          </label>
          <input
            id="settings-binary-path"
            ref={inputRef}
            type="text"
            value={binaryPath}
            onChange={(e) => setBinaryPath(e.target.value)}
            placeholder={t('settings.binaryPlaceholder')}
            style={{
              width: '100%',
              boxSizing: 'border-box',
              padding: '7px 10px',
              fontSize: '13px',
              borderRadius: '6px',
              border: '1px solid var(--border-default)',
              background: 'var(--bg-primary)',
              color: 'var(--text-primary)',
              outline: 'none',
              fontFamily: 'var(--font-mono)'
            }}
            onFocus={(e) => {
              e.currentTarget.style.borderColor = 'var(--border-active)'
            }}
            onBlur={(e) => {
              e.currentTarget.style.borderColor = 'var(--border-default)'
            }}
          />
          <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
            {t('settings.binaryHint')}
          </div>
        </div>

        <div style={{ marginBottom: '16px' }}>
          <label
            htmlFor="settings-language"
            style={{
              display: 'block',
              fontSize: '12px',
              fontWeight: 500,
              color: 'var(--text-secondary)',
              marginBottom: '6px'
            }}
          >
            {t('settings.language')}
          </label>
          <select
            id="settings-language"
            value={locale}
            onChange={(e) => {
              void handleLocaleChange(e.target.value as AppLocale)
            }}
            style={{
              padding: '7px 10px',
              fontSize: '13px',
              borderRadius: '6px',
              border: '1px solid var(--border-default)',
              background: 'var(--bg-primary)',
              color: 'var(--text-primary)',
              cursor: 'pointer',
              width: '180px'
            }}
          >
            <option value="en">{t('settings.language.en')}</option>
            <option value="zh-Hans">{t('settings.language.zhHans')}</option>
          </select>
        </div>

        <div style={{ marginBottom: '16px' }}>
          <div
            style={{
              display: 'block',
              fontSize: '12px',
              fontWeight: 500,
              color: 'var(--text-secondary)',
              marginBottom: '6px'
            }}
          >
            {t('settings.crossChannelVisibility')}
          </div>
          <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginBottom: '10px', lineHeight: 1.45 }}>
            {t('settings.crossChannelHint')}
          </div>
          {serverChannels === null && (
            <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginBottom: '8px' }}>
              {t('settings.channelListLoading')}
            </div>
          )}
          {serverChannels !== null && channelListError && (
            <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginBottom: '8px' }}>
              {t('settings.channelListUnavailable')}
            </div>
          )}
          {serverChannels !== null &&
            !channelListError &&
            CATEGORY_ORDER.map((cat) => {
              const items = channelsByCategory.get(cat)
              if (!items?.length) return null
              const labelKey = CATEGORY_LABEL_KEY[cat]
              return (
                <div key={cat} style={{ marginBottom: '10px' }}>
                  <div
                    style={{
                      fontSize: '10px',
                      fontWeight: 600,
                      textTransform: 'uppercase',
                      letterSpacing: '0.04em',
                      color: 'var(--text-dimmed)',
                      marginBottom: '6px'
                    }}
                  >
                    {labelKey ? t(labelKey) : cat}
                  </div>
                  <div
                    style={{
                      display: 'flex',
                      flexWrap: 'wrap',
                      gap: '6px'
                    }}
                  >
                    {items.map((ch) => {
                      const selected = visibleChannels.includes(ch.name)
                      return (
                        <button
                          key={ch.name}
                          type="button"
                          role="checkbox"
                          aria-checked={selected}
                          onClick={() => {
                            toggleVisibleChannel(ch.name, !selected)
                          }}
                          style={{
                            fontSize: '11px',
                            fontWeight: 600,
                            letterSpacing: '0.02em',
                            padding: '4px 8px',
                            borderRadius: '6px',
                            cursor: 'pointer',
                            border: selected
                              ? '1px solid var(--accent)'
                              : '1px solid var(--border-default)',
                            backgroundColor: selected ? 'var(--accent)' : 'transparent',
                            color: selected ? 'var(--on-accent)' : 'var(--text-primary)'
                          }}
                        >
                          {formatChannelChipLabel(ch.name)}
                        </button>
                      )
                    })}
                  </div>
                </div>
              )
            })}
        </div>

        <div style={{ marginBottom: '16px' }}>
          <label
            htmlFor="settings-theme"
            style={{
              display: 'block',
              fontSize: '12px',
              fontWeight: 500,
              color: 'var(--text-secondary)',
              marginBottom: '6px'
            }}
          >
            {t('settings.theme')}
          </label>
          <select
            id="settings-theme"
            value={theme}
            onChange={(e) => {
              void handleThemeChange(e.target.value as ThemeMode)
            }}
            style={{
              padding: '7px 10px',
              fontSize: '13px',
              borderRadius: '6px',
              border: '1px solid var(--border-default)',
              background: 'var(--bg-primary)',
              color: 'var(--text-primary)',
              cursor: 'pointer',
              width: '180px'
            }}
          >
            <option value="dark">{t('settings.optionThemeDark')}</option>
            <option value="light">{t('settings.optionThemeLight')}</option>
          </select>
        </div>

        <div
          style={{
            fontSize: '12px',
            color: 'var(--text-dimmed)',
            marginBottom: '20px'
          }}
        >
          DotCraft Desktop {t('settings.version')} {version}
        </div>

        <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
          <button
            type="button"
            onClick={onClose}
            style={{
              padding: '7px 16px',
              border: '1px solid var(--border-default)',
              borderRadius: '6px',
              backgroundColor: 'transparent',
              color: 'var(--text-primary)',
              fontSize: '13px',
              cursor: 'pointer'
            }}
          >
            {t('common.cancel')}
          </button>
          <button
            type="button"
            onClick={() => {
              void handleSave()
            }}
            disabled={saving}
            style={{
              padding: '7px 16px',
              border: 'none',
              borderRadius: '6px',
              backgroundColor: 'var(--accent)',
              color: 'var(--on-accent)',
              fontSize: '13px',
              fontWeight: 500,
              cursor: saving ? 'default' : 'pointer',
              opacity: saving ? 0.7 : 1
            }}
          >
            {saving ? t('settings.saving') : t('settings.save')}
          </button>
        </div>
      </div>
    </div>
  )

  return createPortal(dialog, document.body) as JSX.Element
}
