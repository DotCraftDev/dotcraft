import { useEffect, useMemo, useState } from 'react'
import { Sparkle, Terminal } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { CustomCommandInfo } from '../../hooks/useCustomCommandCatalog'
import { ActionTooltip } from '../ui/ActionTooltip'

export interface SlashSkillInfo {
  name: string
  description: string
}

interface CommandSearchPopoverProps {
  query: string
  visible: boolean
  loading: boolean
  commands: CustomCommandInfo[]
  skills?: SlashSkillInfo[]
  onSelectCommand: (commandName: string) => void
  onSelectSkill?: (skillName: string) => void
  onDismiss: () => void
}

export function CommandSearchPopover({
  query,
  visible,
  loading,
  commands,
  skills,
  onSelectCommand,
  onSelectSkill,
  onDismiss
}: CommandSearchPopoverProps): JSX.Element | null {
  const t = useT()
  const skillList = skills ?? []
  const [highlight, setHighlight] = useState(0)
  const filteredCommands = useMemo(() => {
    const prefix = query.toLowerCase()
    if (!prefix) return commands
    return commands.filter((cmd) => {
      if (cmd.name.slice(1).toLowerCase().startsWith(prefix)) return true
      return cmd.aliases.some((alias) => {
        const bare = alias.startsWith('/') ? alias.slice(1) : alias
        return bare.toLowerCase().startsWith(prefix)
      })
    })
  }, [commands, query])
  const filteredSkills = useMemo(() => {
    const prefix = query.toLowerCase()
    if (!prefix) return skillList
    return skillList.filter((skill) => skill.name.toLowerCase().startsWith(prefix))
  }, [query, skillList])
  const entries = useMemo(
    () => [
      ...filteredCommands.map((command) => ({ type: 'command' as const, command })),
      ...filteredSkills.map((skill) => ({ type: 'skill' as const, skill }))
    ],
    [filteredCommands, filteredSkills]
  )

  useEffect(() => {
    setHighlight(0)
  }, [entries, query])

  useEffect(() => {
    if (!visible) return
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') {
        e.preventDefault()
        e.stopPropagation()
        onDismiss()
        return
      }
      if (entries.length === 0) return
      if (e.key === 'ArrowDown') {
        e.preventDefault()
        e.stopPropagation()
        setHighlight((h) => Math.min(entries.length - 1, h + 1))
      } else if (e.key === 'ArrowUp') {
        e.preventDefault()
        e.stopPropagation()
        setHighlight((h) => Math.max(0, h - 1))
      } else if (e.key === 'Enter' || e.key === 'Tab') {
        e.preventDefault()
        e.stopPropagation()
        const item = entries[highlight]
        if (!item) return
        if (item.type === 'command') onSelectCommand(item.command.name)
        else onSelectSkill?.(item.skill.name)
      }
    }
    window.addEventListener('keydown', onKey, true)
    return () => {
      window.removeEventListener('keydown', onKey, true)
    }
  }, [entries, highlight, onDismiss, onSelectCommand, onSelectSkill, visible])

  if (!visible) return null

  return (
    <div
      role="listbox"
      style={{
        position: 'absolute',
        bottom: '100%',
        left: 0,
        marginBottom: '4px',
        minWidth: '320px',
        maxWidth: '480px',
        maxHeight: '260px',
        overflowY: 'auto',
        zIndex: 50,
        boxShadow: '0 4px 12px rgba(0,0,0,0.4)',
        background: 'var(--bg-secondary)',
        border: '1px solid var(--border-default)',
        borderRadius: '8px',
        padding: '4px 0'
      }}
    >
      {loading && (
        <div style={{ padding: '8px 12px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
          {t('slashSearch.loading')}
        </div>
      )}
      {!loading && entries.length === 0 && query.trim() !== '' && (
        <div style={{ padding: '8px 12px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
          {t('slashSearch.noMatch')}
        </div>
      )}
      {!loading && entries.length === 0 && query.trim() === '' && (
        <div style={{ padding: '8px 12px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
          {t('slashSearch.hint')}
        </div>
      )}
      {!loading && filteredCommands.length > 0 && (
        <SectionHeader label={t('slashSearch.commandsGroup')} />
      )}
      {!loading &&
        filteredCommands.map((cmd) => {
          const index = entries.findIndex((entry) => entry.type === 'command' && entry.command.name === cmd.name)
          const description = cmd.description || t('slashSearch.noDescription')
          return (
            <ActionTooltip key={cmd.name} label={description} wrapperStyle={{ display: 'block', width: '100%' }}>
              <button
                type="button"
                role="option"
                aria-selected={index === highlight}
                onMouseEnter={() => {
                  setHighlight(index)
                }}
                onClick={() => {
                  onSelectCommand(cmd.name)
                }}
                style={{
                  display: 'flex',
                  width: '100%',
                  alignItems: 'center',
                  gap: '8px',
                  padding: '7px 12px',
                  border: 'none',
                  background: index === highlight ? 'var(--bg-active)' : 'transparent',
                  color: 'var(--text-primary)',
                  cursor: 'pointer',
                  textAlign: 'left'
                }}
              >
                <span
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '4px',
                    borderRadius: '5px',
                    padding: '1px 6px',
                    fontSize: '12px',
                    fontWeight: 600,
                    background: 'color-mix(in srgb, var(--accent) 16%, transparent)',
                    border: '1px solid color-mix(in srgb, var(--accent) 38%, transparent)',
                    color: 'var(--accent)',
                    whiteSpace: 'nowrap'
                  }}
                >
                  <Terminal size={11} strokeWidth={2} aria-hidden />
                  {highlightMatch(cmd.name.replace(/^\/+/, ''), query)}
                </span>
                <span
                  style={{
                    fontSize: '12px',
                    color: 'var(--text-secondary)',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap'
                  }}
                >
                  {description}
                </span>
              </button>
            </ActionTooltip>
          )
        })}
      {!loading && filteredSkills.length > 0 && (
        <SectionHeader label={t('slashSearch.skillsGroup')} />
      )}
      {!loading &&
        filteredSkills.map((skill) => {
          const index = entries.findIndex((entry) => entry.type === 'skill' && entry.skill.name === skill.name)
          const description = skill.description || t('slashSearch.noDescription')
          return (
            <ActionTooltip key={skill.name} label={description} wrapperStyle={{ display: 'block', width: '100%' }}>
              <button
                type="button"
                role="option"
                aria-selected={index === highlight}
                onMouseEnter={() => {
                  setHighlight(index)
                }}
                onClick={() => {
                  onSelectSkill?.(skill.name)
                }}
                style={{
                  display: 'flex',
                  width: '100%',
                  alignItems: 'center',
                  gap: '8px',
                  padding: '7px 12px',
                  border: 'none',
                  background: index === highlight ? 'var(--bg-active)' : 'transparent',
                  color: 'var(--text-primary)',
                  cursor: 'pointer',
                  textAlign: 'left'
                }}
              >
                <span
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '4px',
                    borderRadius: '5px',
                    padding: '1px 6px',
                    fontSize: '12px',
                    fontWeight: 600,
                    background: 'color-mix(in srgb, var(--success) 16%, transparent)',
                    border: '1px solid color-mix(in srgb, var(--success) 38%, transparent)',
                    color: 'var(--success)',
                    whiteSpace: 'nowrap'
                  }}
                >
                  <Sparkle size={11} strokeWidth={2} aria-hidden />
                  {highlightMatch(skill.name, query)}
                </span>
                <span
                  style={{
                    fontSize: '12px',
                    color: 'var(--text-secondary)',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap'
                  }}
                >
                  {description}
                </span>
              </button>
            </ActionTooltip>
          )
        })}
    </div>
  )
}

function SectionHeader({ label }: { label: string }): JSX.Element {
  return (
    <div
      style={{
        padding: '4px 12px 3px',
        fontSize: '11px',
        color: 'var(--text-dimmed)',
        fontWeight: 600,
        textTransform: 'uppercase',
        letterSpacing: '0.04em'
      }}
    >
      {label}
    </div>
  )
}

function highlightMatch(name: string, query: string): JSX.Element {
  const target = name.replace(/^\/+/, '')
  const lower = target.toLowerCase()
  const lowerQuery = query.toLowerCase()
  const idx = lower.indexOf(lowerQuery)
  if (!query || idx < 0) return <>{target}</>
  return (
    <>
      {target.slice(0, idx)}
      <span style={{ color: 'var(--text-primary)' }}>{target.slice(idx, idx + query.length)}</span>
      {target.slice(idx + query.length)}
    </>
  )
}
