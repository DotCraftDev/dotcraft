using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Localization;
using Spectre.Console;

namespace DotCraft.CLI;

/// <summary>
/// 初始化辅助工具类
/// </summary>
public static class InitHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// 选择语言
    /// </summary>
    public static Language SelectLanguage()
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("请选择语言 / Please select language:")
                .AddChoices("中文 (Chinese)", "English"));

        return choice == "中文 (Chinese)" ? Language.Chinese : Language.English;
    }

    /// <summary>
    /// 询问用户是否确认，使用 Spectre.Console 选项（多语言支持）
    /// </summary>
    public static bool AskYesNo(string title, LanguageService? lang = null)
    {
        lang ??= new LanguageService();
        var yesOption = lang.GetString("是 (Yes)", "Yes");
        var noOption = lang.GetString("否 (No)", "No");
        
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .AddChoices(yesOption, noOption));

        return choice == yesOption;
    }

    /// <summary>
    /// 初始化工作区（多语言支持）
    /// </summary>
    public static int InitializeWorkspace(string workspacePath, Language language = Language.Chinese)
    {
        var lang = new LanguageService(language);
        
        AnsiConsole.MarkupLine($"[blue]🚀 {lang.GetString("开始初始化 DotCraft 工作区...", "Initializing DotCraft workspace...")}[/]");

        try
        {
            // 创建工作区目录结构
            var directories = new[]
            {
                workspacePath,
                Path.Combine(workspacePath, "sessions"),
                Path.Combine(workspacePath, "memory"),
                Path.Combine(workspacePath, "skills"),
                Path.Combine(workspacePath, "security")
            };

            foreach (var dir in directories)
            {
                Directory.CreateDirectory(dir);
                AnsiConsole.MarkupLine($"  [green]✓[/] {dir.EscapeMarkup()}");
            }

            // 创建配置文件（只保存用户设置的字段，避免覆盖全局配置）
            var workspaceNode = new JsonObject
            {
                ["Language"] = language.ToString()
            };

            // 过滤：移除全局配置中已有的字段（避免覆盖）
            var globalConfigPath = GetGlobalConfigPath();
            if (File.Exists(globalConfigPath))
            {
                if (JsonNode.Parse(File.ReadAllText(globalConfigPath)) is JsonObject globalNode)
                {
                    foreach (var prop in globalNode.ToList())
                    {
                        workspaceNode.Remove(prop.Key);
                    }
                }
            }

            var json = workspaceNode.ToJsonString(JsonOptions);
            string configPath = Path.Combine(workspacePath, "config.json");
            File.WriteAllText(configPath, json, System.Text.Encoding.UTF8);
            AnsiConsole.MarkupLine($"  [green]✓[/] {configPath.EscapeMarkup()}");

            // 创建模板文件
            var agentsContent = language == Language.Chinese
                ? """
# DotCraft 智能体指令

你是 DotCraft，一个简洁、可靠的 CLI 智能体。必要时调用工具完成任务。

## 核心原则

- **简洁明了**: 提供清晰、直接的回答，避免冗余
- **准确可靠**: 确保信息的准确性和可靠性
- **安全优先**: 操作文件系统和执行命令时保持谨慎
- **用户友好**: 解释你的行动，让用户了解你的工作过程

## 工作流程

1. **理解用户需求**: 仔细分析用户的问题和需求
2. **规划行动**: 确定是否需要调用工具（如读取文件、执行命令等）
3. **执行任务**: 调用适当的工具完成任务
4. **反馈结果**: 清晰地向用户报告结果
5. **记录重要信息**: 将重要信息保存到记忆中
"""
                : """
# DotCraft Agent Instructions

You are DotCraft, a concise and reliable CLI agent. Use tools when necessary to complete tasks.

## Core Principles

- **Concise and Clear**: Provide clear, direct answers without redundancy
- **Accurate and Reliable**: Ensure accuracy and reliability of information
- **Security First**: Be cautious when operating on the file system and executing commands
- **User Friendly**: Explain your actions to keep users informed of your work process

## Workflow

1. **Understand User Needs**: Carefully analyze user questions and requirements
2. **Plan Actions**: Determine if tools need to be called (e.g., reading files, executing commands)
3. **Execute Tasks**: Call appropriate tools to complete tasks
4. **Report Results**: Clearly report results to the user
5. **Record Important Information**: Save important information to memory
""";
            
            var agentsPath = Path.Combine(workspacePath, "AGENTS.md");
            File.WriteAllText(agentsPath, agentsContent, System.Text.Encoding.UTF8);
            AnsiConsole.MarkupLine("  [green]✓[/] AGENTS.md");

            var userContent = language == Language.Chinese
                ? """
# 用户信息模板

在此文件中记录关于用户的重要信息，帮助 DotCraft 更好地理解你的需求。

## 偏好设置

- **编程语言**: (如：C#, Python, JavaScript)
- **代码风格**: (如：简洁/详细，注重可读性/性能)
- **沟通风格**: (如：正式/随意，简洁/详细)
- **时区**: (如：UTC+8)
- **语言**: (如：中文, English)

## 项目信息

- **项目类型**: (如：Web应用，桌面应用，库)
- **主要技术栈**: (列出主要使用的技术)
- **开发环境**: (如：Visual Studio, VS Code, Rider)
"""
                : """
# User Information Template

Record important information about the user in this file to help DotCraft better understand your needs.

## Preferences

- **Programming Languages**: (e.g., C#, Python, JavaScript)
- **Code Style**: (e.g., Concise/Verbose, Readability/Performance focused)
- **Communication Style**: (e.g., Formal/Casual, Concise/Detailed)
- **Timezone**: (e.g., UTC+8)
- **Language**: (e.g., Chinese, English)

## Project Information

- **Project Type**: (e.g., Web App, Desktop App, Library)
- **Main Tech Stack**: (List main technologies used)
- **Development Environment**: (e.g., Visual Studio, VS Code, Rider)
""";

            var userPath = Path.Combine(workspacePath, "USER.md");
            File.WriteAllText(userPath, userContent, System.Text.Encoding.UTF8);
            AnsiConsole.MarkupLine("  [green]✓[/] USER.md");

            var memoryContent = language == Language.Chinese
                ? """
# 长期记忆

此文件存储需要在会话之间保持的重要信息。

## 用户信息

(关于用户的重要事实)

## 偏好设置

(随时间学习到的用户偏好)

## 重要备注

(需要记住的事项)

## 项目上下文

- 项目名称: ______________
- 当前目标: ______________
- 最近进展: ______________

## 关键决策

记录重要的技术决策及其原因：

| 日期 | 决策 | 原因 |
|------|------|------|
|      |      |      |
"""
                : """
# Long-term Memory

This file stores important information that needs to persist between sessions.

## User Information

(Important facts about the user)

## Preferences

(User preferences learned over time)

## Important Notes

(Things to remember)

## Project Context

- Project Name: ______________
- Current Goal: ______________
- Recent Progress: ______________

## Key Decisions

Record important technical decisions and their reasons:

| Date | Decision | Reason |
|------|----------|--------|
|      |          |        |
""";

            var memoryDir = Path.Combine(workspacePath, "memory");
            var memoryPath = Path.Combine(memoryDir, "MEMORY.md");
            File.WriteAllText(memoryPath, memoryContent, System.Text.Encoding.UTF8);
            AnsiConsole.MarkupLine("  [green]✓[/] memory/MEMORY.md");

            var gitignoreContent = language == Language.Chinese
                ? """
# DotCraft 工作区 - 敏感数据不应提交

# 会话文件（包含对话历史）
sessions/

# 记忆文件（可能包含敏感信息）
memory/

# 批准记录（包含用户授权记录）
security/

# 日志文件
*.log

# 临时文件
*.tmp
*.temp
"""
                : """
# DotCraft Workspace - Sensitive data should not be committed

# Session files (contain conversation history)
sessions/

# Memory files (may contain sensitive information)
memory/

# Approval records (contain user authorization records)
security/

# Log files
*.log

# Temporary files
*.tmp
*.temp
""";

            var gitignorePath = Path.Combine(workspacePath, ".gitignore");
            File.WriteAllText(gitignorePath, gitignoreContent, System.Text.Encoding.UTF8);
            AnsiConsole.MarkupLine("  [green]✓[/] .gitignore");

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]✓ {lang.GetString("DotCraft 初始化完成！", "DotCraft initialization complete!")}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[grey]{lang.GetString("提示: 将 .craft 目录加入 .gitignore 以避免提交敏感数据", "Tip: Add .craft directory to .gitignore to avoid committing sensitive data")}[/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]✗ {lang.GetString("初始化失败", "Initialization failed")}: {ex.Message.EscapeMarkup()}[/]");
            return 1;
        }
    }

    /// <summary>
    /// 获取全局配置文件路径
    /// </summary>
    public static string GetGlobalConfigPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft", "config.json");
    }

    /// <summary>
    /// 提示用户输入 API Key
    /// </summary>
    public static string? PromptApiKey(LanguageService lang)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[cyan]{lang.GetString("请输入 API Key（留空跳过）：", "Enter API Key (leave empty to skip):")}[/]");
        var apiKey = Console.ReadLine();
        return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    /// <summary>
    /// 创建全局配置文件
    /// </summary>
    public static void CreateGlobalConfig(string configPath, string apiKey, Language language)
    {
        var configDir = Path.GetDirectoryName(configPath)!;
        Directory.CreateDirectory(configDir);

        var configNode = new JsonObject
        {
            ["ApiKey"] = apiKey,
            ["Model"] = "gpt-4o-mini",
            ["EndPoint"] = "https://api.openai.com/v1",
            ["Language"] = language.ToString()
        };

        var json = configNode.ToJsonString(JsonOptions);
        File.WriteAllText(configPath, json, System.Text.Encoding.UTF8);
    }
}
