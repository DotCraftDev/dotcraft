import type { JSX, ReactNode } from 'react'

interface ChannelsPanelProps {
  children: ReactNode
}

export function ChannelsPanel({ children }: ChannelsPanelProps): JSX.Element {
  return <>{children}</>
}
