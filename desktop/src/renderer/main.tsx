import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import { applyTheme } from './utils/theme'

applyTheme('dark')

const rootElement = document.getElementById('root')
if (!rootElement) {
  throw new Error('Root element not found — check index.html for <div id="root">')
}

createRoot(rootElement).render(
  <StrictMode>
    <App />
  </StrictMode>
)
