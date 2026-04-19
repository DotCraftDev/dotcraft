using System.ComponentModel;
using System.Text;
using DotCraft.Abstractions;
using DotCraft.Memory;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Tool profile for ephemeral welcome-suggestion threads.
/// </summary>
public sealed class WelcomeSuggestionToolProvider(
    ThreadStore threadStore,
    MemoryStore memoryStore,
    string workspaceRoot) : IAgentToolProvider
{
    private readonly WelcomeSuggestionToolMethods _methods =
        new(threadStore, memoryStore, workspaceRoot);

    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        yield return AIFunctionFactory.Create(_methods.ListRecentWorkspaceThreads);
        yield return AIFunctionFactory.Create(_methods.ReadWelcomeThreadHistory);
        yield return AIFunctionFactory.Create(_methods.ReadWelcomeWorkspaceMemory);
        yield return AIFunctionFactory.Create(_methods.EmitWelcomeSuggestions);
    }
}

public sealed class WelcomeSuggestionThreadSummary
{
    [Description("Thread id.")]
    public string Id { get; set; } = string.Empty;

    [Description("Compact display name for the thread.")]
    public string DisplayName { get; set; } = string.Empty;

    [Description("Origin channel that owns the thread.")]
    public string OriginChannel { get; set; } = string.Empty;

    [Description("Last activity time in UTC ISO-8601 format.")]
    public string LastActiveAt { get; set; } = string.Empty;

    [Description("A short high-signal preview of the thread's recent user intent.")]
    public string Preview { get; set; } = string.Empty;
}

public sealed class WelcomeThreadHistoryResult
{
    [Description("Thread id.")]
    public string ThreadId { get; set; } = string.Empty;

    [Description("Compact display name for the thread.")]
    public string DisplayName { get; set; } = string.Empty;

    [Description("Filtered recent user intent snippets from this thread.")]
    public string[] UserSnippets { get; set; } = [];

    [Description("A compact summary of the latest useful agent response in this thread.")]
    public string AgentSummary { get; set; } = string.Empty;

    [Description("One to three dominant intents inferred from the thread history.")]
    public string[] DominantIntents { get; set; } = [];
}

public sealed class WelcomeWorkspaceMemoryResult
{
    [Description("Tail-trimmed MEMORY.md content.")]
    public string Memory { get; set; } = string.Empty;

    [Description("Tail-trimmed HISTORY.md content.")]
    public string HistoryTail { get; set; } = string.Empty;

    [Description("Combined workspace memory context.")]
    public string Combined { get; set; } = string.Empty;

    [Description("Short highlights extracted from workspace memory and recent history.")]
    public string[] MemoryHighlights { get; set; } = [];
}

public sealed class WelcomeSuggestionToolItem
{
    [Description("Short list title shown in the welcome suggestions UI.")]
    public string Title { get; set; } = string.Empty;

    [Description("Full prompt text inserted into the welcome composer when clicked.")]
    public string Prompt { get; set; } = string.Empty;

    [Description("Brief explanation of which history or memory signals inspired this suggestion.")]
    public string Reason { get; set; } = string.Empty;
}

