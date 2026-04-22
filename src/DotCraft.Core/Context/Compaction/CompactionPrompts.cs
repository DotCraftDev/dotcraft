using System.Text.RegularExpressions;
using DotCraft.Localization;

namespace DotCraft.Context.Compaction;

/// <summary>
/// Bilingual (Chinese/English) prompt templates and formatting helpers for the
/// compaction pipeline. Ports openclaude's <c>prompt.ts</c>
/// (<c>BASE_COMPACT_PROMPT</c>, <c>PARTIAL_COMPACT_UP_TO_PROMPT</c>,
/// <c>formatCompactSummary</c>, <c>getCompactUserSummaryMessage</c>).
/// </summary>
public static class CompactionPrompts
{
    // Aggressive no-tools preamble. We still surface tools to the summarizer
    // because FunctionInvokingChatClient is installed on the chat stack, so
    // the system prompt has to be explicit that any tool call wastes the turn.
    private const string NoToolsPreambleEn = """
CRITICAL: Respond with TEXT ONLY. Do NOT call any tools.

- Do NOT use ReadFile, Exec, GrepFiles, FindFiles, EditFile, WriteFile, or ANY other tool.
- You already have all the context you need in the conversation above.
- Tool calls will be REJECTED and will waste your only turn — you will fail the task.
- Your entire response must be plain text: an <analysis> block followed by a <summary> block.

""";

    private const string NoToolsPreambleZh = """
重要：只能以纯文本形式回复，不要调用任何工具。

- 不要使用 ReadFile、Exec、GrepFiles、FindFiles、EditFile、WriteFile 或其它任何工具。
- 你已经在上方对话中拥有所需的全部上下文。
- 工具调用将被直接拒绝，这一次机会就会浪费掉——你将无法完成任务。
- 整个回复必须是纯文本：先一个 <analysis> 块，然后一个 <summary> 块。

""";

    private const string NoToolsTrailerEn =
        "\n\nREMINDER: Do NOT call any tools. Respond with plain text only — "
        + "an <analysis> block followed by a <summary> block. "
        + "Tool calls will be rejected and you will fail the task.";

    private const string NoToolsTrailerZh =
        "\n\n提醒：不要调用任何工具。只能以纯文本回复 —— "
        + "先一个 <analysis> 块，然后一个 <summary> 块。"
        + "工具调用将被直接拒绝，你将无法完成任务。";

    private const string DetailedAnalysisBaseEn = """
Before providing your final summary, wrap your analysis in <analysis> tags to organize your thoughts and ensure you've covered all necessary points. In your analysis process:

1. Chronologically analyze each message and section of the conversation. For each section thoroughly identify:
   - The user's explicit requests and intents
   - Your approach to addressing the user's requests
   - Key decisions, technical concepts and code patterns
   - Specific details like:
     - file names
     - full code snippets
     - function signatures
     - file edits
   - Errors that you ran into and how you fixed them
   - Pay special attention to specific user feedback that you received, especially if the user told you to do something differently.
2. Double-check for technical accuracy and completeness, addressing each required element thoroughly.
""";

    private const string DetailedAnalysisBaseZh = """
在给出最终摘要之前，先在 <analysis> 标签内整理你的分析过程，确保覆盖所有要点。在分析过程中：

1. 按时间顺序分析对话中的每一条消息、每一个阶段。针对每个阶段仔细识别：
   - 用户的明确请求与意图
   - 你为响应这些请求所采取的做法
   - 关键决策、技术概念与代码模式
   - 具体细节，如：
     - 文件名
     - 完整的代码片段
     - 函数签名
     - 文件修改
   - 你遇到的错误以及修复方式
   - 特别关注用户给出的具体反馈，尤其是用户要求调整做法的部分。
2. 再次核对技术准确性与完整性,逐项落实以下所有章节。
""";

    private const string BaseCompactPromptEn = $$"""
Your task is to create a detailed summary of the conversation so far, paying close attention to the user's explicit requests and your previous actions.
This summary should be thorough in capturing technical details, code patterns, and architectural decisions that would be essential for continuing development work without losing context.

{{DetailedAnalysisBaseEn}}

Your summary should include the following sections:

1. Primary Request and Intent: Capture all of the user's explicit requests and intents in detail
2. Key Technical Concepts: List all important technical concepts, technologies, and frameworks discussed.
3. Files and Code Sections: Enumerate specific files and code sections examined, modified, or created. Pay special attention to the most recent messages and include full code snippets where applicable and include a summary of why this file read or edit is important.
4. Errors and fixes: List all errors that you ran into, and how you fixed them. Pay special attention to specific user feedback that you received, especially if the user told you to do something differently.
5. Problem Solving: Document problems solved and any ongoing troubleshooting efforts.
6. All user messages: List ALL user messages that are not tool results. These are critical for understanding the users' feedback and changing intent.
7. Pending Tasks: Outline any pending tasks that you have explicitly been asked to work on.
8. Current Work: Describe in detail precisely what was being worked on immediately before this summary request, paying special attention to the most recent messages from both user and assistant. Include file names and code snippets where applicable.
9. Optional Next Step: List the next step that you will take that is related to the most recent work you were doing. IMPORTANT: ensure that this step is DIRECTLY in line with the user's most recent explicit requests, and the task you were working on immediately before this summary request. If your last task was concluded, then only list next steps if they are explicitly in line with the users request. Do not start on tangential requests or really old requests that were already completed without confirming with the user first.
                       If there is a next step, include direct quotes from the most recent conversation showing exactly what task you were working on and where you left off. This should be verbatim to ensure there's no drift in task interpretation.

Structure your response as:

<analysis>
[Your thought process]
</analysis>

<summary>
1. Primary Request and Intent:
   ...
2. Key Technical Concepts:
   - ...
3. Files and Code Sections:
   - ...
4. Errors and fixes:
   - ...
5. Problem Solving:
   ...
6. All user messages:
   - ...
7. Pending Tasks:
   - ...
8. Current Work:
   ...
9. Optional Next Step:
   ...
</summary>

Please provide your summary based on the conversation so far, following this structure and ensuring precision and thoroughness in your response.
""";

