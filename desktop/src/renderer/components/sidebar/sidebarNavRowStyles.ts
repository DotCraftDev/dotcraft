import type { CSSProperties } from 'react'

/** Shared geometry for Automations, Skills, Settings, and connection row (sidebar bottom). */
export const SIDEBAR_NAV_ROW_OUTER: CSSProperties = {
  width: 'calc(100% - 8px)',
  margin: '2px 4px',
  padding: '8px 12px',
  borderRadius: '6px',
  border: 'none',
  fontSize: '13px',
  textAlign: 'left',
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  boxSizing: 'border-box'
}

export const SIDEBAR_NAV_ICON_SLOT: CSSProperties = {
  display: 'flex',
  width: 18,
  height: 18,
  flexShrink: 0,
  alignItems: 'center',
  justifyContent: 'center',
  lineHeight: 0
}

export const SIDEBAR_NAV_LABEL: CSSProperties = {
  lineHeight: 1.2
}

export const SIDEBAR_NAV_BORDER_INACTIVE: CSSProperties = {
  borderLeft: '3px solid transparent'
}
