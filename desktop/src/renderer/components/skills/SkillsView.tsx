import { useEffect, useMemo, useState } from 'react'
import { Check, ChevronDown, ChevronLeft, Download, Ellipsis, ExternalLink, Plus, Search, Settings } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useSkillsStore, type SkillEntry } from '../../stores/skillsStore'
import { useSkillMarketStore, type SkillMarketProviderFilter } from '../../stores/skillMarketStore'
import type { MarketSkillDetail, MarketSkillSummary } from '../../../shared/skillMarket'
import { SkillAvatar } from './SkillAvatar'
import { SkillDetailDialog } from './SkillDetailDialog'
import { PillSwitch } from '../ui/PillSwitch'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { RefreshIcon } from '../ui/AppIcons'
import { addToast } from '../../stores/toastStore'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { useUIStore } from '../../stores/uiStore'

type ViewMode = 'browse' | 'manage'
type SourceFilter = 'all' | 'system' | 'personal' | 'market'

export function SkillsView(): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
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
  const {
    query: marketQuery,
    results,
    loading: marketLoading,
    error: marketError,
    selectedSkill: selectedMarketSkill,
    detailLoading,
    installSlug,
    setQuery: setMarketQuery,
    search,
    selectSkill: selectMarketSkill,
    clearSelection: clearMarketSelection,
    installSelected
  } = useSkillMarketStore()

  const [mode, setMode] = useState<ViewMode>('browse')
  const [query, setQuery] = useState('')
  const [sourceFilter, setSourceFilter] = useState<SourceFilter>('all')
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)
  const [savedSkillName, setSavedSkillName] = useState<string | null>(null)

  useEffect(() => {
    void fetchSkills()
  }, [fetchSkills])

  useEffect(() => {
    setMarketQuery(query)
  }, [query, setMarketQuery])

  useEffect(() => {
    const trimmed = marketQuery.trim()
    if (!trimmed) {
      void search()
      return
    }
    const timer = window.setTimeout(() => void search(), 350)
    return () => window.clearTimeout(timer)
  }, [marketQuery, search])

  useEffect(() => {
    if (!savedSkillName) return
    const timer = window.setTimeout(() => setSavedSkillName(null), 1500)
    return () => window.clearTimeout(timer)
  }, [savedSkillName])

  const filteredSkills = useMemo(() => filterLocalSkills(skills, query, sourceFilter), [skills, query, sourceFilter])
  const manageSkills = useMemo(() => filterLocalSkills(skills, query, 'all'), [skills, query])
  const systemSkills = filteredSkills.filter((skill) => skill.source === 'builtin')
  const personalSkills = filteredSkills.filter((skill) => skill.source !== 'builtin')
  const marketResults = sourceFilter === 'system' || sourceFilter === 'personal'
    ? []
    : results

  const selected = selectedSkillName
    ? skills.find((s) => s.name === selectedSkillName) ?? null
    : null
  const bodyMd = selected && skillContent != null ? stripYamlFrontmatter(skillContent) : ''

  async function handleRefresh(): Promise<void> {
    await fetchSkills()
    if (query.trim()) await search()
  }

  function handleTrySkillInChat(skill: SkillEntry): void {
    const text = `$${skill.name}`
    const ui = useUIStore.getState()
    const existing = ui.welcomeDraft
    ui.setWelcomeDraft({
      text,
      segments: [{ type: 'skill', skillName: skill.name }],
      selectionStart: 1,
      selectionEnd: 1,
      images: [],
      files: [],
      mode: existing?.mode ?? 'agent',
      model: existing?.model || 'Default',
      approvalPolicy: existing?.approvalPolicy ?? 'default'
    })
    clearSelection()
    ui.goToNewChat()
  }

  async function handleInstallMarketSkill(skill: MarketSkillDetail, overwrite = false): Promise<void> {
    if ((skill.installed || skill.updateAvailable) && !overwrite) {
      const ok = await confirm({
        title: t('skillMarket.overwriteTitle'),
        message: t('skillMarket.overwriteMessage', { name: skill.name }),
        confirmLabel: skill.updateAvailable ? t('skillMarket.update') : t('skillMarket.reinstall'),
        cancelLabel: t('common.cancel')
      })
      if (!ok) return
      await handleInstallMarketSkill(skill, true)
      return
    }
    try {
      await installSelected(overwrite)
      await fetchSkills()
      addToast(t('skillMarket.installSuccess', { name: skill.name }), 'success')
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      addToast(t('skillMarket.installFailed', { error: msg }), 'error')
    }
  }

  if (mode === 'manage') {
    return (
      <SkillsManageView
        skills={manageSkills}
        allSkills={skills}
        query={query}
        loading={loading}
        error={error}
        savedSkillName={savedSkillName}
        onQueryChange={setQuery}
        onBack={() => setMode('browse')}
        onToggleEnabled={async (skill, enabled) => {
          try {
            await toggleSkillEnabled(skill.name, enabled)
            setSavedSkillName(skill.name)
          } catch {
            addToast(t('skills.updateFailed'), 'error')
          }
        }}
      />
    )
  }

  return (
    <div style={page}>
      <header style={browseHeader}>
        <div style={topActions}>
          <button type="button" onClick={() => setMode('manage')} style={manageButton}>
            <Settings size={14} aria-hidden />
            {t('skills.manage')}
          </button>
          <ActionTooltip label={t('skills.moreActions')} placement="bottom">
            <button
              type="button"
              aria-label={t('skills.moreActions')}
              onClick={(event) => setMenuPosition({ x: event.clientX, y: event.clientY })}
              style={iconButton}
            >
              <Ellipsis size={16} aria-hidden />
            </button>
          </ActionTooltip>
        </div>

        <h1 style={heroTitle}>{t('skills.heroTitle')}</h1>
        <div style={searchRow}>
          <div style={searchBox}>
            <Search size={15} aria-hidden />
            <input
              type="search"
              placeholder={t('skills.searchPlaceholder')}
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              style={searchInput}
            />
          </div>
          <SkillFilterMenu value={sourceFilter} onChange={setSourceFilter} />
        </div>
      </header>

      <main style={browseMain}>
        {loading && <p style={emptyText}>{t('skills.loading')}</p>}
        {error && <p style={{ ...emptyText, color: 'var(--error)' }} role="alert">{error}</p>}
        {marketError && <p style={{ ...emptyText, color: 'var(--error)' }} role="alert">{marketError}</p>}

        {query.trim() && sourceFilter !== 'system' && sourceFilter !== 'personal' && (
          <SkillSection title={t('skills.section.marketResults')}>
            {marketLoading ? (
              <p style={emptyText}>{t('skillMarket.loading')}</p>
            ) : marketResults.length > 0 ? (
              <CompactGrid>
                {marketResults.map((skill) => (
                  <MarketSkillItem
                    key={`${skill.provider}:${skill.slug}`}
                    skill={skill}
                    onOpen={() => void selectMarketSkill(skill)}
                  />
                ))}
              </CompactGrid>
            ) : (
              <p style={emptyText}>{t('skillMarket.noResults')}</p>
            )}
          </SkillSection>
        )}

        {sourceFilter !== 'market' && (
          <>
            {systemSkills.length > 0 && (
              <SkillSection title={t('skills.section.system')}>
                <CompactGrid>
                  {systemSkills.map((skill) => (
                    <LocalSkillItem key={skill.name} skill={skill} onOpen={() => void selectSkill(skill.name)} />
                  ))}
                </CompactGrid>
              </SkillSection>
            )}
            {personalSkills.length > 0 && (
              <SkillSection title={t('skills.section.personal')}>
                <CompactGrid>
                  {personalSkills.map((skill) => (
                    <LocalSkillItem key={skill.name} skill={skill} onOpen={() => void selectSkill(skill.name)} />
                  ))}
                </CompactGrid>
              </SkillSection>
            )}
          </>
        )}

        {!loading && !error && !query.trim() && systemSkills.length === 0 && personalSkills.length === 0 && (
          <p style={emptyText}>{t('skills.empty')}</p>
        )}
      </main>

      {menuPosition && (
        <ContextMenu
          position={menuPosition}
          onClose={() => setMenuPosition(null)}
          items={[
            {
              label: t('skills.refresh'),
              icon: <RefreshIcon size={14} />,
              onClick: () => void handleRefresh()
            }
          ]}
        />
      )}

      {selected && (
        <SkillDetailDialog
          skill={selected}
          markdownBody={bodyMd}
          loading={contentLoading}
          showToggle
          onClose={() => clearSelection()}
          onToggleEnabled={async (enabled) => {
            try {
              await toggleSkillEnabled(selected.name, enabled)
              setSavedSkillName(selected.name)
            } catch {
              addToast(t('skills.updateFailed'), 'error')
            }
          }}
          onTryInChat={() => handleTrySkillInChat(selected)}
        />
      )}

      {selectedMarketSkill && (
        <MarketSkillDetailDialog
          skill={selectedMarketSkill}
          loading={detailLoading}
          installing={installSlug === selectedMarketSkill.slug}
          onClose={clearMarketSelection}
          onInstall={() => void handleInstallMarketSkill(selectedMarketSkill)}
        />
      )}
    </div>
  )
}

