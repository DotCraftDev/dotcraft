import { useRef, useState, lazy, Suspense } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { useConversationStore } from '../../stores/conversationStore'
import { FilePlus2, ListChecks, SquareTerminal, Plus, FileText, Image, FileType2, X, Globe } from 'lucide-react'
import { ChangesTab } from '../detail/ChangesTab'
import { PlanTab } from '../detail/PlanTab'
import { TerminalTab } from '../detail/TerminalTab'
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
 * Detail Panel — system tabs (Changes / Plan / Terminal) + dynamic viewer tabs.
 *
 * Tab bar layout:
 *   [changes] [plan] [terminal] │ [viewer1] [viewer2] … │ [+] [flex] [×]
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
  const turns = useConversationStore((s) => s.turns)

  const changedFileCount = changedFiles.size
  const terminalCount = turns.reduce(
    (acc, turn) => acc + turn.items.filter((i) => i.type === 'commandExecution').length,
    0
  )

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
    },
    {
      id: 'terminal' as const,
      label: t('detailPanel.tabTerminal'),
      icon: <SquareTerminal size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />,
      badge: terminalCount > 0 ? terminalCount : undefined
    }
  ]

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        backgroundColor: 'var(--bg-secondary)'
      }}
    >
      {/* ── Tab bar ── */}
      <div
        style={{
          display: 'flex',
          alignItems: 'stretch',
          height: 'var(--chrome-header-height)',
          boxSizing: 'border-box',
          borderBottom: '1px solid var(--border-default)',
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
              title={tab.kind === 'browser' ? tab.currentUrl : tab.absolutePath}
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

        {/* Close panel button */}
        <button
          onClick={toggleDetailPanel}
          title={t('detailPanel.closeTitle')}
          style={{
            alignSelf: 'center',
            width: '28px',
            height: '28px',
            borderRadius: '4px',
            backgroundColor: 'transparent',
            border: 'none',
            color: 'var(--text-secondary)',
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: '16px',
            marginRight: '4px',
            flexShrink: 0
          }}
          aria-label={t('detailPanel.closeAria')}
        >
          ×
        </button>
      </div>

      {/* ── Panel body ── */}
      <div style={{ flex: 1, overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
        {activeDetailTab.kind === 'system' && activeDetailTab.id === 'changes' && (
          <ChangesTab workspacePath={workspacePath} />
        )}
        {activeDetailTab.kind === 'system' && activeDetailTab.id === 'plan' && <PlanTab />}
        {activeDetailTab.kind === 'system' && activeDetailTab.id === 'terminal' && <TerminalTab />}
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
