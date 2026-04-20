/**
 * Shared types for the Desktop Viewer Panel (M1).
 * Used by main process (IPC handlers) and renderer (store, components).
 */

/** Currently only 'file'; 'browser' will be added in M2. */
export type ViewerKind = 'file'

/** Content class resolved for an opened file. */
export type ViewerContentClass = 'text' | 'image' | 'pdf' | 'unsupported'

/** A single viewer tab descriptor, owned by a specific thread. */
export interface ViewerTab {
  /** Stable uuid created at openFile time. */
  id: string
  /** Kind of viewer tab — always 'file' in M1. */
  kind: ViewerKind
  /** Normalized absolute path (realpath-resolved by main). */
  absolutePath: string
  /** Workspace-relative path used for label derivation. */
  relativePath: string
  /** Display label shown in the tab strip, possibly disambiguated (§5.4). */
  label: string
  /** Resolved content class used to pick the viewer component. */
  contentClass: ViewerContentClass
  /** File size in bytes at classification time; used by image viewer for info display. */
  sizeBytes?: number
  /**
   * If set, the tab renders an in-tab error state instead of the viewer.
   * Used when the file disappears after a tab is saved (§9.5).
   */
  errorMessage?: string
}

/** Per-thread viewer tab state stored in viewerTabStore. */
export interface PerThreadViewerState {
  /** Ordered list of open viewer tabs (insertion order). */
  tabs: ViewerTab[]
  /** Id of the currently active viewer tab, or null if none is active. */
  activeTabId: string | null
}

/** Result returned by `workspace:viewer:classify`. */
export interface ClassifyResult {
  contentClass: ViewerContentClass
  /** MIME type hint derived from extension / magic bytes. */
  mime: string
  /** File size in bytes at classification time. */
  sizeBytes: number
}

/** Result returned by `workspace:viewer:read-text`. */
export interface ReadTextResult {
  /** Decoded text content. */
  text: string
  /** True if the file was truncated to stay within `limitBytes`. */
  truncated: boolean
  /** Encoding that was used (currently always 'utf-8'). */
  encoding: string
}

/** Parameters for `workspace:viewer:list-files`. */
export interface ListFilesParams {
  workspacePath: string
  query: string
  limit: number
}

/** Parameters for `workspace:viewer:classify`. */
export interface ClassifyParams {
  absolutePath: string
}

/** Parameters for `workspace:viewer:read-text`. */
export interface ReadTextParams {
  absolutePath: string
  limitBytes?: number
}