function SkillFilterMenu({
  value,
  onChange
}: {
  value: SourceFilter
  onChange: (value: SourceFilter) => void
}): JSX.Element {
  const t = useT()
  const [position, setPosition] = useState<ContextMenuPosition | null>(null)
  const labels: Record<SourceFilter, string> = {
    all: t('skills.filter.all'),
    system: t('skills.filter.system'),
    personal: t('skills.filter.personal'),
    market: t('skills.filter.market')
  }

  return (
    <>
      <button
        type="button"
        aria-label={t('skills.filter.label')}
        aria-haspopup="menu"
        aria-expanded={position != null}
        onClick={(event) => {
          const rect = event.currentTarget.getBoundingClientRect()
          setPosition({ x: rect.left, y: rect.bottom + 6 })
        }}
        style={filterMenuButton}
      >
        <span>{labels[value]}</span>
        <ChevronDown size={14} aria-hidden />
      </button>
      {position && (
        <ContextMenu
          position={position}
          onClose={() => setPosition(null)}
          items={(Object.keys(labels) as SourceFilter[]).map((key) => ({
            label: labels[key],
            onClick: () => onChange(key)
          }))}
        />
      )}
    </>
  )
}

function SkillsManageView({
  skills,
  allSkills,
  query,
  loading,
  error,
  savedSkillName,
  onQueryChange,
  onBack,
  onToggleEnabled
}: {
  skills: SkillEntry[]
  allSkills: SkillEntry[]
  query: string
  loading: boolean
  error: string | null
  savedSkillName: string | null
  onQueryChange: (query: string) => void
  onBack: () => void
  onToggleEnabled: (skill: SkillEntry, enabled: boolean) => void
}): JSX.Element {
  const t = useT()
  const systemCount = allSkills.filter((skill) => skill.source === 'builtin').length
  const personalCount = allSkills.length - systemCount

  return (
    <div style={page}>
      <header style={manageHeader}>
        <div style={breadcrumb}>
          <button type="button" onClick={onBack} style={breadcrumbButton}>
            <ChevronLeft size={14} aria-hidden />
            {t('skills.pageTitle')}
          </button>
          <span style={breadcrumbSep}>›</span>
          <span style={breadcrumbCurrent}>{t('skills.manage')}</span>
        </div>
        <div style={manageToolbar}>
          <Chip label={t('skills.manage.count.all', { count: String(allSkills.length) })} active />
          <Chip label={t('skills.manage.count.system', { count: String(systemCount) })} />
          <Chip label={t('skills.manage.count.personal', { count: String(personalCount) })} />
          <div style={{ flex: 1 }} />
          {savedSkillName && <span style={savedHint}>{t('settings.savedToast')}</span>}
          <div style={{ ...searchBox, maxWidth: '280px', flex: '0 1 280px' }}>
            <Search size={15} aria-hidden />
            <input
              type="search"
              placeholder={t('skills.manage.searchPlaceholder')}
              value={query}
              onChange={(event) => onQueryChange(event.target.value)}
              style={searchInput}
            />
          </div>
        </div>
      </header>

      <main style={manageMain}>
        {loading && <p style={emptyText}>{t('skills.loading')}</p>}
        {error && <p style={{ ...emptyText, color: 'var(--error)' }} role="alert">{error}</p>}
        {!loading && !error && skills.map((skill) => (
          <div key={skill.name} style={manageRow}>
            <SkillAvatar
              name={skill.name}
              displayName={skillTitle(skill)}
              size={38}
              iconDataUrl={skill.iconSmallDataUrl}
            />
            <div style={{ minWidth: 0, flex: 1 }}>
              <div style={rowTitle}>{skillTitle(skill)}</div>
              <div style={rowDesc}>{skillSubtitle(skill, t)}</div>
            </div>
            <span style={manageSource}>{sourceLabel(skill, t)}</span>
            <PillSwitch
              checked={skill.enabled}
              onChange={(enabled) => onToggleEnabled(skill, enabled)}
              size="sm"
              aria-label={skill.enabled ? t('skillCard.toggleDisable') : t('skillCard.toggleEnable')}
            />
          </div>
        ))}
      </main>
    </div>
  )
}