internal sealed class WelcomeSuggestionToolMethods(
    ThreadStore threadStore,
    MemoryStore memoryStore,
    string workspaceRoot)
{
    private const int DefaultRecentThreadLimit = 12;
    private const int MaxRecentThreadLimit = 12;
    private const int MaxThreadSnippetCount = 8;
    private const int MemoryCharsLimit = 5_000;
    private const int HistoryTailCharsLimit = 3_000;
    private const int TotalMemoryCharsLimit = 8_000;

    [Tool(
        Icon = "🧵",
        DisplayType = typeof(WelcomeSuggestionToolDisplays),
        DisplayMethod = nameof(WelcomeSuggestionToolDisplays.ListRecentWorkspaceThreads))]
    [Description("List recent non-archived workspace threads that may provide context for welcome suggestions.")]
    public async Task<IReadOnlyList<WelcomeSuggestionThreadSummary>> ListRecentWorkspaceThreads(
        [Description("Maximum number of recent threads to return. Defaults to 12.")] int limit = DefaultRecentThreadLimit)
    {
        var normalizedWorkspace = WelcomeSuggestionService.NormalizeWorkspacePath(workspaceRoot);
        var recentThreads = (await threadStore.LoadIndexAsync(CancellationToken.None).ConfigureAwait(false))
            .Where(summary =>
                string.Equals(
                    WelcomeSuggestionService.NormalizeWorkspacePath(summary.WorkspacePath),
                    normalizedWorkspace,
                    StringComparison.OrdinalIgnoreCase)
                && summary.Status != ThreadStatus.Archived
                && !WelcomeSuggestionService.IsInternalThread(summary))
            .OrderByDescending(summary => summary.LastActiveAt)
            .Take(Math.Clamp(limit, 1, MaxRecentThreadLimit))
            .ToList();

        var results = new List<WelcomeSuggestionThreadSummary>(recentThreads.Count);
        foreach (var summary in recentThreads)
        {
            var thread = await threadStore.LoadThreadAsync(summary.Id, CancellationToken.None).ConfigureAwait(false);
            results.Add(new WelcomeSuggestionThreadSummary
            {
                Id = summary.Id,
                DisplayName = string.IsNullOrWhiteSpace(summary.DisplayName) ? summary.Id : summary.DisplayName.Trim(),
                OriginChannel = summary.OriginChannel,
                LastActiveAt = summary.LastActiveAt.ToString("O"),
                Preview = thread == null ? string.Empty : WelcomeSuggestionService.BuildThreadPreview(thread)
            });
        }

        return results;
    }

    [Tool(
        Icon = "📜",
        DisplayType = typeof(WelcomeSuggestionToolDisplays),
        DisplayMethod = nameof(WelcomeSuggestionToolDisplays.ReadWelcomeThreadHistory))]
    [Description("Read filtered user-message history for a workspace thread to understand likely next tasks.")]
    public async Task<WelcomeThreadHistoryResult> ReadWelcomeThreadHistory(
        [Description("Thread id returned by ListRecentWorkspaceThreads.")] string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return new WelcomeThreadHistoryResult();

        var thread = await threadStore.LoadThreadAsync(threadId.Trim(), CancellationToken.None).ConfigureAwait(false);
        if (thread == null)
            return new WelcomeThreadHistoryResult { ThreadId = threadId.Trim() };

        var snippets = WelcomeSuggestionService.ExtractUserSnippets(thread)
            .Take(MaxThreadSnippetCount)
            .ToArray();

        return new WelcomeThreadHistoryResult
        {
            ThreadId = thread.Id,
            DisplayName = string.IsNullOrWhiteSpace(thread.DisplayName) ? thread.Id : thread.DisplayName.Trim(),
            UserSnippets = snippets,
            AgentSummary = WelcomeSuggestionService.ExtractAgentSummary(thread),
            DominantIntents = WelcomeSuggestionService.ExtractDominantIntents(snippets)
        };
    }

    [Tool(
        Icon = "🧠",
        DisplayType = typeof(WelcomeSuggestionToolDisplays),
        DisplayMethod = nameof(WelcomeSuggestionToolDisplays.ReadWelcomeWorkspaceMemory))]
    [Description("Read workspace MEMORY.md and the recent tail of HISTORY.md for welcome suggestion grounding.")]
    public Task<WelcomeWorkspaceMemoryResult> ReadWelcomeWorkspaceMemory()
    {
        var memoryText = WelcomeSuggestionService.TrimToLimit(memoryStore.ReadLongTerm(), MemoryCharsLimit);
        var historyTail = WelcomeSuggestionService.ReadHistoryTailFromFile(memoryStore.HistoryFilePath, HistoryTailCharsLimit);
        var combined = WelcomeSuggestionService.CombineMemory(memoryText, historyTail, TotalMemoryCharsLimit);

        return Task.FromResult(new WelcomeWorkspaceMemoryResult
        {
            Memory = memoryText,
            HistoryTail = historyTail,
            Combined = combined,
            MemoryHighlights = WelcomeSuggestionService.ExtractMemoryHighlights(memoryText, historyTail)
        });
    }

    [Tool(
        Icon = "✨",
        DisplayType = typeof(WelcomeSuggestionToolDisplays),
        DisplayMethod = nameof(WelcomeSuggestionToolDisplays.EmitWelcomeSuggestions))]
    [Description("Submit the generated welcome suggestions as one batch.")]
    public string EmitWelcomeSuggestions(
        [Description("Exactly the requested number of welcome suggestions.")]
        WelcomeSuggestionToolItem[] items)
    {
        _ = items;
        return "Recorded.";
    }
}

public static class WelcomeSuggestionMethods
{
    public const string ListRecentWorkspaceThreadsToolName = "ListRecentWorkspaceThreads";
    public const string ReadWelcomeThreadHistoryToolName = "ReadWelcomeThreadHistory";
    public const string ReadWelcomeWorkspaceMemoryToolName = "ReadWelcomeWorkspaceMemory";
    public const string ToolName = "EmitWelcomeSuggestions";
}

public static class WelcomeSuggestionToolDisplays
{
    public static string ListRecentWorkspaceThreads(IDictionary<string, object?>? args)
    {
        var limit = TryReadArg(args, "limit");
        return string.IsNullOrWhiteSpace(limit)
            ? WelcomeSuggestionMethods.ListRecentWorkspaceThreadsToolName
            : $"{WelcomeSuggestionMethods.ListRecentWorkspaceThreadsToolName} ({limit})";
    }

    public static string ReadWelcomeThreadHistory(IDictionary<string, object?>? args)
    {
        var threadId = TryReadArg(args, "threadId");
        return string.IsNullOrWhiteSpace(threadId)
            ? WelcomeSuggestionMethods.ReadWelcomeThreadHistoryToolName
            : $"{WelcomeSuggestionMethods.ReadWelcomeThreadHistoryToolName} ({threadId})";
    }

    public static string ReadWelcomeWorkspaceMemory(IDictionary<string, object?>? args)
    {
        _ = args;
        return WelcomeSuggestionMethods.ReadWelcomeWorkspaceMemoryToolName;
    }

    public static string EmitWelcomeSuggestions(IDictionary<string, object?>? args)
    {
        var count = "items";
        if (args != null && args.TryGetValue("items", out var raw) && raw is System.Collections.ICollection collection)
            count = $"{collection.Count} items";
        return $"{WelcomeSuggestionMethods.ToolName} ({count})";
    }

    private static string? TryReadArg(IDictionary<string, object?>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var raw) || raw == null)
            return null;
        var text = raw.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
