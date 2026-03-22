import { useEffect, useMemo, useState } from 'react'
import { useSkillsStore } from '../../stores/skillsStore'
import { SkillCard } from './SkillCard'
import { SkillDetailDialog } from './SkillDetailDialog'
import { addToast } from '../../stores/toastStore'

/**
 * Full-width skills management surface (Codex-style list + detail modal).
 */
export function SkillsView(): JSX.Element {
  const {
    skills,
    loading,
    error,
    fetchSkills,
    selectedSkillName,
    skillContent,
    contentLoading,
    selectSkill,
    clearSelection,
    toggleSkillEnabled
  } = useSkillsStore()
  const [query, setQuery] = useState('')

  useEffect(() => {
    void fetchSkills()
  }, [fetchSkills])

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return skills
    return skills.filter(
      (s) =>
        s.name.toLowerCase().includes(q) ||
        (s.description ?? '').toLowerCase().includes(q)
    )
  }, [skills, query])

  const grouped = useMemo(() => {
    const order: Array<'builtin' | 'workspace' | 'user'> = ['builtin', 'workspace', 'user']
    const titles: Record<'builtin' | 'workspace' | 'user', string> = {
      builtin: 'Built-in skills',
      workspace: 'Workspace skills',
      user: 'User skills'
    }
    const map: Record<'builtin' | 'workspace' | 'user', typeof filtered> = {
      builtin: [],
      workspace: [],
      user: []
    }
    for (const s of filtered) {
      map[s.source].push(s)
    }
    return order.map((src) => ({ source: src, title: titles[src], items: map[src] }))
  }, [filtered])

  const selected = selectedSkillName
    ? skills.find((s) => s.name === selectedSkillName) ?? null
    : null

  const bodyMd = selected && skillContent != null ? stripYamlFrontmatter(skillContent) : ''

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        minHeight: 0,
        backgroundColor: 'var(--bg-primary)'
      }}
    >
      <header
        style={{
          padding: '20px 24px 12px',
          borderBottom: '1px solid var(--border-default)',
          flexShrink: 0
        }}
      >
        <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: '16px', flexWrap: 'wrap' }}>
          {/* Grows with header row so the path callout can stay on one line when space allows */}
          <div style={{ flex: '1 1 0%', minWidth: 0 }}>
            <h1 style={{ margin: 0, fontSize: '22px', fontWeight: 600, color: 'var(--text-primary)' }}>
              Skills
            </h1>
            <p style={skillsIntroSubtitle}>
              Extend the agent with markdown skills. Enable or disable each skill for this workspace.
            </p>
            <div style={skillsSourcesCallout} role="note">
              <p style={skillsSourceLine}>
                <strong style={skillsSourceHeading}>Workspace</strong>{' '}
                <PathChip>.craft/skills/</PathChip>
                <span style={skillsSourceRest}>
                  {' '}
                  — Includes built-ins deployed here and project-only skills.
                </span>
              </p>
              <p style={{ ...skillsSourceLine, marginTop: '10px' }}>
                <strong style={skillsSourceHeading}>User</strong>{' '}
                <PathChip>~/.craft/skills/</PathChip>
                <span style={skillsSourceRest}>
                  {' '}
                  — Shared across projects when no same-named skill exists in the workspace.
                </span>
              </p>
            </div>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap', flexShrink: 0 }}>
            <button
              type="button"
              onClick={() => void fetchSkills()}
              style={toolbarBtn}
              title="Refresh list"
            >
              Refresh
            </button>
            <input
              type="search"
              placeholder="Search skills"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              style={{
                minWidth: '180px',
                padding: '8px 12px',
                fontSize: '13px',
                borderRadius: '6px',
                border: '1px solid var(--border-default)',
                backgroundColor: 'var(--bg-secondary)',
                color: 'var(--text-primary)'
              }}
            />
          </div>
        </div>
      </header>

      <div style={{ flex: 1, overflow: 'auto', padding: '20px 24px' }}>
        {loading && <p style={{ color: 'var(--text-secondary)' }}>Loading skills…</p>}
        {error && (
          <p style={{ color: 'var(--error)' }} role="alert">
            {error}
          </p>
        )}
        {!loading && !error && skills.length === 0 && (
          <p style={{ color: 'var(--text-secondary)' }}>No skills found for this workspace.</p>
        )}
        {!loading &&
          !error &&
          grouped.map((section) =>
            section.items.length === 0 ? null : (
              <section key={section.source} style={{ marginBottom: '28px' }}>
                <h2
                  style={{
                    fontSize: '13px',
                    fontWeight: 600,
                    color: 'var(--text-secondary)',
                    textTransform: 'uppercase',
                    letterSpacing: '0.04em',
                    margin: '0 0 12px'
                  }}
                >
                  {section.title}
                </h2>
                <div
                  style={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))',
                    gap: '12px'
                  }}
                >
                  {section.items.map((skill) => (
                    <SkillCard
                      key={skill.name}
                      skill={skill}
                      onOpen={() => void selectSkill(skill.name)}
                      onToggleEnabled={async (enabled) => {
                        try {
                          await toggleSkillEnabled(skill.name, enabled)
                        } catch {
                          addToast('Failed to update skill', 'error')
                        }
                      }}
                    />
                  ))}
                </div>
              </section>
            )
          )}
      </div>

      {selected && (
        <SkillDetailDialog
          skill={selected}
          markdownBody={bodyMd}
          loading={contentLoading}
          onClose={() => clearSelection()}
          onToggleEnabled={async (enabled) => {
            try {
              await toggleSkillEnabled(selected.name, enabled)
              if (!enabled) clearSelection()
              else void selectSkill(selected.name)
            } catch {
              addToast('Failed to update skill', 'error')
            }
          }}
        />
      )}
    </div>
  )
}

const toolbarBtn: React.CSSProperties = {
  padding: '8px 14px',
  fontSize: '13px',
  borderRadius: '6px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-secondary)',
  color: 'var(--text-primary)',
  cursor: 'pointer'
}

const skillsIntroSubtitle: React.CSSProperties = {
  margin: '10px 0 0',
  fontSize: '13px',
  lineHeight: 1.5,
  color: 'var(--text-secondary)'
}

const skillsSourcesCallout: React.CSSProperties = {
  marginTop: '12px',
  width: '100%',
  boxSizing: 'border-box',
  padding: '10px 12px',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  borderLeft: '3px solid var(--accent)',
  backgroundColor: 'var(--bg-secondary)'
}

const skillsSourceLine: React.CSSProperties = {
  margin: 0,
  fontSize: '12px',
  lineHeight: 1.55,
  color: 'var(--text-secondary)'
}

const skillsSourceHeading: React.CSSProperties = {
  color: 'var(--text-primary)',
  fontWeight: 600
}

const skillsSourceRest: React.CSSProperties = {
  color: 'var(--text-secondary)'
}

const pathChipStyle: React.CSSProperties = {
  fontFamily: 'var(--font-mono)',
  fontSize: '12px',
  padding: '2px 6px',
  borderRadius: '4px',
  backgroundColor: 'var(--bg-tertiary)',
  color: 'var(--text-primary)',
  whiteSpace: 'nowrap' as const
}

function PathChip({ children }: { children: string }): JSX.Element {
  return <code style={pathChipStyle}>{children}</code>
}

function stripYamlFrontmatter(s: string): string {
  if (!s.startsWith('---')) return s
  const m = s.match(/^---\r?\n[\s\S]*?\r?\n---\r?\n/)
  return m ? s.slice(m[0].length).trim() : s
}