function LocalSkillItem({ skill, onOpen }: { skill: SkillEntry; onOpen: () => void }): JSX.Element {
  const t = useT()
  return (
    <button type="button" onClick={onOpen} style={compactItem}>
      <SkillAvatar
        name={skill.name}
        displayName={skillTitle(skill)}
        size={40}
        iconDataUrl={skill.iconSmallDataUrl}
      />
      <div style={{ minWidth: 0, flex: 1 }}>
        <div style={rowTitle}>{skillTitle(skill)}</div>
        <div style={rowDesc}>{skillSubtitle(skill, t)}</div>
      </div>
      <span title={skill.enabled ? t('skillCard.on') : t('skillCard.disabledBadge')} style={statusIcon}>
        {skill.enabled ? <Check size={16} aria-hidden /> : t('skillCard.disabledBadge')}
      </span>
    </button>
  )
}

function MarketSkillItem({ skill, onOpen }: { skill: MarketSkillSummary; onOpen: () => void }): JSX.Element {
  const t = useT()
  return (
    <button type="button" onClick={onOpen} style={compactItem}>
      <SkillAvatar name={skill.name} size={40} />
      <div style={{ minWidth: 0, flex: 1 }}>
        <div style={rowTitle}>{skill.name}</div>
        <div style={rowDesc}>{skill.description || skill.slug}</div>
      </div>
      <span style={marketAction(skill)}>
        {skill.updateAvailable
          ? t('skillMarket.updateAvailable')
          : skill.installed
            ? t('skillMarket.installed')
            : <Plus size={16} aria-hidden />}
      </span>
    </button>
  )
}

