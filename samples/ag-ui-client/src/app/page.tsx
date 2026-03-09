"use client";

import {
  CopilotChat,
  CopilotKitProvider,
  useConfigureSuggestions,
  useAgentContext,
  useCopilotKit,
} from "@copilotkitnext/react";
import type { ToolsMenuItem } from "@copilotkitnext/react";
import { useEffect, useMemo } from "react";
import { Nav } from "@/components/Nav";
import { ThreadList } from "@/components/ThreadList";
import { AbortErrorBoundary } from "@/components/AbortErrorBoundary";
import { ThreadsProvider, useThreads } from "@/contexts/ThreadsContext";
import { toolRenderers } from "@/lib/toolRenderers";

const DEFAULT_DASHBOARD_URL = "http://localhost:5101/dashboard";

type SuggestionsAvailable =
  | "always"
  | "after-first-message"
  | "before-first-message"
  | "disabled";

function getSuggestionsAvailable(): SuggestionsAvailable {
  const v = process.env.NEXT_PUBLIC_SUGGESTIONS_AVAILABLE?.toLowerCase();
  if (
    v === "always" ||
    v === "after-first-message" ||
    v === "before-first-message" ||
    v === "disabled"
  ) {
    return v;
  }
  return "always";
}

function getDashboardUrl(): string {
  if (typeof window !== "undefined") {
    return process.env.NEXT_PUBLIC_DASHBOARD_URL ?? DEFAULT_DASHBOARD_URL;
  }
  return process.env.NEXT_PUBLIC_DASHBOARD_URL ?? DEFAULT_DASHBOARD_URL;
}

/**
 * Component that handles thread switching and connects to the agent.
 * When threadId changes, CopilotKit's useAgent hook will automatically
 * trigger a connect to restore session history.
 */
function ChatWithThreads() {
  const { currentThreadId, addThread } = useThreads();
  const suggestionsAvailable = getSuggestionsAvailable();
  const { agent } = useCopilotKit();

  useConfigureSuggestions(
    suggestionsAvailable === "disabled"
      ? null
      : {
          instructions:
            "Suggest helpful follow-up questions or tasks based on the conversation.",
          available: suggestionsAvailable,
        }
  );

  // Send current thread ID to agent context for better debugging
  useAgentContext({
    description: "Current thread ID",
    value: currentThreadId,
  });

  /**
   * Force reconnect when thread changes.
   * CopilotKit should handle this automatically via useAgent, but
   * explicit reconnection ensures session history is properly restored.
   */
  useEffect(() => {
    if (agent && currentThreadId) {
      // The agent object from useCopilotKit provides the runtime connection
      // When threadId changes in CopilotChat, it automatically calls connectAgent
      // We don't need to manually trigger reconnect here as CopilotChat handles it
      console.log("[DotCraft] Thread changed to:", currentThreadId);
    }
  }, [currentThreadId, agent]);

  const toolsMenu = useMemo<(ToolsMenuItem | "-")[]>(
    () => [
      {
        label: "New chat",
        action: () => addThread(),
      },
      "-",
      {
        label: "Open DotCraft Dashboard",
        action: () => window.open(getDashboardUrl(), "_blank", "noopener,noreferrer"),
      },
      "-",
      {
        label: "Suggest: List workspace",
        action: () => {
          const textarea = document.querySelector<HTMLTextAreaElement>(
            'textarea[placeholder*="message" i], textarea[placeholder*="Type" i]'
          );
          if (textarea) {
            const value = "List the files in the workspace root.";
            const nativeSetter = Object.getOwnPropertyDescriptor(
              window.HTMLTextAreaElement.prototype,
              "value"
            )?.set;
            nativeSetter?.call(textarea, value);
            textarea.dispatchEvent(new Event("input", { bubbles: true }));
            textarea.focus();
          }
        },
      },
    ],
    [addThread]
  );

  return (
    <>
      <Nav onNewChat={addThread} />
      <div className="flex items-center gap-3 border-b border-slate-200 bg-white px-4 py-2 dark:border-slate-700 dark:bg-slate-900">
        <span className="text-sm text-slate-500 dark:text-slate-400">Thread:</span>
        <ThreadList />
      </div>
      <div className="min-h-0 flex-1">
        <AbortErrorBoundary>
          <CopilotChat 
            threadId={currentThreadId} 
            input={{ toolsMenu }} 
          />
        </AbortErrorBoundary>
      </div>
    </>
  );
}

export default function DotCraftChatPage() {
  return (
    <CopilotKitProvider
      runtimeUrl="/api/dotcraft"
      useSingleEndpoint
      renderToolCalls={toolRenderers}
      showDevConsole="auto"
    >
      <ThreadsProvider>
        <div className="flex h-screen flex-col">
          <ChatWithThreads />
        </div>
      </ThreadsProvider>
    </CopilotKitProvider>
  );
}
