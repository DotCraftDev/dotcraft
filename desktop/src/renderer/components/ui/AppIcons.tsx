import type { CSSProperties, JSX } from 'react'
import { RotateCw } from 'lucide-react'

export function FolderIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <path
        d="M2.5 5.5A1.5 1.5 0 0 1 4 4h3.2l1.2 1.3h7.6A1.5 1.5 0 0 1 17.5 6.8v8.7A1.5 1.5 0 0 1 16 17H4a1.5 1.5 0 0 1-1.5-1.5v-10Z"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinejoin="round"
      />
    </svg>
  )
}

export function RefreshIcon({ size = 16, style }: { size?: number; style?: CSSProperties }): JSX.Element {
  return <RotateCw size={size} strokeWidth={1.8} style={style} aria-hidden="true" />
}

export function CheckCircleIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <circle cx="10" cy="10" r="7" stroke="currentColor" strokeWidth="1.6" />
      <path
        d="m6.8 10.1 2.1 2.1 4.4-4.7"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

export function OpenInBrowserIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <path
        d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M15 3h6v6"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M10 14 21 3"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

export function SparkIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <path
        d="M12 3.2c0 2.6.9 4.5 2.4 6 1.5 1.5 3.4 2.4 6 2.4-2.6 0-4.5.9-6 2.4-1.5 1.5-2.4 3.4-2.4 6 0-2.6-.9-4.5-2.4-6-1.5-1.5-3.4-2.4-6-2.4 2.6 0 4.5-.9 6-2.4 1.5-1.5 2.4-3.4 2.4-6Z"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinejoin="round"
      />
    </svg>
  )
}

export function AutomationIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <circle cx="6" cy="5" r="2" />
      <circle cx="6" cy="19" r="2" />
      <circle cx="17" cy="5" r="2" />
      <path d="M6 7v10" />
      <path d="M6 12C6 8 8 5 12 5h3" />
    </svg>
  )
}

export function BranchIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle cx="6" cy="5.5" r="2.2" stroke="currentColor" strokeWidth="1.7" />
      <circle cx="6" cy="18.5" r="2.2" stroke="currentColor" strokeWidth="1.7" />
      <circle cx="18" cy="12" r="2.2" stroke="currentColor" strokeWidth="1.7" />
      <path
        d="M6 7.7v8.6"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
      />
      <path
        d="M8.1 5.8h4.4a3.3 3.3 0 0 1 3.3 3.3v1.1"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M8.1 18.2h4.4a3.3 3.3 0 0 0 3.3-3.3v-1.1"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

export function DesktopIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <rect x="3" y="4.5" width="18" height="12" rx="2.2" />
      <path d="M9 20h6" />
      <path d="M12 16.5V20" />
    </svg>
  )
}

export function BotIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <rect x="4" y="7" width="16" height="11" rx="4" stroke="currentColor" strokeWidth="1.8" />
      <path d="M12 4v3" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
      <circle cx="9" cy="12.5" r="1" fill="currentColor" />
      <circle cx="15" cy="12.5" r="1" fill="currentColor" />
      <path d="M9 15.5h6" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
    </svg>
  )
}

export function IDEIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <rect
        x="3"
        y="4.5"
        width="18"
        height="15"
        rx="2.4"
        stroke="currentColor"
        strokeWidth="1.7"
      />
      <path d="M3 8.5h18" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      <circle cx="5.8" cy="6.5" r=".8" fill="currentColor" />
      <circle cx="8.4" cy="6.5" r=".8" fill="currentColor" />
      <path
        d="m9.8 11.8-2 2 2 2"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="m14.2 11.8 2 2-2 2"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="m12.8 10.4-1.6 6.6"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
      />
    </svg>
  )
}

export function TerminalIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <rect
        x="3"
        y="4.5"
        width="18"
        height="15"
        rx="2.4"
        stroke="currentColor"
        strokeWidth="1.7"
      />
      <path
        d="m7.4 9.6 2.8 2.4-2.8 2.4"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M12.6 14.8h4"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
      />
    </svg>
  )
}

export function ClockIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <circle cx="12" cy="12" r="9" />
      <path d="M12 12V8" />
      <path d="M12 12h4" />
    </svg>
  )
}

export function HeartbeatIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <path
        d="M3 12.2h3.6l1.7-3.8 2.6 8.2 2.4-6 1.7 3.6H20.4"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <circle cx="20.4" cy="14.2" r="1.1" fill="currentColor" />
    </svg>
  )
}
