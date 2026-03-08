"use client";

import { Component, type ReactNode } from "react";

function isAbortError(error: unknown): boolean {
  if (error instanceof Error) {
    if (error.name === "AbortError") return true;
    const msg = error.message?.toLowerCase() ?? "";
    if (msg.includes("abort") || msg.includes("signal is aborted")) return true;
  }
  return false;
}

type Props = { children: ReactNode };

type State = { error: Error | null };

/**
 * Catches AbortError (and similar) thrown during React effect cleanup when
 * switching threads or unmounting CopilotChat, and suppresses them so they
 * don't surface to the user.
 */
export class AbortErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    if (isAbortError(error)) {
      return { error: null };
    }
    return { error };
  }

  componentDidCatch(error: Error, errorInfo: React.ErrorInfo) {
    if (isAbortError(error)) {
      return;
    }
    console.error("Uncaught error:", error, errorInfo);
  }

  render() {
    if (this.state.error) {
      throw this.state.error;
    }
    return this.props.children;
  }
}
