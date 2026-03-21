import { memo, useState } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import rehypeHighlight from 'rehype-highlight'
// Import highlight.js dark theme CSS
import 'highlight.js/styles/github-dark.css'
import type { Components } from 'react-markdown'

interface MarkdownRendererProps {
  content: string
}

/**
 * Renders markdown content using react-markdown with GFM and syntax highlighting.
 * Memoized to avoid re-rendering finalized messages in the turn history.
 * Spec §10.3.3
 */
export const MarkdownRenderer = memo(function MarkdownRenderer({
  content
}: MarkdownRendererProps): JSX.Element {
  return (
    <div className="markdown-body" style={markdownContainerStyle}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        rehypePlugins={[rehypeHighlight]}
        components={customComponents}
      >
        {content}
      </ReactMarkdown>
    </div>
  )
})

// ---------------------------------------------------------------------------
// Custom components
// ---------------------------------------------------------------------------

const customComponents: Components = {
  // Open links in external browser (Electron)
  a({ href, children, ...props }) {
    function handleClick(e: React.MouseEvent<HTMLAnchorElement>): void {
      e.preventDefault()
      if (href) window.open(href, '_blank', 'noopener,noreferrer')
    }
    return (
      <a
        href={href}
        onClick={handleClick}
        style={{ color: 'var(--info)', textDecoration: 'underline', cursor: 'pointer' }}
        {...props}
      >
        {children}
      </a>
    )
  },

  // Inline code
  code({ children, className, ...props }) {
    // react-markdown passes className="language-xxx" for fenced code blocks
    // Fenced blocks are handled by the pre/code combo; inline code has no className
    const isBlock = Boolean(className)
    if (!isBlock) {
      return (
        <code
          style={{
            fontFamily: 'var(--font-mono)',
            fontSize: '0.875em',
            backgroundColor: 'var(--bg-tertiary)',
            padding: '1px 5px',
            borderRadius: '3px',
            color: 'var(--text-primary)'
          }}
          {...props}
        >
          {children}
        </code>
      )
    }
    return (
      <code className={className} {...props}>
        {children}
      </code>
    )
  },

  // Code blocks wrapper — with copy button
  pre({ children, ...props }) {
    return <CodeBlock {...props}>{children}</CodeBlock>
  },

  // Blockquote
  blockquote({ children, ...props }) {
    return (
      <blockquote
        style={{
          borderLeft: '3px solid var(--border-active)',
          paddingLeft: '12px',
          margin: '8px 0',
          color: 'var(--text-secondary)',
          fontStyle: 'italic'
        }}
        {...props}
      >
        {children}
      </blockquote>
    )
  },

  // Table
  table({ children, ...props }) {
    return (
      <div style={{ overflowX: 'auto', margin: '8px 0' }}>
        <table
          style={{
            borderCollapse: 'collapse',
            width: '100%',
            fontSize: '13px'
          }}
          {...props}
        >
          {children}
        </table>
      </div>
    )
  },

  th({ children, ...props }) {
    return (
      <th
        style={{
          padding: '6px 12px',
          borderBottom: '1px solid var(--border-active)',
          textAlign: 'left',
          fontWeight: 600,
          color: 'var(--text-primary)'
        }}
        {...props}
      >
        {children}
      </th>
    )
  },

  td({ children, ...props }) {
    return (
      <td
        style={{
          padding: '6px 12px',
          borderBottom: '1px solid var(--border-default)',
          color: 'var(--text-secondary)'
        }}
        {...props}
      >
        {children}
      </td>
    )
  }
}

// ---------------------------------------------------------------------------
// Code block with copy button
// ---------------------------------------------------------------------------

function CodeBlock({ children, ...props }: React.HTMLAttributes<HTMLPreElement>): JSX.Element {
  const [copied, setCopied] = useState(false)

  function handleCopy(): void {
    // Extract text content recursively from children
    const text = extractText(children)
    navigator.clipboard.writeText(text).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 1800)
    }).catch(() => {})
  }

  return (
    <div style={{ position: 'relative', margin: '8px 0' }}>
      <pre
        style={{
          backgroundColor: '#0d1117',
          borderRadius: '6px',
          padding: '12px 16px',
          paddingTop: '36px',
          overflowX: 'auto',
          fontFamily: 'var(--font-mono)',
          fontSize: '13px',
          lineHeight: 1.5,
          margin: 0
        }}
        {...props}
      >
        {children}
      </pre>
      <button
        onClick={handleCopy}
        aria-label="Copy code"
        title="Copy code"
        style={{
          position: 'absolute',
          top: '6px',
          right: '8px',
          padding: '3px 8px',
          fontSize: '11px',
          background: copied ? 'var(--success)' : 'rgba(255,255,255,0.08)',
          border: '1px solid rgba(255,255,255,0.12)',
          borderRadius: '4px',
          color: copied ? '#fff' : 'rgba(255,255,255,0.6)',
          cursor: 'pointer',
          transition: 'background-color 150ms ease, color 150ms ease',
          fontFamily: 'var(--font-sans)'
        }}
      >
        {copied ? 'Copied!' : 'Copy'}
      </button>
    </div>
  )
}

function extractText(node: React.ReactNode): string {
  if (typeof node === 'string') return node
  if (typeof node === 'number') return String(node)
  if (!node) return ''
  if (Array.isArray(node)) return node.map(extractText).join('')
  if (typeof node === 'object' && 'props' in (node as React.ReactElement)) {
    return extractText((node as React.ReactElement).props.children)
  }
  return ''
}

// ---------------------------------------------------------------------------
// Container styles
// ---------------------------------------------------------------------------

const markdownContainerStyle: React.CSSProperties = {
  color: 'var(--text-primary)',
  fontSize: '14px',
  lineHeight: 1.6,
  wordBreak: 'break-word'
}
