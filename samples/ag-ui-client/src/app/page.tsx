"use client";

import {
  CopilotChat,
  CopilotKitProvider,
  useConfigureSuggestions,
  useAgentContext,
  CopilotChatConfigurationProvider,
} from "@copilotkitnext/react";
import type { ToolsMenuItem, CopilotChatLabels } from "@copilotkitnext/react";
import { useMemo, useState } from "react";
import { Nav } from "@/components/Nav";
import { ThreadPanel } from "@/components/ThreadPanel";
import { AbortErrorBoundary } from "@/components/AbortErrorBoundary";
import { ThreadsProvider, useThreads } from "@/contexts/ThreadsContext";
import { toolRenderers } from "@/lib/toolRenderers";
import { useMessagePersistence } from "@/hooks/useMessagePersistence";
import { WelcomeScreen } from "@/components/WelcomeScreen";

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

const dotcraftLabels: Partial<CopilotChatLabels> = {
  chatInputPlaceholder: "向 DotCraft 发送消息...",
  assistantMessageToolbarCopyMessageLabel: "复制",
  assistantMessageToolbarThumbsUpLabel: "有用",
  assistantMessageToolbarThumbsDownLabel: "没用",
  assistantMessageToolbarRegenerateLabel: "重新生成",
  userMessageToolbarCopyMessageLabel: "复制",
  userMessageToolbarEditMessageLabel: "编辑",
  welcomeMessageText: "你好！我是 DotCraft，有什么可以帮你的？",
};

function ChatWithThreads() {
  const { currentThreadId, addThread } = useThreads();
  const [panelOpen, setPanelOpen] = useState(false);
  useMessagePersistence(currentThreadId);
  const suggestionsAvailable = getSuggestionsAvailable();

  useConfigureSuggestions(
    suggestionsAvailable === "disabled"
      ? null
      : {
          instructions:
            "Suggest helpful follow-up questions or tasks based on the conversation.",
          available: suggestionsAvailable,
        }
  );

  useAgentContext({
    description: "Current thread ID:",
    value: currentThreadId,
  });

  const toolsMenu = useMemo<(ToolsMenuItem | "-")[]>(
    () => [
      {
        label: "新建对话",
        action: () => addThread(),
      },
      "-",
      {
        label: "打开 DotCraft Dashboard",
        action: () => window.open(getDashboardUrl(), "_blank", "noopener,noreferrer"),
      },
      "-",
      {
        label: "建议：列出工作区文件",
        action: () => {
          const textarea = document.querySelector<HTMLTextAreaElement>(
            'textarea[placeholder*="message" i], textarea[placeholder*="Type" i], textarea[placeholder*="发送" i]'
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
    <div className="flex h-screen flex-col">
      <Nav onMenuToggle={() => setPanelOpen((v) => !v)} menuOpen={panelOpen} />
      <div className="flex min-h-0 flex-1">
        <ThreadPanel open={panelOpen} onClose={() => setPanelOpen(false)} />
        <main className="min-w-0 flex-1">
          <AbortErrorBoundary>
            <CopilotChatConfigurationProvider labels={dotcraftLabels}>
              <CopilotChat
                threadId={currentThreadId}
                input={{ toolsMenu }}
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                chatView={{ welcomeScreen: WelcomeScreen as any }}
              />
            </CopilotChatConfigurationProvider>
          </AbortErrorBoundary>
        </main>
      </div>
    </div>
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
        <ChatWithThreads />
      </ThreadsProvider>
    </CopilotKitProvider>
  );
}