function MarketSkillDetailDialog({
  skill,
  loading,
  installing,
  onClose,
  onInstall
}: {
  skill: MarketSkillDetail
  loading: boolean
  installing: boolean
  onClose: () => void
  onInstall: () => void
}): JSX.Element {
  const t = useT()

  useEffect(() => {
    function onKey(event: KeyboardEvent): void {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const installLabel = skill.updateAvailable
    ? t('skillMarket.update')
    : skill.installed
      ? t('skillMarket.reinstall')
      : t('skillMarket.install')

  return (
    <div role="presentation" style={modalScrim} onClick={onClose}>
      <div role="dialog" aria-modal aria-labelledby="skill-market-detail-title" style={modal} onClick={(e) => e.stopPropagation()}>
        <header style={modalHeader}>
          <div style={{ minWidth: 0 }}>
            <h2 id="skill-market-detail-title" style={modalTitle}>{skill.name}</h2>
            <p style={modalSubtitle}>{skill.description || skill.slug}</p>
          </div>
          <button type="button" onClick={onClose} style={iconCloseBtn} aria-label={t('skillDetail.close')}>×</button>
        </header>
        <div style={metaRow}>
          <Meta label={t('skillMarket.provider')} value={providerLabel(skill.provider)} />
          <Meta label={t('skillMarket.version')} value={skill.version || t('skillMarket.versionUnknown')} />
          {skill.author && <Meta label={t('skillMarket.author')} value={skill.author} />}
          {skill.downloads != null && <Meta label={t('skillMarket.downloadsLabel')} value={String(skill.downloads)} />}
        </div>
        <div style={modalBody}>
          {loading ? (
            <p style={emptyText}>{t('skillDetail.loading')}</p>
          ) : skill.readme ? (
            <MarkdownRenderer content={stripYamlFrontmatter(skill.readme)} linkMode="external" />
          ) : skill.description ? (
            <p style={previewFallbackText}>{skill.description}</p>
          ) : (
            <p style={emptyText}>{t('skillMarket.noReadme')}</p>
          )}
        </div>
        <footer style={modalFooter}>
          <button
            type="button"
            onClick={() => {
              if (skill.sourceUrl) void window.api.shell.openExternal(skill.sourceUrl)
            }}
            disabled={!skill.sourceUrl}
            style={!skill.sourceUrl ? disabledSecondaryBtn : secondaryBtn}
          >
            <ExternalLink size={14} aria-hidden />
            {t('skillMarket.openSource')}
          </button>
          <button type="button" onClick={onInstall} disabled={installing || loading} style={installing || loading ? disabledPrimaryBtn : primaryBtn}>
            <Download size={14} aria-hidden />
            {installing ? t('skillMarket.installing') : installLabel}
          </button>
        </footer>
      </div>
    </div>
  )
}

function SkillSection({ title, children }: { title: string; children: React.ReactNode }): JSX.Element {
  return (
    <section style={{ marginBottom: '34px' }}>
      <h2 style={sectionTitle}>{title}</h2>
      {children}
    </section>
  )
}

function CompactGrid({ children }: { children: React.ReactNode }): JSX.Element {
  return <div style={compactGrid}>{children}</div>
}

function Chip({ label, active = false }: { label: string; active?: boolean }): JSX.Element {
  return <span style={active ? chipActive : chip}>{label}</span>
}

function Meta({ label, value }: { label: string; value: string }): JSX.Element {
  return (
    <div style={metaItem}>
      <span style={metaLabel}>{label}</span>
      <span style={metaValue}>{value}</span>
    </div>
  )
}

function filterLocalSkills(skills: SkillEntry[], query: string, filter: SourceFilter): SkillEntry[] {
  const q = query.trim().toLowerCase()
  return skills.filter((skill) => {
    if (filter === 'system' && skill.source !== 'builtin') return false
    if (filter === 'personal' && skill.source === 'builtin') return false
    if (filter === 'market') return false
    if (!q) return true
    return (
      skill.name.toLowerCase().includes(q) ||
      (skill.displayName ?? '').toLowerCase().includes(q) ||
      (skill.description ?? '').toLowerCase().includes(q) ||
      (skill.shortDescription ?? '').toLowerCase().includes(q)
    )
  })
}

function skillTitle(skill: SkillEntry): string {
  return skill.displayName || skill.name
}

function skillSubtitle(skill: SkillEntry, t: ReturnType<typeof useT>): string {
  return skill.shortDescription || skill.description || t('skillCard.noDescription')
}

function sourceLabel(skill: SkillEntry, t: ReturnType<typeof useT>): string {
  if (skill.source === 'builtin') return t('skills.source.system')
  if (skill.source === 'workspace') return t('skills.source.workspace')
  return t('skills.source.user')
}

function providerLabel(provider: SkillMarketProviderFilter): string {
  if (provider === 'skillhub') return 'SkillHub'
  if (provider === 'clawhub') return 'ClawHub'
  return 'All'
}

function stripYamlFrontmatter(s: string): string {
  if (!s.startsWith('---')) return s
  const m = s.match(/^---\r?\n[\s\S]*?\r?\n---\r?\n/)
  return m ? s.slice(m[0].length).trim() : s
}

const page: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  height: '100%',
  minHeight: 0,
  backgroundColor: 'var(--bg-primary)',
  color: 'var(--text-primary)'
}

const browseHeader: React.CSSProperties = {
  position: 'relative',
  flexShrink: 0,
  padding: '28px 64px 16px',
  borderBottom: '1px solid var(--border-subtle)'
}

const topActions: React.CSSProperties = {
  position: 'absolute',
  top: '16px',
  right: '24px',
  display: 'flex',
  gap: '8px',
  alignItems: 'center'
}

const heroTitle: React.CSSProperties = {
  margin: '0 0 24px',
  textAlign: 'center',
  fontSize: '26px',
  lineHeight: 1.2,
  fontWeight: 700,
  letterSpacing: 0
}

const searchRow: React.CSSProperties = {
  display: 'flex',
  gap: '8px',
  maxWidth: '760px',
  margin: '0 auto',
  alignItems: 'center'
}

const searchBox: React.CSSProperties = {
  flex: '1 1 320px',
  minWidth: 0,
  height: '36px',
  boxSizing: 'border-box',
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  padding: '0 11px',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-secondary)',
  color: 'var(--text-secondary)'
}

const searchInput: React.CSSProperties = {
  width: '100%',
  minWidth: 0,
  border: 'none',
  outline: 'none',
  backgroundColor: 'transparent',
  color: 'var(--text-primary)',
  fontSize: '13px'
}

const filterMenuButton: React.CSSProperties = {
  height: '36px',
  minWidth: '74px',
  boxSizing: 'border-box',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-secondary)',
  color: 'var(--text-primary)',
  padding: '0 10px',
  fontSize: '13px',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: '6px',
  cursor: 'pointer',
  lineHeight: 1,
  whiteSpace: 'nowrap'
}

