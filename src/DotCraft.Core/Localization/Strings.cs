namespace DotCraft.Localization;

/// <summary>
/// Localized strings for CLI
/// </summary>
public static class Strings
{
    // Command descriptions
    public static string CmdExit(LanguageService lang) => lang.GetString("退出程序", "Exit the program");
    public static string CmdHelp(LanguageService lang) => lang.GetString("显示帮助信息", "Show help information");
    public static string CmdClear(LanguageService lang) => lang.GetString("清屏", "Clear screen");
    public static string CmdNew(LanguageService lang) => lang.GetString("创建新会话", "Create a new session");
    public static string CmdLoad(LanguageService lang) => lang.GetString("选择并切换到另一个会话", "Select and switch to another session");
    public static string CmdDelete(LanguageService lang) => lang.GetString("选择并删除一个会话", "Select and delete a session");
    public static string CmdInit(LanguageService lang) => lang.GetString("初始化工作区", "Initialize workspace");
    public static string CmdDebug(LanguageService lang) => lang.GetString("切换调试模式", "Toggle debug mode");
    public static string CmdSkills(LanguageService lang) => lang.GetString("显示可用技能列表", "Show available skills");
    public static string CmdMcp(LanguageService lang) => lang.GetString("显示 MCP 服务状态", "Show MCP service status");
    public static string CmdSessions(LanguageService lang) => lang.GetString("显示保存的会话列表", "Show saved sessions");
    public static string CmdMemory(LanguageService lang) => lang.GetString("显示长期记忆", "Show long-term memory");
    public static string CmdHeartbeat(LanguageService lang) => lang.GetString("立即触发心跳检查", "Trigger heartbeat check immediately");
    public static string CmdCronList(LanguageService lang) => lang.GetString("查看定时任务列表", "List cron jobs");
    public static string CmdCronRemove(LanguageService lang) => lang.GetString("删除定时任务", "Remove a cron job");
    public static string CmdCronToggle(LanguageService lang) => lang.GetString("启用/禁用定时任务", "Enable/disable a cron job");
    public static string CmdLang(LanguageService lang) => lang.GetString("切换语言 (中/英)", "Switch language (Chinese/English)");
    public static string CmdCommands(LanguageService lang) => lang.GetString("显示自定义命令列表", "Show custom commands");
    public static string CmdAgent(LanguageService lang) => lang.GetString("切换到 Agent 模式", "Switch to Agent mode");
    public static string CmdPlan(LanguageService lang) => lang.GetString("切换到 Plan 模式", "Switch to Plan mode");

    // Welcome screen
    public static string CurrentSession(LanguageService lang) => lang.GetString("当前会话", "Current session");
    public static string QuickCommands(LanguageService lang) => lang.GetString("快捷命令", "Quick commands");

    // Session management
    public static string SessionLoaded(LanguageService lang) => lang.GetString("已加载会话", "Session loaded");
    public static string SessionLoadFailed(LanguageService lang) => lang.GetString("加载会话失败", "Failed to load session");
    public static string SessionCreated(LanguageService lang) => lang.GetString("已创建新会话", "New session created");
    public static string SessionCreateFailed(LanguageService lang) => lang.GetString("创建新会话失败", "Failed to create new session");
    public static string SessionDeleted(LanguageService lang) => lang.GetString("已删除会话", "Session deleted");
    public static string SessionNotFound(LanguageService lang) => lang.GetString("未找到会话", "Session not found");
    public static string SessionNewCreated(LanguageService lang) => lang.GetString("已创建新会话", "New session created");
    public static string SessionDeleteFailed(LanguageService lang) => lang.GetString("删除会话失败", "Failed to delete session");

    // Init command
    public static string InitWorkspace(LanguageService lang) => lang.GetString("重新初始化工作区", "Re-initialize workspace");
    public static string CurrentWorkspace(LanguageService lang) => lang.GetString("当前工作区", "Current workspace");
    public static string WorkspaceExists(LanguageService lang) => lang.GetString("工作区已存在，是否重新初始化？这将覆盖现有配置", "Workspace already exists. Re-initialize? This will overwrite existing configuration");
    public static string InitCancelled(LanguageService lang) => lang.GetString("初始化已取消", "Initialization cancelled");
    public static string InitComplete(LanguageService lang) => lang.GetString("初始化完成！", "Initialization complete!");
    public static string InitFailed(LanguageService lang) => lang.GetString("初始化失败，错误码", "Initialization failed, error code");

    // Memory command
    public static string LongTermMemory(LanguageService lang) => lang.GetString("长期记忆 (MEMORY.md)", "Long-term Memory (MEMORY.md)");
    public static string MemoryNotExists(LanguageService lang) => lang.GetString("长期记忆文件不存在", "Long-term memory file does not exist");
    public static string ExpectedPath(LanguageService lang) => lang.GetString("预期路径", "Expected path");
    public static string MemoryEmpty(LanguageService lang) => lang.GetString("长期记忆为空", "Long-term memory is empty");

    // Debug command
    public static string DebugEnabled(LanguageService lang) => lang.GetString("调试模式已开启", "Debug mode enabled");
    public static string DebugDisabled(LanguageService lang) => lang.GetString("调试模式已关闭", "Debug mode disabled");