    private const string BaseCompactPromptZh = $$"""
你的任务是对迄今为止的整段对话生成一份详细的摘要，密切关注用户的明确请求以及你此前的操作。
这份摘要需要充分覆盖技术细节、代码模式和架构决策，以便在不丢失上下文的情况下继续开发工作。

{{DetailedAnalysisBaseZh}}

摘要需要包含以下章节：

1. 主要请求与意图：详尽记录用户的所有明确请求和意图
2. 关键技术概念：列出所讨论的全部重要技术概念、技术和框架。
3. 文件与代码片段：枚举被查看、修改或创建的具体文件和代码片段。特别关注最近几条消息，包含完整代码片段，并说明为什么该文件的阅读/编辑很重要。
4. 错误与修复：列出遇到的所有错误以及修复方式。特别关注用户给出的具体反馈，尤其是要求调整做法的部分。
5. 问题解决：记录已解决的问题以及仍在排查中的问题。
6. 所有用户消息：列出所有非工具结果的用户消息，这对于理解用户反馈和意图变化至关重要。
7. 待办任务：概述用户明确要求你继续处理的所有未完成任务。
8. 当前工作：详尽描述在本次摘要请求之前你正在进行的工作，特别关注最近的用户消息和助理消息。必要时包含文件名和代码片段。
9. 可选的下一步：列出与最近工作直接相关的下一步。重要：确保这一步与用户最近明确的请求以及你当时正在做的任务直接对齐。如果上一项任务已完结，只有在明确符合用户请求时才列出下一步。不要在未与用户确认的情况下开始旁支或早已完成的旧任务。
                       如果存在下一步，请包含最近对话的原文引用，精确表明当时正在做什么、停在了哪里。必须逐字引用，避免任务解读漂移。

请按以下结构组织你的回复：

<analysis>
[你的思考过程]
</analysis>

<summary>
1. 主要请求与意图：
   ...
2. 关键技术概念：
   - ...
3. 文件与代码片段：
   - ...
4. 错误与修复：
   - ...
5. 问题解决：
   ...
6. 所有用户消息：
   - ...
7. 待办任务：
   - ...
8. 当前工作：
   ...
9. 可选的下一步：
   ...
</summary>

请基于上述对话给出摘要，严格遵循该结构，确保精准与完整。
""";

    // "up_to" variant: model sees only the summarized prefix; newer messages
    // follow after the summary in the next turn.
    private const string PartialCompactUpToEn = $$"""
Your task is to create a detailed summary of this conversation. This summary will be placed at the start of a continuing session; newer messages that build on this context will follow after your summary (you do not see them here). Summarize thoroughly so that someone reading only your summary and then the newer messages can fully understand what happened and continue the work.

{{DetailedAnalysisBaseEn}}

Your summary should include the following sections:

1. Primary Request and Intent
2. Key Technical Concepts
3. Files and Code Sections
4. Errors and fixes
5. Problem Solving
6. All user messages
7. Pending Tasks
8. Work Completed
9. Context for Continuing Work

Structure your response as:

<analysis>
[Your thought process]
</analysis>

<summary>
1. Primary Request and Intent:
   ...
2. Key Technical Concepts:
   - ...
3. Files and Code Sections:
   - ...
4. Errors and fixes:
   - ...
5. Problem Solving:
   ...
6. All user messages:
   - ...
7. Pending Tasks:
   - ...
8. Work Completed:
   ...
9. Context for Continuing Work:
   ...
</summary>

Please provide your summary following this structure, ensuring precision and thoroughness in your response.
""";