const browseMain: React.CSSProperties = {
  flex: 1,
  minHeight: 0,
  overflow: 'auto',
  padding: '28px 64px 48px'
}

const sectionTitle: React.CSSProperties = {
  maxWidth: '760px',
  margin: '0 auto 12px',
  paddingTop: '4px',
  borderTop: '1px solid var(--border-subtle)',
  fontSize: '16px',
  lineHeight: 1.3,
  fontWeight: 700,
  color: 'var(--text-primary)'
}

const compactGrid: React.CSSProperties = {
  maxWidth: '760px',
  margin: '0 auto',
  display: 'grid',
  gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
  columnGap: '34px',
  rowGap: '18px'
}

const compactItem: React.CSSProperties = {
  width: '100%',
  minWidth: 0,
  height: '58px',
  display: 'flex',
  alignItems: 'center',
  gap: '12px',
  padding: '0 8px',
  border: 'none',
  borderRadius: '8px',
  backgroundColor: 'transparent',
  color: 'var(--text-primary)',
  cursor: 'pointer',
  textAlign: 'left'
}

const rowTitle: React.CSSProperties = {
  fontSize: '13px',
  lineHeight: 1.25,
  fontWeight: 700,
  color: 'var(--text-primary)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const rowDesc: React.CSSProperties = {
  marginTop: '4px',
  fontSize: '12px',
  lineHeight: 1.3,
  color: 'var(--text-secondary)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const statusIcon: React.CSSProperties = {
  minWidth: '28px',
  display: 'inline-flex',
  justifyContent: 'center',
  color: 'var(--text-dimmed)',
  fontSize: '11px',
  whiteSpace: 'nowrap'
}