    // Heartbeat command
    public static string HeartbeatUnavailable(LanguageService lang) => lang.GetString("心跳服务不可用。", "Heartbeat service unavailable.");
    public static string TriggeringHeartbeat(LanguageService lang) => lang.GetString("正在触发心跳...", "Triggering heartbeat...");
    public static string HeartbeatResult(LanguageService lang) => lang.GetString("心跳结果", "Heartbeat result");
    public static string HeartbeatNoResponse(LanguageService lang) => lang.GetString("无心跳响应（HEARTBEAT.md 可能为空或不存在）。", "No heartbeat response (HEARTBEAT.md may be empty or missing).");
    public static string HeartbeatUsage(LanguageService lang) => lang.GetString("用法：/heartbeat trigger", "Usage: /heartbeat trigger");

    // Cron command
    public static string CronUnavailable(LanguageService lang) => lang.GetString("定时任务服务不可用。", "Cron service unavailable.");
    public static string NoCronJobs(LanguageService lang) => lang.GetString("没有定时任务。", "No cron jobs.");
    public static string CronColId(LanguageService lang) => lang.GetString("ID", "ID");
    public static string CronColName(LanguageService lang) => lang.GetString("名称", "Name");
    public static string CronColSchedule(LanguageService lang) => lang.GetString("调度", "Schedule");
    public static string CronColStatus(LanguageService lang) => lang.GetString("状态", "Status");
    public static string CronColNextRun(LanguageService lang) => lang.GetString("下次运行", "Next Run");
    public static string CronExecuteOnce(LanguageService lang) => lang.GetString("在", "At");
    public static string CronExecuteOnceSuffix(LanguageService lang) => lang.GetString("执行一次", "execute once");
    public static string CronEvery(LanguageService lang) => lang.GetString("每", "Every");
    public static string CronEnabled(LanguageService lang) => lang.GetString("已启用", "Enabled");
    public static string CronDisabled(LanguageService lang) => lang.GetString("已禁用", "Disabled");
    public static string CronRemoveUsage(LanguageService lang) => lang.GetString("用法：/cron remove <jobId>", "Usage: /cron remove <jobId>");
    public static string CronJobDeleted(LanguageService lang) => lang.GetString("任务", "Job");
    public static string CronJobDeletedSuffix(LanguageService lang) => lang.GetString("已删除。", "deleted.");
    public static string CronJobNotFound(LanguageService lang) => lang.GetString("未找到任务", "Job not found");
    public static string CronToggleUsage(LanguageService lang) => lang.GetString("用法", "Usage");
    public static string CronJobEnabled(LanguageService lang) => lang.GetString("已启用。", "enabled.");
    public static string CronJobDisabled(LanguageService lang) => lang.GetString("已禁用。", "disabled.");
    public static string CronUsage(LanguageService lang) => lang.GetString("用法：/cron list | /cron remove <id> | /cron enable <id> | /cron disable <id>", "Usage: /cron list | /cron remove <id> | /cron enable <id> | /cron disable <id>");

    // Context compaction
    public static string ContextLimitReached(LanguageService lang) => lang.GetString("上下文 token 限制已达到，正在压缩对话...", "Context token limit reached, compacting conversation...");
    public static string ContextCompacted(LanguageService lang) => lang.GetString("上下文压缩成功。", "Context compacted successfully.");
    public static string ContextCompactSkipped(LanguageService lang) => lang.GetString("跳过上下文压缩（历史记录不足）。", "Context compaction skipped (insufficient history).");

    // Memory consolidation
    public static string MemoryConsolidating(LanguageService lang) => lang.GetString("正在整理记忆...", "Consolidating memory...");

    // Agent interrupt
    public static string AgentInterrupted(LanguageService lang) => lang.GetString("Agent 已中断", "Agent interrupted");

    // Goodbye
    public static string Goodbye(LanguageService lang) => lang.GetString("再见！", "Goodbye!");

    // Help panel
    public static string Commands(LanguageService lang) => lang.GetString("命令", "Commands");
    public static string UsageTips(LanguageService lang) => lang.GetString("使用提示", "Usage Tips");
    public static string TipDirectInput(LanguageService lang) => lang.GetString("直接输入问题与 DotCraft 对话", "Directly input questions to chat with DotCraft");
    public static string TipArrowKeys(LanguageService lang) => lang.GetString("使用方向键 ↑↓ 浏览历史", "Use arrow keys ↑↓ to browse history");
    public static string TipAutoSave(LanguageService lang) => lang.GetString("会话结束会自动保存", "Sessions are saved automatically");
    public static string TipTabComplete(LanguageService lang) => lang.GetString("按 Tab 自动补全命令", "Press Tab to auto-complete commands");
    public static string TipShiftTabMode(LanguageService lang) => lang.GetString("按 Shift+Tab 切换 Plan/Agent 模式", "Press Shift+Tab to switch Plan/Agent mode");

