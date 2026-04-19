using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Protocol;

public interface IWelcomeSuggestionService
{
    Task<WelcomeSuggestionsResult> SuggestAsync(
        WelcomeSuggestionsParams parameters,
        CancellationToken cancellationToken = default);
}

public sealed class WelcomeSuggestionService(
    ISessionService sessionService,
    ThreadStore threadStore,
    MemoryStore memoryStore,
    string workspaceRoot,
    ILogger<WelcomeSuggestionService>? logger = null) : IWelcomeSuggestionService
{
    private const int DefaultMaxItems = 4;
    private const int MaxItemsLimit = 4;
    internal const int RecentThreadLimit = 12;
    private const int MaxSnippetCount = 20;
    private const int MinSnippetLength = 15;
    private const int MaxSnippetLength = 300;
    private const int MaxAgentSummaryChars = 220;
    private const int MaxPreviewChars = 120;
    private const int MaxHighlightCount = 5;
    internal const int MemoryCharsLimit = 5_000;
    internal const int HistoryTailCharsLimit = 3_000;
    internal const int TotalMemoryCharsLimit = 8_000;
    private const int MinimumSnippetsForDynamicSuggestions = 2;
    private static readonly TimeSpan SuggestTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly string[] SpecificitySignals =
    [
        ".cs",
        ".tsx",
        ".ts",
        ".md",
        ".json",
        "api",
        "agent",
        "bug",
        "component",
        "config",
        "endpoint",
        "file",
        "history",
        "icon",
        "memory",
        "model",
        "module",
        "page",
        "prompt",
        "protocol",
        "service",
        "setting",
        "settings",
        "suggest",
        "suggestion",
        "test",
        "tests",
        "thread",
        "tool",
        "trace",
        "ui",
        "ux",
        "welcome",
        "workspace",
        "配置",
        "协议",
        "线程",
        "历史",
        "记忆",
        "模型",
        "提示词",
        "组件",
        "页面",
        "服务",
        "测试",
        "图标",
        "输入框",
        "建议",
        "欢迎页",
        "工作区",
        "修复",
        "排查"
    ];
    private static readonly string[] ActionSignals =
    [
        "add",
        "adjust",
        "analyze",
        "audit",
        "debug",
        "design",
        "fix",
        "implement",
        "investigate",
        "map",
        "refine",
        "review",
        "trace",
        "tune",
        "update",
        "wire",
        "优化",
        "分析",
        "修复",
        "排查",
        "实现",
        "调整",
        "梳理",
        "审查",
        "追踪",
        "更新"
    ];

    private readonly ConcurrentDictionary<string, WelcomeSuggestionCacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<WelcomeSuggestionsResult>>> _inflight =
        new(StringComparer.Ordinal);

    public async Task<WelcomeSuggestionsResult> SuggestAsync(
        WelcomeSuggestionsParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Identity == null)
            throw new InvalidOperationException("identity is required.");

        var workspacePath = NormalizeWorkspacePath(parameters.Identity.WorkspacePath);
        if (string.IsNullOrWhiteSpace(workspacePath))
            throw new InvalidOperationException("identity.workspacePath is required.");
        if (!string.Equals(workspacePath, NormalizeWorkspacePath(workspaceRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Requested workspace is not hosted by this AppServer instance.");

        var maxItems = ClampMaxItems(parameters.MaxItems);
        if (!IsWelcomeSuggestionsEnabled(workspacePath))
            return BuildNoSuggestionsResult(string.Empty);

        var evidence = await BuildEvidenceAsync(workspacePath, maxItems, cancellationToken).ConfigureAwait(false);
        if (!evidence.HasSufficientContext)
            return BuildNoSuggestionsResult(evidence.Fingerprint);

        if (_cache.TryGetValue(evidence.CacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.Result;

        var lazy = _inflight.GetOrAdd(
            evidence.CacheKey,
            _ => new Lazy<Task<WelcomeSuggestionsResult>>(
                () => GenerateAndCacheAsync(parameters.Identity, evidence, maxItems, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            _inflight.TryRemove(evidence.CacheKey, out _);
        }
    }

    private async Task<WelcomeSuggestionsResult> GenerateAndCacheAsync(
        SessionIdentity identity,
        WelcomeSuggestionEvidence evidence,
        int maxItems,
        CancellationToken cancellationToken)
    {
        var result = await GenerateDynamicSuggestionsAsync(identity, evidence, maxItems, cancellationToken)
            .ConfigureAwait(false);

        _cache[evidence.CacheKey] = new WelcomeSuggestionCacheEntry(
            result,
            DateTimeOffset.UtcNow.Add(CacheTtl));
        return result;
    }

    private async Task<WelcomeSuggestionsResult> GenerateDynamicSuggestionsAsync(
        SessionIdentity identity,
        WelcomeSuggestionEvidence evidence,
        int maxItems,
        CancellationToken cancellationToken)
    {
        string? tempThreadId = null;
        try
        {
            using var timeoutCts = new CancellationTokenSource(SuggestTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var tempThread = await sessionService.CreateThreadAsync(
                    new SessionIdentity
                    {
                        ChannelName = WelcomeSuggestionConstants.ChannelName,
                        UserId = WelcomeSuggestionConstants.InternalUserId,
                        WorkspacePath = identity.WorkspacePath,
                        ChannelContext = $"welcome-suggest:{evidence.Fingerprint}"
                    },
                    new ThreadConfiguration
                    {
                        Mode = "agent",
                        ToolProfile = WelcomeSuggestionConstants.ToolProfileName,
                        UseToolProfileOnly = true,
                        ApprovalPolicy = ApprovalPolicy.AutoApprove,
                        AgentInstructions = WelcomeSuggestionInstructions.SystemPrompt
                    },
                    HistoryMode.Server,
                    displayName: "[internal] Welcome suggestions",
                    ct: linked.Token)
                .ConfigureAwait(false);

            tempThreadId = tempThread.Id;
            tempThread.Metadata[WelcomeSuggestionConstants.InternalMetadataKey] =
                WelcomeSuggestionConstants.InternalMetadataValue;
            await threadStore.SaveThreadAsync(tempThread, linked.Token).ConfigureAwait(false);

            List<WelcomeSuggestionItem>? items = null;
            await foreach (var evt in sessionService.SubmitInputAsync(
                               tempThreadId,
                               [new TextContent(BuildGenerationPrompt(maxItems))],
                               ct: linked.Token).ConfigureAwait(false))
            {
                if (evt.EventType != SessionEventType.ItemCompleted || evt.ItemPayload == null)
                    continue;
                if (evt.ItemPayload.Type != ItemType.ToolCall)
                    continue;
                var tc = evt.ItemPayload.AsToolCall;
                if (tc == null || !string.Equals(tc.ToolName, WelcomeSuggestionMethods.ToolName, StringComparison.Ordinal))
                    continue;

                items = ParseSuggestionItems(tc.Arguments, maxItems);
                if (items.Count == maxItems)
                    break;
            }

            if (items == null || items.Count != maxItems)
                throw new InvalidOperationException("The model did not emit the expected welcome suggestions.");

            return new WelcomeSuggestionsResult
            {
                Items = items,
                Source = "dynamic",
                GeneratedAt = DateTimeOffset.UtcNow,
                Fingerprint = evidence.Fingerprint
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning("Welcome suggestion generation timed out; returning no personalized suggestions.");
            return BuildNoSuggestionsResult(evidence.Fingerprint);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Welcome suggestion generation failed; returning no personalized suggestions.");
            return BuildNoSuggestionsResult(evidence.Fingerprint);
        }
        finally
        {
            if (tempThreadId != null)
            {
                try
                {
                    using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await sessionService.DeleteThreadPermanentlyAsync(tempThreadId, cleanupCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    logger?.LogWarning("Timeout while deleting ephemeral welcome-suggest thread {ThreadId}", tempThreadId);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to delete ephemeral welcome-suggest thread {ThreadId}", tempThreadId);
                }
            }
        }
    }

    private async Task<WelcomeSuggestionEvidence> BuildEvidenceAsync(
        string workspacePath,
        int maxItems,
        CancellationToken cancellationToken)
    {
        var summaries = (await threadStore.LoadIndexAsync(cancellationToken).ConfigureAwait(false))
            .Where(summary =>
                string.Equals(NormalizeWorkspacePath(summary.WorkspacePath), workspacePath, StringComparison.OrdinalIgnoreCase)
                && summary.Status != ThreadStatus.Archived
                && !IsInternalThread(summary))
            .OrderByDescending(summary => summary.LastActiveAt)
            .Take(RecentThreadLimit)
            .ToList();

        var snippets = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var summary in summaries)
        {
            var thread = await threadStore.LoadThreadAsync(summary.Id, cancellationToken).ConfigureAwait(false);
            if (thread == null)
                continue;

            foreach (var snippet in ExtractUserSnippets(thread))
            {
                if (snippets.Count >= MaxSnippetCount)
                    break;
                var key = NormalizeForDedup(snippet);
                if (!seen.Add(key))
                    continue;
                snippets.Add(snippet);
            }

            if (snippets.Count >= MaxSnippetCount)
                break;
        }

        var memoryText = TrimToLimit(memoryStore.ReadLongTerm(), MemoryCharsLimit);
        var historyText = ReadHistoryTailFromFile(memoryStore.HistoryFilePath, HistoryTailCharsLimit);
        var combinedMemory = CombineMemory(memoryText, historyText, TotalMemoryCharsLimit);
        var fingerprint = BuildFingerprint(
            workspacePath,
            maxItems,
            summaries,
            snippets,
            memoryStore.LongTermFilePath,
            memoryStore.HistoryFilePath,
            combinedMemory);

        return new WelcomeSuggestionEvidence(
            summaries,
            snippets,
            combinedMemory,
            fingerprint,
            $"{fingerprint}:{maxItems}",
            snippets.Count >= MinimumSnippetsForDynamicSuggestions || !string.IsNullOrWhiteSpace(combinedMemory));
    }

    private bool IsWelcomeSuggestionsEnabled(string workspacePath)
    {
        try
        {
            var configPath = Path.Combine(workspacePath, ".craft", "config.json");
            var mergedConfig = AppConfig.LoadWithGlobalFallback(configPath);
            return mergedConfig.WelcomeSuggestions.Enabled;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to resolve welcome suggestion config; defaulting to enabled.");
            return true;
        }
    }

    private static string BuildGenerationPrompt(int maxItems) =>
        $"Inspect recent workspace history and memory, infer the likely next tasks, and call {WelcomeSuggestionMethods.ToolName} exactly once with exactly {maxItems} concrete suggestions. If you cannot produce {maxItems} concrete suggestions, do not call the tool.";

    private static List<WelcomeSuggestionItem> ParseSuggestionItems(JsonObject? arguments, int maxItems)
    {
        if (arguments == null)
            return [];

        if (!arguments.TryGetPropertyValue("items", out var itemsNode) || itemsNode is not JsonArray itemsArray)
            return [];

        var items = new List<WelcomeSuggestionItem>(maxItems);
        foreach (var node in itemsArray)
        {
            if (node is not JsonObject obj)
                continue;

            var title = SanitizeSuggestionField(obj["title"]?.GetValue<string>(), 80);
            var prompt = SanitizeSuggestionField(obj["prompt"]?.GetValue<string>(), 500);
            var reason = SanitizeSuggestionField(obj["reason"]?.GetValue<string>(), 200);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(prompt))
                continue;
            if (!IsSpecificSuggestion(title, prompt))
                continue;

            items.Add(new WelcomeSuggestionItem
            {
                Title = title,
                Prompt = prompt,
                Reason = reason
            });
        }

        return items.Take(maxItems).ToList();
    }

    private static string SanitizeSuggestionField(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        trimmed = trimmed.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        trimmed = string.Join(" ", trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (trimmed.Length > maxChars)
            trimmed = trimmed[..maxChars].TrimEnd();
        return trimmed;
    }

    internal static IEnumerable<string> ExtractUserSnippets(SessionThread thread)
    {
        var ranked = new List<(int Index, int Score, string Text)>();
        var index = 0;
        foreach (var turn in thread.Turns)
        {
            foreach (var item in turn.Items)
            {
                if (item.Type != ItemType.UserMessage || item.AsUserMessage is not { } payload)
                    continue;

                var normalized = NormalizeSnippet(payload.Text);
                if (normalized == null)
                    continue;
                ranked.Add((index++, ScoreSnippetSpecificity(normalized), normalized));
            }
        }

        foreach (var entry in ranked
                     .OrderByDescending(entry => entry.Score)
                     .ThenByDescending(entry => entry.Index))
        {
            yield return entry.Text;
        }
    }

    internal static string? NormalizeSnippet(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var collapsed = string.Join(
            " ",
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (collapsed.Length < MinSnippetLength || collapsed.Length > MaxSnippetLength)
            return null;
        if (IsSlashCommand(collapsed))
            return null;
        if (IsAcknowledgement(collapsed))
            return null;
        return collapsed;
    }

    private static bool IsSlashCommand(string text)
    {
        if (!text.StartsWith("/", StringComparison.Ordinal))
            return false;

        return !text.Contains('\n') && text.Count(ch => ch == ' ') <= 1 && text.Length <= 48;
    }

    private static bool IsAcknowledgement(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        return normalized is "ok" or "okay" or "thanks" or "thank you" or "got it" or "continue" or "继续" or "好的" or "收到" or "明白了";
    }

    private static string NormalizeForDedup(string text) => text.Trim().ToLowerInvariant();

    internal static string BuildThreadPreview(SessionThread thread)
    {
        var preview = ExtractUserSnippets(thread).FirstOrDefault();
        return string.IsNullOrWhiteSpace(preview) ? string.Empty : SanitizeSuggestionField(preview, MaxPreviewChars);
    }

    internal static string ExtractAgentSummary(SessionThread thread)
    {
        foreach (var turn in thread.Turns.OrderByDescending(turn => turn.StartedAt))
        {
            foreach (var item in turn.Items.AsEnumerable().Reverse())
            {
                if (item.Type != ItemType.AgentMessage || item.AsAgentMessage is not { } payload)
                    continue;
                var summary = NormalizeSnippet(payload.Text) ?? SanitizeSuggestionField(payload.Text, MaxAgentSummaryChars);
                if (!string.IsNullOrWhiteSpace(summary))
                    return SanitizeSuggestionField(summary, MaxAgentSummaryChars);
            }
        }

        return string.Empty;
    }

    internal static string[] ExtractDominantIntents(IEnumerable<string> snippets)
    {
        var normalized = snippets
            .Select(SimplifyIntentPhrase)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .GroupBy(text => text, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key.Length)
            .Select(group => group.Key)
            .Take(3)
            .ToArray();

        return normalized;
    }

    internal static string[] ExtractMemoryHighlights(string memoryText, string historyText)
    {
        return ExtractCandidateHighlights(memoryText)
            .Concat(ExtractCandidateHighlights(historyText))
            .Where(text => ScoreSnippetSpecificity(text) > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ScoreSnippetSpecificity)
            .ThenByDescending(text => text.Length)
            .Take(MaxHighlightCount)
            .Select(text => SanitizeSuggestionField(text, 180))
            .ToArray();
    }

    internal static string CombineMemory(string memoryText, string historyText, int totalMemoryCharsLimit)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(memoryText))
        {
            sb.AppendLine("## MEMORY.md");
            sb.AppendLine(memoryText.Trim());
        }

        if (!string.IsNullOrWhiteSpace(historyText))
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.AppendLine("## HISTORY.md (tail)");
            sb.AppendLine(historyText.Trim());
        }

        var combined = sb.ToString().Trim();
        return TrimToLimit(combined, totalMemoryCharsLimit);
    }

    internal static string ReadHistoryTailFromFile(string historyFilePath, int maxChars)
    {
        if (!File.Exists(historyFilePath))
            return string.Empty;

        try
        {
            var content = File.ReadAllText(historyFilePath, Encoding.UTF8);
            if (content.Length <= maxChars)
                return content;
            return content[^maxChars..];
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static string TrimToLimit(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var trimmed = text.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[^maxChars..].TrimStart();
    }

    private static bool IsSpecificSuggestion(string title, string prompt)
    {
        return HasSpecificitySignal(title) || (HasSpecificitySignal(prompt) && HasActionSignal(prompt));
    }

    private static bool HasSpecificitySignal(string text)
    {
        foreach (var signal in SpecificitySignals)
        {
            if (text.Contains(signal, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return text.Contains('/') || text.Contains('\\') || text.Contains('`') || text.Contains('_');
    }

    private static bool HasActionSignal(string text)
    {
        foreach (var signal in ActionSignals)
        {
            if (text.Contains(signal, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static int ScoreSnippetSpecificity(string text)
    {
        var score = 0;
        foreach (var signal in SpecificitySignals)
        {
            if (text.Contains(signal, StringComparison.OrdinalIgnoreCase))
                score += 3;
        }

        foreach (var signal in ActionSignals)
        {
            if (text.Contains(signal, StringComparison.OrdinalIgnoreCase))
                score += 2;
        }

        if (text.Contains('/') || text.Contains('\\') || text.Contains('.') || text.Contains('`'))
            score += 2;

        return score;
    }

    private static string SimplifyIntentPhrase(string text)
    {
        var normalized = text.Trim();
        if (normalized.Length > 90)
            normalized = normalized[..90].TrimEnd();
        return normalized;
    }

    private static IEnumerable<string> ExtractCandidateHighlights(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var parts = text
            .Split(['\r', '\n', '.', '!', '?', '。', '！', '？'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            var normalized = NormalizeSnippet(part);
            if (normalized != null)
                yield return normalized;
        }
    }

    private static WelcomeSuggestionsResult BuildNoSuggestionsResult(string fingerprint) =>
        new()
        {
            Items = [],
            Source = "none",
            GeneratedAt = DateTimeOffset.UtcNow,
            Fingerprint = fingerprint
        };

    private static string BuildFingerprint(
        string workspacePath,
        int maxItems,
        IReadOnlyList<ThreadSummary> threads,
        IReadOnlyList<string> snippets,
        string memoryPath,
        string historyPath,
        string memoryContext)
    {
        var sb = new StringBuilder();
        sb.AppendLine(workspacePath);
        sb.AppendLine($"maxItems:{maxItems}");
        foreach (var thread in threads)
            sb.AppendLine($"{thread.Id}|{thread.LastActiveAt:O}");
        foreach (var snippet in snippets)
            sb.AppendLine($"snippet:{snippet}");
        sb.AppendLine($"memoryMtime:{GetFileTimestamp(memoryPath):O}");
        sb.AppendLine($"historyMtime:{GetFileTimestamp(historyPath):O}");
        sb.AppendLine(memoryContext);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static DateTimeOffset GetFileTimestamp(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.UnixEpoch;

    internal static bool IsInternalThread(ThreadSummary summary)
    {
        if (summary.Metadata.TryGetValue(WelcomeSuggestionConstants.InternalMetadataKey, out var value)
            && string.Equals(value, WelcomeSuggestionConstants.InternalMetadataValue, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (summary.Metadata.TryGetValue(CommitMessageSuggestConstants.InternalMetadataKey, out var commitValue)
            && string.Equals(commitValue, CommitMessageSuggestConstants.InternalMetadataValue, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(summary.OriginChannel, WelcomeSuggestionConstants.ChannelName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(summary.OriginChannel, CommitMessageSuggestConstants.ChannelName, StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeWorkspacePath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static int ClampMaxItems(int? maxItems)
    {
        if (maxItems is not > 0)
            return DefaultMaxItems;
        return Math.Min(maxItems.Value, MaxItemsLimit);
    }

    private sealed record WelcomeSuggestionEvidence(
        IReadOnlyList<ThreadSummary> Threads,
        IReadOnlyList<string> Snippets,
        string MemoryContext,
        string Fingerprint,
        string CacheKey,
        bool HasSufficientContext);

    private sealed record WelcomeSuggestionCacheEntry(
        WelcomeSuggestionsResult Result,
        DateTimeOffset ExpiresAt);
}
