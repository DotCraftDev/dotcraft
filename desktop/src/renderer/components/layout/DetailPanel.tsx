import { useRef, useState, lazy, Suspense } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { useConversationStore } from '../../stores/conversationStore'
import { FilePlus2, ListChecks, SquareTerminal, Plus, FileText, Image, FileType2, X, Globe, PanelRightClose } from 'lucide-react'
import { ChangesTab } from '../detail/ChangesTab'
import { PlanTab } from '../detail/PlanTab'
import { AddTabPopup } from '../detail/AddTabPopup'
import type { ViewerContentClass } from '../../../shared/viewer/types'

interface DetailPanelProps {
  workspacePath?: string
}

function contentClassIcon(contentClass: ViewerContentClass): JSX.Element {
  switch (contentClass) {
    case 'image':
      return <Image size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
    case 'pdf':
      return <FileType2 size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
    default:
      return <FileText size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
  }
}

function browserTabIcon(faviconDataUrl?: string): JSX.Element {
  if (faviconDataUrl) {
    return (
      <img
        src={faviconDataUrl}
        alt=""
        width={14}
        height={14}
        style={{ display: 'block', borderRadius: '2px', flexShrink: 0 }}
      />
    )
  }
  return <Globe size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
}

/**
 * Detail Panel — system tabs (Changes / Plan) + dynamic viewer tabs.
 *
 * Tab bar layout:
 *   [changes] [plan] │ [viewer1] [viewer2] … │ [+] [flex] [×]
 */