function marketAction(skill: MarketSkillSummary): React.CSSProperties {
  return {
    ...statusIcon,
    color: skill.updateAvailable ? 'var(--warning)' : skill.installed ? 'var(--success)' : 'var(--text-primary)'
  }
}

const manageButton: React.CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  gap: '6px',
  height: '32px',
  padding: '0 12px',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-secondary)',
  color: 'var(--text-primary)',
  fontSize: '13px',
  boxSizing: 'border-box',
  cursor: 'pointer'
}

const iconButton: React.CSSProperties = {
  width: '32px',
  height: '32px',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-secondary)',
  color: 'var(--text-secondary)',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  boxSizing: 'border-box',
  cursor: 'pointer'
}

const manageHeader: React.CSSProperties = {
  flexShrink: 0,
  padding: '14px 64px 12px',
  borderBottom: '1px solid var(--border-subtle)'
}

const breadcrumb: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  color: 'var(--text-secondary)',
  fontSize: '13px'
}

const breadcrumbButton: React.CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '4px',
  border: 'none',
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  padding: 0,
  fontSize: '13px'
}

const breadcrumbSep: React.CSSProperties = {
  color: 'var(--text-dimmed)'
}

const breadcrumbCurrent: React.CSSProperties = {
  color: 'var(--text-primary)',
  fontWeight: 700
}

const manageToolbar: React.CSSProperties = {
  margin: '34px auto 0',
  maxWidth: '730px',
  display: 'flex',
  alignItems: 'center',
  gap: '8px'
}

const chip: React.CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  height: '28px',
  padding: '0 10px',
  borderRadius: '8px',
  backgroundColor: 'transparent',
  color: 'var(--text-secondary)',
  fontSize: '13px',
  whiteSpace: 'nowrap'
}

const chipActive: React.CSSProperties = {
  ...chip,
  backgroundColor: 'var(--bg-tertiary)',
  color: 'var(--text-primary)'
}

