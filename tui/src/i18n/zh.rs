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

// UX polish: footer hints
pub const MODE_CYCLE_HINT: &str = "shift+tab 切换";
pub const SHORTCUTS_HINT: &str = "? 查看快捷键";
