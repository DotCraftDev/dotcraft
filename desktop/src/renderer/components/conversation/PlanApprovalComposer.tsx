import { useCallback, useEffect, useRef, useState, type CSSProperties } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useUIStore } from '../../stores/uiStore'
import { acceptPlanSentinelFor } from '../../utils/planAcceptSentinel'
import { startTurnWithOptimisticUI } from '../../utils/startTurn'
import { ComposerShell } from './ComposerShell'
import { RichInputArea, type RichInputAreaHandle } from './RichInputArea'

interface PlanApprovalComposerProps {
  threadId: string
  workspacePath: string
  turnId: string
}

export function PlanApprovalComposer({
  threadId,
  workspacePath,
  turnId
}: PlanApprovalComposerProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const [editorFocused, setEditorFocused] = useState(false)
  const [, setContentRevision] = useState(0)
  const [hoverYes, setHoverYes] = useState(false)
  const richRef = useRef<RichInputAreaHandle>(null)
  const sendInFlightRef = useRef(false)
  const setThreadMode = useConversationStore((s) => s.setThreadMode)
  const dismissPlanApproval = useUIStore((s) => s.dismissPlanApproval)

  const text = richRef.current?.getText() ?? ''
  const trimmed = text.trim()
  const submitAsNo = trimmed.length > 0

  const handleAcceptPlan = useCallback(async (): Promise<void> => {
    if (sendInFlightRef.current) return
    sendInFlightRef.current = true
    dismissPlanApproval(turnId)
    setThreadMode('agent')
    try {
      await window.api.appServer.sendRequest('thread/mode/set', { threadId, mode: 'agent' })
    } catch (err) {
      console.error('thread/mode/set failed:', err)
    }
    await startTurnWithOptimisticUI({
      threadId,
      workspacePath,
      text: acceptPlanSentinelFor(locale),
      fallbackThreadName: t('toast.imageMessage'),
      fileFallbackThreadName: t('toast.fileReferenceMessage'),
      attachmentFallbackThreadName: t('toast.attachmentMessage'),
      includeUserPreview: false,
      renameThreadFromText: false
    })
    sendInFlightRef.current = false
  }, [dismissPlanApproval, locale, setThreadMode, t, threadId, turnId, workspacePath])

  async function handleSubmit(): Promise<void> {
    if (sendInFlightRef.current) return
    if (!submitAsNo) {
      await handleAcceptPlan()
      return
    }
    sendInFlightRef.current = true
    const started = await startTurnWithOptimisticUI({
      threadId,
      workspacePath,
      text: trimmed,
      fallbackThreadName: t('toast.imageMessage'),
      fileFallbackThreadName: t('toast.fileReferenceMessage'),
      attachmentFallbackThreadName: t('toast.attachmentMessage')
    })
    if (started) {
      dismissPlanApproval(turnId)
      richRef.current?.clear()
    }
    sendInFlightRef.current = false
  }

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        dismissPlanApproval(turnId)
        return
      }
      if (event.key === '1' && !event.metaKey && !event.ctrlKey && !event.altKey && !editorFocused) {
        event.preventDefault()
        void handleAcceptPlan()
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [dismissPlanApproval, editorFocused, handleAcceptPlan, turnId])

  return (
    <div style={{ flexShrink: 0 }}>
      <ComposerShell
        dragOver={false}
        dropLabel=""
        onDragOver={(e) => e.preventDefault()}
        onDragLeave={(e) => e.preventDefault()}
        onDrop={(e) => e.preventDefault()}
        focused={editorFocused}
        editor={(
          <div style={{ display: 'grid', gap: '8px' }}>
            <div style={{ color: 'var(--text-primary)', fontSize: '14px', fontWeight: 600 }}>
              {t('planApproval.title')}
            </div>
            <button
              type="button"
              onMouseEnter={() => setHoverYes(true)}
              onMouseLeave={() => setHoverYes(false)}
              onClick={() => {
                void handleAcceptPlan()
              }}
              style={{
                border: '1px solid var(--border-default)',
                background: hoverYes ? 'var(--bg-tertiary)' : 'var(--bg-primary)',
                borderRadius: '10px',
                color: 'var(--text-primary)',
                textAlign: 'left',
                padding: '0 10px',
                minHeight: '40px',
                cursor: 'pointer',
                fontSize: '13px',
                display: 'flex',
                alignItems: 'center',
                gap: '8px'
              }}
            >
              <span style={{ color: 'var(--text-dimmed)' }}>1.</span>
              {t('planApproval.yes')}
            </button>
            <div
              style={{
                border: '1px solid var(--border-default)',
                borderRadius: '10px',
                padding: '0 10px',
                minHeight: '40px',
                display: 'flex',
                alignItems: 'center',
                gap: '8px'
              }}
            >
              <span style={{ color: 'var(--text-dimmed)', fontSize: '13px', lineHeight: 1, flexShrink: 0 }}>2.</span>
              <div style={{ flex: 1, minWidth: 0 }}>
                <RichInputArea
                  ref={richRef}
                  chrome="minimal"
                  placeholder={t('planApproval.noPlaceholder')}
                  onSubmit={() => {
                    void handleSubmit()
                  }}
                  onAtQuery={() => {}}
                  onSlashQuery={() => {}}
                  onContentChange={() => {
                    setContentRevision((n) => n + 1)
                  }}
                  onFocusChange={setEditorFocused}
                />
              </div>
            </div>
          </div>
        )}
        footerLeading={<div />}
        footerAction={(
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <button
              type="button"
              onClick={() => dismissPlanApproval(turnId)}
              aria-label={t('planApproval.escKey')}
              title={t('planApproval.escKey')}
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: '6px',
                border: 'none',
                background: 'transparent',
                color: 'var(--text-dimmed)',
                fontSize: '12px',
                cursor: 'pointer',
                padding: 0
              }}
            >
              <span style={kbdChipStyle}>Esc</span>
              <span>{t('planApproval.dismissHint')}</span>
            </button>
            <button
              type="button"
              onClick={() => {
                void handleSubmit()
              }}
              disabled={sendInFlightRef.current}
              style={{
                height: '32px',
                borderRadius: '999px',
                border: '1px solid var(--border-default)',
                padding: '0 14px',
                background: 'var(--bg-primary)',
                color: 'var(--text-primary)',
                cursor: sendInFlightRef.current ? 'default' : 'pointer',
                opacity: sendInFlightRef.current ? 0.65 : 1,
                fontSize: '12px',
                fontWeight: 600
              }}
            >
              {t('planApproval.submit')}
            </button>
          </div>
        )}
      />
    </div>
  )
}

const kbdChipStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  minWidth: '28px',
  height: '20px',
  padding: '0 6px',
  borderRadius: '4px',
  border: '1px solid var(--border-default)',
  background: 'var(--bg-secondary)',
  color: 'var(--text-secondary)',
  fontSize: '11px',
  fontFamily: 'var(--font-mono, ui-monospace)'
}
