import type { JSX, ReactNode } from 'react'

interface ProxyPanelProps {
  children: ReactNode
}

export function ProxyPanel({ children }: ProxyPanelProps): JSX.Element {
  return <>{children}</>
}