const savedHint: React.CSSProperties = {
  fontSize: '12px',
  color: 'var(--success)',
  whiteSpace: 'nowrap'
}

const manageMain: React.CSSProperties = {
  flex: 1,
  minHeight: 0,
  overflow: 'auto',
  padding: '28px 64px 48px'
}

const manageRow: React.CSSProperties = {
  maxWidth: '730px',
  margin: '0 auto',
  minHeight: '74px',
  display: 'flex',
  alignItems: 'center',
  gap: '12px'
}

const manageSource: React.CSSProperties = {
  width: '72px',
  color: 'var(--text-secondary)',
  fontSize: '13px',
  textAlign: 'left'
}

const emptyText: React.CSSProperties = {
  maxWidth: '760px',
  margin: '0 auto',
  fontSize: '13px',
  color: 'var(--text-secondary)'
}

const modalScrim: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 1000,
  backgroundColor: 'rgba(0,0,0,0.55)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '24px'
}

const modal: React.CSSProperties = {
  width: 'min(760px, 100%)',
  maxHeight: 'min(86vh, 920px)',
  backgroundColor: 'var(--bg-primary)',
  borderRadius: '12px',
  border: '1px solid var(--border-default)',
  boxShadow: '0 16px 48px rgba(0,0,0,0.45)',
  display: 'flex',
  flexDirection: 'column',
  overflow: 'hidden'
}

const modalHeader: React.CSSProperties = {
  padding: '16px 20px',
  borderBottom: '1px solid var(--border-default)',
  display: 'flex',
  alignItems: 'flex-start',
  justifyContent: 'space-between',
  gap: '12px',
  flexShrink: 0
}

const modalTitle: React.CSSProperties = {
  margin: 0,
  fontSize: '18px',
  fontWeight: 600,
  color: 'var(--text-primary)',
  wordBreak: 'break-word'
}

const modalSubtitle: React.CSSProperties = {
  margin: '8px 0 0',
  color: 'var(--text-secondary)',
  fontSize: '13px',
  lineHeight: 1.45
}

const metaRow: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(auto-fit, minmax(120px, 1fr))',
  gap: '10px',
  padding: '12px 20px',
  borderBottom: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-secondary)'
}

const metaItem: React.CSSProperties = {
  minWidth: 0
}

const metaLabel: React.CSSProperties = {
  display: 'block',
  fontSize: '11px',
  color: 'var(--text-dimmed)',
  textTransform: 'uppercase',
  marginBottom: '3px'
}

const metaValue: React.CSSProperties = {
  display: 'block',
  fontSize: '13px',
  color: 'var(--text-primary)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const modalBody: React.CSSProperties = {
  flex: 1,
  overflow: 'auto',
  padding: '16px 20px',
  minHeight: '220px'
}

const previewFallbackText: React.CSSProperties = {
  margin: 0,
  fontSize: '14px',
  lineHeight: 1.6,
  color: 'var(--text-secondary)',
  whiteSpace: 'pre-wrap'
}

const modalFooter: React.CSSProperties = {
  padding: '12px 20px',
  borderTop: '1px solid var(--border-default)',
  display: 'flex',
  justifyContent: 'flex-end',
  alignItems: 'center',
  gap: '8px',
  flexWrap: 'wrap'
}

const secondaryBtn: React.CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  padding: '7px 12px',
  fontSize: '13px',
  borderRadius: '6px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-tertiary)',
  color: 'var(--text-primary)',
  cursor: 'pointer'
}

const primaryBtn: React.CSSProperties = {
  ...secondaryBtn,
  backgroundColor: 'var(--accent)',
  borderColor: 'var(--accent)',
  color: 'var(--on-accent)'
}

const disabledSecondaryBtn: React.CSSProperties = {
  ...secondaryBtn,
  opacity: 0.55,
  cursor: 'not-allowed'
}

const disabledPrimaryBtn: React.CSSProperties = {
  ...primaryBtn,
  opacity: 0.65,
  cursor: 'not-allowed'
}

const iconCloseBtn: React.CSSProperties = {
  width: '32px',
  height: '32px',
  fontSize: '22px',
  lineHeight: 1,
  borderRadius: '6px',
  border: 'none',
  backgroundColor: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  flexShrink: 0
}
