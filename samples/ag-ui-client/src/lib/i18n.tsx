"use client";

import { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from "react";

export type Locale = "zh" | "en";

const STORAGE_KEY = "dotcraft-locale";

export type Messages = {
  // Nav
  openSidebar: string;
  closeSidebar: string;
  switchToLight: string;
  switchToDark: string;
  switchLocale: string;
  // Thread panel
  newChat: string;
  noChats: string;
  rename: string;
  delete: string;
  // Welcome screen
  welcomeTitle: string;
  welcomeSubtitle: string;
  featureFiles: string;
  featureFilesDesc: string;
  featureShell: string;
  featureShellDesc: string;
  featureSearch: string;
  featureSearchDesc: string;
  // CopilotKit labels
  chatPlaceholder: string;
  copy: string;
  helpful: string;
  notHelpful: string;
  regenerate: string;
  edit: string;
  readAloud: string;
  copyCode: string;
  copyCodeCopied: string;
  startTranscribe: string;
  cancelTranscribe: string;
  finishTranscribe: string;
  addAttachment: string;
  toolsMenu: string;
  chatToggleOpen: string;
  chatToggleClose: string;
  modalHeaderTitle: string;
  // Tools menu
  suggestListFiles: string;
  // Connection banner
  connectionError: string;
  retry: string;
};

const zhMessages: Messages = {
  openSidebar: "打开侧栏",
  closeSidebar: "关闭侧栏",
  switchToLight: "切换到浅色模式",
  switchToDark: "切换到深色模式",
  switchLocale: "EN",
  newChat: "新建对话",
  noChats: "暂无对话",
  rename: "重命名",
  delete: "删除",
  welcomeTitle: "DotCraft",
  welcomeSubtitle: "你好！我是 DotCraft，有什么可以帮你的？",
  featureFiles: "文件操作",
  featureFilesDesc: "读写、创建、删除工作区文件",
  featureShell: "Shell 执行",
  featureShellDesc: "在工作区内运行任意命令",
  featureSearch: "搜索",
  featureSearchDesc: "全局内容搜索与文件查找",
  chatPlaceholder: "向 DotCraft 发送消息...",
  copy: "复制",
  helpful: "有用",
  notHelpful: "没用",
  regenerate: "重新生成",
  edit: "编辑",
  readAloud: "朗读",
  copyCode: "复制代码",
  copyCodeCopied: "已复制",
  startTranscribe: "开始录音",
  cancelTranscribe: "取消录音",
  finishTranscribe: "完成录音",
  addAttachment: "添加附件",
  toolsMenu: "工具",
  chatToggleOpen: "打开对话",
  chatToggleClose: "关闭对话",
  modalHeaderTitle: "DotCraft",
  suggestListFiles: "建议：列出工作区文件",
  connectionError: "无法连接到 DotCraft 后端，请检查服务是否已启动。",
  retry: "重试",
};

const enMessages: Messages = {
  openSidebar: "Open sidebar",
  closeSidebar: "Close sidebar",
  switchToLight: "Switch to light mode",
  switchToDark: "Switch to dark mode",
  switchLocale: "中文",
  newChat: "New Chat",
  noChats: "No conversations",
  rename: "Rename",
  delete: "Delete",
  welcomeTitle: "DotCraft",
  welcomeSubtitle: "Hi! I'm DotCraft, how can I help you?",
  featureFiles: "File Operations",
  featureFilesDesc: "Read, write, create, delete workspace files",
  featureShell: "Shell Execution",
  featureShellDesc: "Run any command in the workspace",
  featureSearch: "Search",
  featureSearchDesc: "Global content search and file lookup",
  chatPlaceholder: "Send a message to DotCraft...",
  copy: "Copy",
  helpful: "Helpful",
  notHelpful: "Not helpful",
  regenerate: "Regenerate",
  edit: "Edit",
  readAloud: "Read aloud",
  copyCode: "Copy code",
  copyCodeCopied: "Copied",
  startTranscribe: "Start recording",
  cancelTranscribe: "Cancel recording",
  finishTranscribe: "Finish recording",
  addAttachment: "Add attachment",
  toolsMenu: "Tools",
  chatToggleOpen: "Open chat",
  chatToggleClose: "Close chat",
  modalHeaderTitle: "DotCraft",
  suggestListFiles: "Suggest: List workspace files",
  connectionError: "Cannot connect to DotCraft backend. Please check that the server is running.",
  retry: "Retry",
};

const dictionaries: Record<Locale, Messages> = { zh: zhMessages, en: enMessages };

function resolveInitialLocale(): Locale {
  if (typeof window === "undefined") return "zh";
  const stored = localStorage.getItem(STORAGE_KEY);
  if (stored === "zh" || stored === "en") return stored;
  return navigator.language.startsWith("zh") ? "zh" : "en";
}

type LocaleContextValue = {
  locale: Locale;
  t: (key: keyof Messages) => string;
  setLocale: (locale: Locale) => void;
};

const LocaleContext = createContext<LocaleContextValue | null>(null);

export function LocaleProvider({ children }: { children: ReactNode }) {
  const [locale, setLocaleState] = useState<Locale>("zh");

  useEffect(() => {
    setLocaleState(resolveInitialLocale());
  }, []);

  const setLocale = useCallback((next: Locale) => {
    localStorage.setItem(STORAGE_KEY, next);
    setLocaleState(next);
  }, []);

  const t = useCallback((key: keyof Messages): string => {
    return dictionaries[locale][key];
  }, [locale]);

  return (
    <LocaleContext.Provider value={{ locale, t, setLocale }}>
      {children}
    </LocaleContext.Provider>
  );
}

export function useLocale(): LocaleContextValue {
  const ctx = useContext(LocaleContext);
  if (!ctx) throw new Error("useLocale must be used within LocaleProvider");
  return ctx;
}
