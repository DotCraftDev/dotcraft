import { formatTokenCount } from '../../utils/formatTokens'

interface TokenUsageDisplayProps {
  inputTokens: number
  outputTokens: number
}

/**
 * Compact token usage counter shown during / after a running turn.
 * Format: ↑1.2k ↓345 tokens
 * Spec §10.3.5
 */
export function TokenUsageDisplay({ inputTokens, outputTokens }: TokenUsageDisplayProps): JSX.Element | null {
  if (inputTokens === 0 && outputTokens === 0) return null

  return (
    <span
      style={{
        fontSize: '11px',
        color: 'var(--text-dimmed)',
        whiteSpace: 'nowrap'
      }}
    >
      ↑{formatTokenCount(inputTokens)} ↓{formatTokenCount(outputTokens)} tokens
    </span>
  )
}
