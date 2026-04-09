// Chinese UI strings (Simplified).

pub const THINKING: &str = "思考中...";
pub const THINKING_COLLAPSED: &str = "思考中... (Tab 展开)";
pub const CONNECTED: &str = "● 已连接";
pub const DISCONNECTED: &str = "○ 未连接";
pub const MODE_AGENT: &str = "Agent 模式";
pub const MODE_PLAN: &str = "Plan 模式";
pub const ENTER_TO_SEND: &str = "Enter 发送  Shift+Enter 换行  Ctrl+C 中断";
pub const APPROVAL_TITLE: &str = "需要批准";
pub const PLAN_TITLE_PREFIX: &str = "计划：";
pub const SUBAGENTS_TITLE: &str = "子 Agent";
pub const PLACEHOLDER: &str = "输入消息或 /help...";
pub const TYPING_INDICATOR: &str = "▍";
pub const SCROLL_INDICATOR: &str = "↓ 还有 {} 行";
pub const TURN_RUNNING: &str = "运行中";
pub const TURN_APPROVAL: &str = "⏸ 等待批准";
pub const TURN_IDLE: &str = "";
pub const SYSTEM_COMPACTING: &str = "⟳ 压缩上下文...";
pub const SYSTEM_CONSOLIDATING: &str = "⟳ 整合中...";
pub const TOOL_RUNNING_PREFIX: &str = "⠋";
pub const TOOL_DONE_PREFIX: &str = "✓";
pub const TOOL_ERROR_PREFIX: &str = "✗";
pub const USER_PREFIX: &str = "❯";
pub const ERROR_PREFIX: &str = "✗";
pub const REASONING_HEADER: &str = "💭 思考";
pub const TOKENS_LABEL: &str = "tokens";
pub const SCROLL_TOP: &str = "↑ 顶部";
pub const SCROLL_BOTTOM: &str = "↓ 底部";
pub const MORE_LINES: &str = "↓";
pub const APPROVE: &str = "批准";
pub const REJECT: &str = "拒绝";
pub const EXPAND_HINT: &str = "(Enter 展开)";
pub const COLLAPSE_HINT: &str = "(Enter 折叠)";
pub const TAB_TOGGLE_REASONING: &str = "Tab: 切换推理显示";

// Phase 3: approval overlay
pub const APPROVAL_SHELL: &str = "Shell 命令";
pub const APPROVAL_FILE: &str = "文件操作";
pub const APPROVAL_ACCEPT: &str = "批准";
pub const APPROVAL_ACCEPT_SESSION: &str = "本次会话批准";
pub const APPROVAL_ACCEPT_ALWAYS: &str = "永久批准";
pub const APPROVAL_DECLINE: &str = "拒绝";
pub const APPROVAL_CANCEL: &str = "取消操作";
pub const APPROVAL_OPERATION_LABEL: &str = "命令";
pub const APPROVAL_TARGET_LABEL: &str = "目录";
pub const APPROVAL_REASON_LABEL: &str = "原因";

// Phase 3: focus indicator
pub const FOCUS_CHAT_HINT: &str = "Esc: 滚动聊天";
pub const FOCUS_INPUT_HINT: &str = "Enter/i: 输入";

// Phase 3: notification toast
pub const NOTIFICATION_JOB_RESULT: &str = "任务结果";
pub const NOTIFICATION_SUCCESS: &str = "成功";
pub const NOTIFICATION_ERROR: &str = "错误";

// Phase 4: thread picker overlay
pub const SESSIONS_TITLE: &str = "会话列表";
pub const SESSIONS_EMPTY: &str = "没有找到会话。";
pub const SESSIONS_LOADING: &str = "加载中...";
pub const SESSIONS_RESUME_HINT: &str = "Enter: 恢复";
pub const SESSIONS_ARCHIVE_HINT: &str = "a: 归档";
pub const SESSIONS_DELETE_HINT: &str = "d: 删除";
pub const SESSIONS_CLOSE_HINT: &str = "Esc: 关闭";

// Phase 4: help overlay
pub const HELP_TITLE: &str = "帮助";
pub const HELP_COMMANDS_HEADER: &str = "命令";
pub const HELP_KEYBINDINGS_HEADER: &str = "快捷键";

// Phase 4: misc
pub const CRON_NO_JOBS: &str = "没有配置定时任务。";
pub const THREAD_NOT_FOUND: &str = "未找到该会话。";
pub const FEATURE_UNAVAILABLE: &str = "此功能在当前服务器上不可用。";
pub const MODEL_USAGE: &str = "用法: /model [name|default]";
pub const MODEL_UPDATED_DEFAULT: &str = "模型已重置为 Default。";
pub const MODEL_UPDATED_TO: &str = "模型已更新为 {}。";
pub const MODEL_DEFAULT_LABEL: &str = "Default";
pub const MODEL_PICKER_TITLE: &str = "模型选择";
pub const MODEL_PICKER_LOADING: &str = "正在加载模型列表...";
pub const MODEL_PICKER_EMPTY: &str = "没有可用模型。";
pub const MODEL_PICKER_SELECT_HINT: &str = "Enter: 应用";
pub const MODEL_PICKER_CLOSE_HINT: &str = "Esc: 关闭";
pub const WELCOME_MODEL_LABEL: &str = "模型";

// UX polish: footer hints
pub const MODE_CYCLE_HINT: &str = "shift+tab 切换";
pub const SHORTCUTS_HINT: &str = "? 查看快捷键";

// UX polish round 2: welcome, action feedback
pub const WELCOME_MESSAGE: &str =
    "✦ DotCraft v{}\n  工作区：{}\n  输入消息开始对话 · /help 查看命令 · /sessions 查看历史";
pub const NEW_SESSION_HINT: &str = "新对话。输入消息开始。";
pub const SESSION_LOADED_PREFIX: &str = "已加载会话：";

// New design (v0.2.0): StatusIndicator, FooterLine, WelcomeScreen
pub const WORKING: &str = "运行中";
pub const ESC_TO_INTERRUPT: &str = "esc 中断";
pub const TAB_TO_QUEUE: &str = "tab 排队发送";
pub const ENTER_TO_SEND_HINT: &str = "enter 发送 · shift+enter 换行";
pub const QUIT_CONFIRM_HINT: &str = "再按 ctrl+c 退出";
pub const WELCOME_HINT_START: &str = "输入消息开始对话";
pub const WELCOME_HINT_COMMANDS: &str = "/help 查看命令 · /sessions 查看历史";
pub const WELCOME_CONNECTING: &str = "连接中...";
pub const WELCOME_READY: &str = "已连接 — 按任意键开始";
pub const CALLING: &str = "调用";
pub const CALLED: &str = "已调用";
pub const SUBAGENTS_ACTIVE_HEADER: &str = "子 Agent（{} 运行中，{} 完成）";
pub const SUBAGENTS_COMPLETE: &str = "子 Agent 已完成";
pub const PENDING_QUEUED_PREFIX: &str = "已排队：";