    private const string PartialCompactUpToZh = $$"""
你的任务是为这段对话生成一份详细摘要。这份摘要会被放在后续对话的开头；在此之后将会有基于该上下文的新消息（你现在看不到）。请充分总结，使得只阅读你的摘要加上之后的新消息的人，也能够完全理解此前发生的事情并继续工作。

{{DetailedAnalysisBaseZh}}

摘要需要包含以下章节：

1. 主要请求与意图
2. 关键技术概念
3. 文件与代码片段
4. 错误与修复
5. 问题解决
6. 所有用户消息
7. 待办任务
8. 已完成工作
9. 继续工作所需的上下文

请按以下结构组织你的回复：

<analysis>
[你的思考过程]
</analysis>

<summary>
1. 主要请求与意图：
   ...
2. 关键技术概念：
   - ...
3. 文件与代码片段：
   - ...
4. 错误与修复：
   - ...
5. 问题解决：
   ...
6. 所有用户消息：
   - ...
7. 待办任务：
   - ...
8. 已完成工作：
   ...
9. 继续工作所需的上下文：
   ...
</summary>

请按此结构输出摘要，确保精准与完整。
""";

    private const string ContinuationPrefaceEn =
        "This session is being continued from a previous conversation that ran out of context. "
        + "The summary below covers the earlier portion of the conversation.\n\n";

    private const string ContinuationPrefaceZh =
        "本次会话是从上一段已耗尽上下文的对话延续而来。"
        + "以下摘要涵盖了先前对话的内容。\n\n";

    private const string TranscriptHintEn =
        "\n\nIf you need specific details from before compaction (like exact code snippets, error messages, or content you generated), "
        + "read the full transcript at: {0}";

    private const string TranscriptHintZh =
        "\n\n如果需要压缩前的具体细节（例如代码片段、错误信息或生成的内容），"
        + "请查阅完整记录：{0}";

    private const string RecentPreservedEn = "\n\nRecent messages are preserved verbatim.";
    private const string RecentPreservedZh = "\n\n最近的消息已原样保留。";

    /// <summary>
    /// Returns the system-prompt text for a full-history compaction.
    /// </summary>
    public static string GetCompactPrompt(Language? language = null)
    {
        var lang = language ?? LanguageService.Current.CurrentLanguage;
        return lang == Language.Chinese
            ? NoToolsPreambleZh + BaseCompactPromptZh + NoToolsTrailerZh
            : NoToolsPreambleEn + BaseCompactPromptEn + NoToolsTrailerEn;
    }

    /// <summary>
    /// Returns the system-prompt text for a partial (up-to) compaction where
    /// the summary will precede retained recent messages.
    /// </summary>
    public static string GetPartialCompactPrompt(Language? language = null)
    {
        var lang = language ?? LanguageService.Current.CurrentLanguage;
        return lang == Language.Chinese
            ? NoToolsPreambleZh + PartialCompactUpToZh + NoToolsTrailerZh
            : NoToolsPreambleEn + PartialCompactUpToEn + NoToolsTrailerEn;
    }

    private static readonly Regex AnalysisBlockRegex =
        new(@"<analysis>[\s\S]*?</analysis>", RegexOptions.Compiled);

    private static readonly Regex SummaryBlockRegex =
        new(@"<summary>([\s\S]*?)</summary>", RegexOptions.Compiled);

    private static readonly Regex MultipleBlankLinesRegex =
        new(@"\n\n+", RegexOptions.Compiled);

    /// <summary>
    /// Strips the <c>&lt;analysis&gt;</c> scratchpad and unwraps the
    /// <c>&lt;summary&gt;</c> block, returning a plain-text summary.
    /// Mirrors openclaude's <c>formatCompactSummary</c>.
    /// </summary>
    public static string FormatCompactSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return string.Empty;

        var formatted = AnalysisBlockRegex.Replace(summary, string.Empty);
        var match = SummaryBlockRegex.Match(formatted);
        if (match.Success)
        {
            var content = match.Groups[1].Value.Trim();
            var label = LanguageService.Current.CurrentLanguage == Language.Chinese ? "摘要：" : "Summary:";
            formatted = SummaryBlockRegex.Replace(formatted, $"{label}\n{content}", 1);
        }

        formatted = MultipleBlankLinesRegex.Replace(formatted, "\n\n");
        return formatted.Trim();
    }

    /// <summary>
    /// Wraps a formatted summary with the continuation preamble that gets
    /// prepended to the new conversation history after compaction.
    /// </summary>
    public static string GetCompactUserSummaryMessage(
        string summary,
        string? transcriptPath = null,
        bool recentMessagesPreserved = false,
        Language? language = null)
    {
        var lang = language ?? LanguageService.Current.CurrentLanguage;
        var formatted = FormatCompactSummary(summary);

        var text = lang == Language.Chinese
            ? ContinuationPrefaceZh + formatted
            : ContinuationPrefaceEn + formatted;

        if (!string.IsNullOrWhiteSpace(transcriptPath))
        {
            var hint = lang == Language.Chinese ? TranscriptHintZh : TranscriptHintEn;
            text += string.Format(hint, transcriptPath);
        }

        if (recentMessagesPreserved)
        {
            text += lang == Language.Chinese ? RecentPreservedZh : RecentPreservedEn;
        }

        return text;
    }
}