    // Skills panel
    public static string AvailableSkills(LanguageService lang) => lang.GetString("可用技能", "Available skills");
    public static string Skill(LanguageService lang) => lang.GetString("技能", "Skill");
    public static string Status(LanguageService lang) => lang.GetString("状态", "Status");
    public static string Source(LanguageService lang) => lang.GetString("来源", "Source");
    public static string Description(LanguageService lang) => lang.GetString("描述", "Description");
    public static string Available(LanguageService lang) => lang.GetString("可用", "Available");
    public static string Unavailable(LanguageService lang) => lang.GetString("不可用", "Unavailable");
    public static string NoSkills(LanguageService lang) => lang.GetString("没有可用的技能。", "No available skills.");
    public static string SkillsPath(LanguageService lang) => lang.GetString("技能路径", "Skills path");
    public static string NoDescription(LanguageService lang) => lang.GetString("无描述", "No description");

    // Sessions panel
    public static string SavedSessions(LanguageService lang) => lang.GetString("已保存的会话", "Saved sessions");
    public static string Session(LanguageService lang) => lang.GetString("会话", "Session");
    public static string CreatedAt(LanguageService lang) => lang.GetString("创建时间", "Created");
    public static string UpdatedAt(LanguageService lang) => lang.GetString("更新时间", "Updated");
    public static string Summary(LanguageService lang) => lang.GetString("摘要", "Summary");
    public static string NoSessions(LanguageService lang) => lang.GetString("没有找到会话。", "No sessions found.");

    // MCP panel
    public static string McpServices(LanguageService lang) => lang.GetString("MCP 服务", "MCP Services");
    public static string Server(LanguageService lang) => lang.GetString("服务器", "Server");
    public static string Tools(LanguageService lang) => lang.GetString("工具", "Tools");
    public static string ToolNames(LanguageService lang) => lang.GetString("工具名称", "Tool Names");
    public static string NoMcpServers(LanguageService lang) => lang.GetString("未连接 MCP 服务器。", "No MCP servers connected.");
    public static string McpConfigTip(LanguageService lang) => lang.GetString("在 config.json 的 \"McpServers\" 中配置 MCP 服务器。", "Configure MCP servers in \"McpServers\" section of config.json.");
    public static string Unknown(LanguageService lang) => lang.GetString("未知", "Unknown");

    // Language command
    public static string LanguageSwitched(LanguageService lang) => lang.GetString("语言已切换为", "Language switched to");
    public static string LanguageChinese(LanguageService lang) => lang.GetString("中文", "Chinese");
    public static string LanguageEnglish(LanguageService lang) => lang.GetString("英文", "English");

    // Unknown command
    public static string UnknownCommand(LanguageService lang) => lang.GetString("未知命令", "Unknown command");
    public static string DidYouMean(LanguageService lang) => lang.GetString("你是否想输入", "Did you mean");
    public static string ViewAllCommands(LanguageService lang) => lang.GetString("输入 /help 查看所有可用命令。", "Type /help to see all available commands.");

    // Session prompt
    public static string NoSessionsAvailable(LanguageService lang) => lang.GetString("没有可用的会话。", "No sessions available.");
    public static string NoSessionsToDelete(LanguageService lang) => lang.GetString("没有可删除的会话。", "No sessions to delete.");
    public static string SelectSessionToLoadTitle(LanguageService lang) => lang.GetString("选择要加载的会话：", "Select a session to load:");
    public static string SelectSessionToDeleteTitle(LanguageService lang) => lang.GetString("选择要删除的会话：", "Select a session to delete:");
    public static string SessionSelected(LanguageService lang) => lang.GetString("已选择会话", "Session selected");
    public static string Cancelled(LanguageService lang) => lang.GetString("已取消。", "Cancelled.");
    public static string Cancel(LanguageService lang) => lang.GetString("取消", "Cancel");
    public static string ConfirmDeleteCurrentWarning(LanguageService lang, string sessionId) => lang.GetString($"⚠️  您即将删除[cyan]当前[/]会话 '[cyan]{sessionId}[/]'。", $"⚠️  You are about to delete the [cyan]current[/] session '[cyan]{sessionId}[/]'.");
    public static string ConfirmDeleteCurrentSuffix(LanguageService lang) => lang.GetString("删除后将创建新会话。", "A new session will be created after deletion.");
    public static string ConfirmDeleteOther(LanguageService lang, string sessionId) => lang.GetString($"确定要删除会话 [cyan]{sessionId}[/]吗？", $"Are you sure you want to delete session [cyan]{sessionId}[/]?");
    public static string ConfirmDeleteQuestion(LanguageService lang) => lang.GetString("删除此会话？", "Delete this session?");
    public static string TimeUnknown(LanguageService lang) => lang.GetString("未知", "unknown");
    public static string TimeJustNow(LanguageService lang) => lang.GetString("刚刚", "just now");
    public static string TimeMinutesAgo(LanguageService lang, int n) => lang.GetString($"{n}分钟前", $"{n} min ago");
    public static string TimeHoursAgo(LanguageService lang, int n) => lang.GetString($"{n}小时前", $"{n}h ago");
    public static string TimeDaysAgo(LanguageService lang, int n) => lang.GetString($"{n}天前", $"{n}d ago");
}