export function DetailPanel({ workspacePath = '' }: DetailPanelProps): JSX.Element {
  const t = useT()
  const {
    activeDetailTab,
    lastActiveSystemTab,
    setActiveDetailTab,
    setActiveViewerTab,
    closeViewerTab,
    toggleDetailPanel
  } = useUIStore()

  const currentThreadId = useViewerTabStore((s) => s.currentThreadId)
  const viewerTabs = useViewerTabStore((s) => s.getThreadState(s.currentThreadId ?? '').tabs)
  const closeViewerTabInStore = useViewerTabStore((s) => s.closeTab)

  const changedFiles = useConversationStore((s) => s.changedFiles)

  const changedFileCount = changedFiles.size

  const [addPopupOpen, setAddPopupOpen] = useState(false)
  const addButtonRef = useRef<HTMLButtonElement>(null)

  const isSystemTab = activeDetailTab.kind === 'system'
  const activeSystemId = isSystemTab ? activeDetailTab.id : lastActiveSystemTab
  const activeViewerId = !isSystemTab ? activeDetailTab.id : null

  const handleCloseViewerTab = (tabId: string): void => {
    if (!currentThreadId) return
    const closing = viewerTabs.find((t) => t.id === tabId)
    if (closing?.kind === 'browser') {
      void window.api.workspace.viewer.browser.destroy({ tabId: closing.id })
    } else if (closing?.kind === 'terminal') {
      void window.api.workspace.viewer.terminal.dispose({ tabId: closing.id })
    }
    closeViewerTabInStore(currentThreadId, tabId)

    // If we just closed the active tab, we need to figure out new active tab
    const remaining = viewerTabs.filter((t) => t.id !== tabId)
    const wasActive = activeDetailTab.kind === 'viewer' && activeDetailTab.id === tabId

    if (wasActive) {
      if (remaining.length > 0) {
        // Nearest neighbor was already handled by the store — we need to read it
        const idx = viewerTabs.findIndex((t) => t.id === tabId)
        const newActive = idx > 0
          ? remaining[idx - 1]
          : remaining[0]
        if (newActive) {
          setActiveViewerTab(newActive.id)
        } else {
          closeViewerTab()
        }
      } else {
        closeViewerTab()
      }
    }
  }

  const systemTabs = [
    {
      id: 'changes' as const,
      label: t('detailPanel.tabChanges'),
      icon: <FilePlus2 size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />,
      badge: changedFileCount > 0 ? changedFileCount : undefined
    },
    {
      id: 'plan' as const,
      label: t('detailPanel.tabPlan'),
      icon: <ListChecks size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
    }
  ]

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        backgroundColor: 'var(--bg-primary)'
      }}
    >
      {/* ── Tab bar ── */}
      {/* No borderBottom here — the unified header line is painted at the
          ThreePanel level so it stays continuous across the DragHandle. */}
      <div
        style={{
          display: 'flex',
          alignItems: 'stretch',
          height: 'var(--chrome-header-height)',
          boxSizing: 'border-box',
          flexShrink: 0,
          paddingLeft: '4px',
          overflowX: 'auto',
          overflowY: 'hidden',
          scrollbarWidth: 'none'
        }}
      >
        {/* System tabs */}
        {systemTabs.map((tab) => {
          const isActive = isSystemTab && activeSystemId === tab.id
          return (
            <button
              key={tab.id}
              onClick={() => setActiveDetailTab(tab.id)}
              title={tab.label}
              aria-label={tab.label}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '5px',
                height: '100%',
                padding: '0 10px',
                fontSize: '13px',
                fontWeight: isActive ? 500 : 400,
                color: isActive ? 'var(--text-primary)' : 'var(--text-secondary)',
                backgroundColor: 'transparent',
                border: 'none',
                boxSizing: 'border-box',
                boxShadow: isActive ? 'inset 0 -2px 0 var(--accent)' : 'none',
                cursor: 'pointer',
                flexShrink: 0,
                transition: 'color 100ms ease, box-shadow 100ms ease'
              }}
            >
              {tab.icon}
              {tab.badge !== undefined && (
                <span
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    minWidth: '16px',
                    height: '16px',
                    padding: '0 4px',
                    borderRadius: '8px',
                    background: isActive ? 'var(--accent)' : 'var(--bg-tertiary)',
                    color: isActive ? '#ffffff' : 'var(--text-secondary)',
                    fontSize: '10px',
                    fontWeight: 500
                  }}
                >
                  {tab.badge}
                </span>
              )}
            </button>
          )
        })}

        {/* Separator — only visible when viewer tabs exist */}
        {viewerTabs.length > 0 && (
          <div
            aria-hidden
            style={{
              alignSelf: 'center',
              width: '1px',
              height: '16px',
              backgroundColor: 'var(--border-default)',
              flexShrink: 0,
              margin: '0 4px'
            }}
          />
        )}

        {/* Viewer tabs */}
        {viewerTabs.map((tab) => {
          const isActive = !isSystemTab && activeViewerId === tab.id
          return (
            <div
              key={tab.id}
              role="tab"
              aria-selected={isActive}
              title={
                tab.kind === 'browser'
                  ? tab.currentUrl
                  : tab.kind === 'terminal'
                    ? tab.cwd
                    : tab.absolutePath
              }
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '4px',
                height: '100%',
                padding: '0 6px 0 10px',
                fontSize: '13px',
                fontWeight: isActive ? 500 : 400,
                color: isActive ? 'var(--text-primary)' : 'var(--text-secondary)',
                backgroundColor: 'transparent',
                boxSizing: 'border-box',
                boxShadow: isActive ? 'inset 0 -2px 0 var(--accent)' : 'none',
                cursor: 'pointer',
                flexShrink: 0,
                transition: 'color 100ms ease, box-shadow 100ms ease',
                userSelect: 'none',
                maxWidth: '160px'
              }}
              onClick={() => setActiveViewerTab(tab.id)}
              onAuxClick={(e) => {
                // Middle-click to close
                if (e.button === 1) {
                  e.preventDefault()
                  handleCloseViewerTab(tab.id)
                }
              }}
            >
              {tab.kind === 'browser'
                ? browserTabIcon(tab.faviconDataUrl)
                : tab.kind === 'terminal'
                  ? <SquareTerminal size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
                  : contentClassIcon(tab.contentClass)}
              <span
                style={{
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                  maxWidth: '100px'
                }}
              >
                {tab.label}
              </span>
              <button
                onClick={(e) => {
                  e.stopPropagation()
                  handleCloseViewerTab(tab.id)
                }}
                aria-label={`${t('viewer.close')} ${tab.label}`}
                title={t('viewer.close')}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  width: '16px',
                  height: '16px',
                  borderRadius: '3px',
                  border: 'none',
                  background: 'transparent',
                  color: 'var(--text-secondary)',
                  cursor: 'pointer',
                  padding: 0,
                  flexShrink: 0,
                  opacity: isActive ? 1 : 0
                }}
                onMouseEnter={(e) => {
                  ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-hover, rgba(255,255,255,0.1))'
                  ;(e.currentTarget as HTMLButtonElement).style.opacity = '1'
                }}
                onMouseLeave={(e) => {
                  ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
                  ;(e.currentTarget as HTMLButtonElement).style.opacity = isActive ? '1' : '0'
                }}
              >
                <X size={10} aria-hidden style={{ display: 'block' }} />
              </button>
            </div>
          )
        })}

        {/* Add tab (+) button */}
        <button
          ref={addButtonRef}
          onClick={() => setAddPopupOpen((v) => !v)}
          aria-label={t('detailPanel.addTab')}
          title={t('detailPanel.addTab')}
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            height: '100%',
            padding: '0 6px',
            border: 'none',
            background: 'transparent',
            color: 'var(--text-secondary)',
            cursor: 'pointer',
            flexShrink: 0
          }}
        >
          <Plus size={14} aria-hidden style={{ display: 'block' }} />
        </button>

        <div style={{ flex: 1 }} />

        {/* Close panel button — ghost icon, mirrors ThreadHeader's open-panel button */}
        <button
          onClick={toggleDetailPanel}
          title={t('detailPanel.closeTitle')}
          aria-label={t('detailPanel.closeAria')}
          style={{
            alignSelf: 'center',
            width: '28px',
            height: '28px',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            padding: 0,
            border: 'none',
            borderRadius: '6px',
            backgroundColor: 'transparent',
            color: 'var(--text-secondary)',
            cursor: 'pointer',
            flexShrink: 0,
            marginRight: '4px',
            transition: 'background-color 100ms ease, color 100ms ease'
          }}
          onMouseEnter={(e) => {
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-tertiary)'
            ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-primary)'
          }}
          onMouseLeave={(e) => {
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
            ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-secondary)'
          }}
        >
          <PanelRightClose size={16} aria-hidden />
        </button>
      </div>

      {/* ── Panel body ──
          The 1px inset shadow on the left draws the vertical arm of the
          Codex-style T divider, starting exactly below the overlay header
          line. Using inset shadow (not borderLeft) avoids a 1px layout shift. */}
      <div
        style={{
          flex: 1,
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
          boxShadow: 'inset 1px 0 0 0 var(--border-default)'
        }}
      >
        {activeDetailTab.kind === 'system' && activeDetailTab.id === 'changes' && (
          <ChangesTab workspacePath={workspacePath} />
        )}
        {activeDetailTab.kind === 'system' && activeDetailTab.id === 'plan' && <PlanTab />}
        {activeDetailTab.kind === 'viewer' && (
          <ViewerTabContainer tabId={activeDetailTab.id} />
        )}
      </div>

      {/* AddTabPopup — rendered as portal above everything */}
      {addPopupOpen && (
        <AddTabPopup
          anchorRef={addButtonRef}
          onClose={() => setAddPopupOpen(false)}
        />
      )}
    </div>
  )
}

const LazyViewerTab = lazy(() => import('../detail/ViewerTab').then((m) => ({ default: m.ViewerTab })))

/** Lazy-loads the ViewerTab component to avoid shipping Monaco in the initial bundle. */
function ViewerTabContainer({ tabId }: { tabId: string }): JSX.Element {
  return (
    <Suspense fallback={
      <div style={{ padding: '24px', color: 'var(--text-secondary)', fontSize: '13px' }}>
        Loading…
      </div>
    }>
      <LazyViewerTab tabId={tabId} />
    </Suspense>
  )
}
