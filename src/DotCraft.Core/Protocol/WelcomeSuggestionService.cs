using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private const int RecentThreadLimit = 12;
    private const int MaxSnippetCount = 20;
    private const int MinSnippetLength = 15;
    private const int MaxSnippetLength = 300;
    private const int MemoryCharsLimit = 5_000;
    private const int HistoryTailCharsLimit = 3_000;
    private const int TotalMemoryCharsLimit = 8_000;
    private static readonly TimeSpan SuggestTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, WelcomeSuggestionCacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<WelcomeSuggestionsResult>>> _inflight = new(StringComparer.Ordinal);

    public async Task<WelcomeSuggestionsResult> SuggestAsync(
        WelcomeSuggestionsParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Identity == null)
            throw new InvalidOperationException("identity is required.");

        var workspacePath = NormalizeWorkspace(parameters.Identity.WorkspacePath);
        if (string.IsNullOrWhiteSpace(workspacePath))
            throw new InvalidOperationException("identity.workspacePath is required.");
        if (!string.Equals(workspacePath, NormalizeWorkspace(workspaceRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Requested workspace is not hosted by this AppServer instance.");

        var maxItems = ClampMaxItems(parameters.MaxItems);
        var evidence = await BuildEvidenceAsync(workspacePath, maxItems, cancellationToken).ConfigureAwait(false);

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
        WelcomeSuggestionsResult result;
        if (evidence.Snippets.Count < 3 && string.IsNullOrWhiteSpace(evidence.MemoryContext))
        {
            result = BuildFallbackResult(maxItems, evidence.Fingerprint);
        }
        else
        {
            result = await GenerateDynamicSuggestionsAsync(identity, evidence, maxItems, cancellationToken).ConfigureAwait(false);
        }

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
                    HistoryMode.Client,
                    displayName: "[internal] Welcome suggestions",
                    ct: linked.Token)
                .ConfigureAwait(false);

            tempThreadId = tempThread.Id;
            tempThread.Metadata[WelcomeSuggestionConstants.InternalMetadataKey] =
                WelcomeSuggestionConstants.InternalMetadataValue;

            var history = BuildEvidenceMessages(evidence, maxItems);
            var userPrompt = BuildGenerationPrompt(maxItems);

            List<WelcomeSuggestionItem>? items = null;
            await foreach (var evt in sessionService.SubmitInputAsync(
                               tempThreadId,
                               userPrompt,
                               messages: history,
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Welcome suggestion generation failed; falling back to static suggestions.");
            return BuildFallbackResult(maxItems, evidence.Fingerprint);
        }
        finally
        {
            if (tempThreadId != null)
            {
                try
                {
                    using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await sessionService.DeleteThreadPermanentlyAsync(tempThreadId, cleanupCts.Token).ConfigureAwait(false);
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
                string.Equals(NormalizeWorkspace(summary.WorkspacePath), workspacePath, StringComparison.OrdinalIgnoreCase)
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
        var historyText = TrimToLimit(ReadHistoryTail(), HistoryTailCharsLimit);
        var combinedMemory = CombineMemory(memoryText, historyText);
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
            $"{fingerprint}:{maxItems}");
    }

    private static ChatMessage[] BuildEvidenceMessages(WelcomeSuggestionEvidence evidence, int maxItems)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Generate exactly {maxItems} welcome suggestions.");
        sb.AppendLine();

        if (evidence.Threads.Count > 0)
        {
            sb.AppendLine("Recent threads:");
            foreach (var thread in evidence.Threads)
            {
                var name = string.IsNullOrWhiteSpace(thread.DisplayName) ? thread.Id : thread.DisplayName.Trim();
                sb.Append("- ").Append(name);
                sb.Append(" | origin=").Append(thread.OriginChannel);
                sb.Append(" | lastActiveAt=").AppendLine(thread.LastActiveAt.ToString("O"));
            }
            sb.AppendLine();
        }

        if (evidence.Snippets.Count > 0)
        {
            sb.AppendLine("Recent user intent snippets:");
            foreach (var snippet in evidence.Snippets)
                sb.Append("- ").AppendLine(snippet);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(evidence.MemoryContext))
        {
            sb.AppendLine("Workspace memory:");
            sb.AppendLine(evidence.MemoryContext);
        }

        return [new ChatMessage(ChatRole.User, sb.ToString().Trim())];
    }

    private static string BuildGenerationPrompt(int maxItems) =>
        $"Call {WelcomeSuggestionMethods.ToolName} now with exactly {maxItems} suggestions.";

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
        foreach (var turn in thread.Turns)
        {
            foreach (var item in turn.Items)
            {
                if (item.Type != ItemType.UserMessage || item.AsUserMessage is not { } payload)
                    continue;

                var normalized = NormalizeSnippet(payload.Text);
                if (normalized == null)
                    continue;

                yield return normalized;
            }
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

    private static string CombineMemory(string memoryText, string historyText)
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
        return TrimToLimit(combined, TotalMemoryCharsLimit);
    }

    private string ReadHistoryTail()
    {
        if (!File.Exists(memoryStore.HistoryFilePath))
            return string.Empty;

        try
        {
            var content = File.ReadAllText(memoryStore.HistoryFilePath, Encoding.UTF8);
            if (content.Length <= HistoryTailCharsLimit)
                return content;
            return content[^HistoryTailCharsLimit..];
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to read HISTORY.md tail for welcome suggestions.");
            return string.Empty;
        }
    }

    private static string TrimToLimit(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var trimmed = text.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[^maxChars..].TrimStart();
    }

    private static WelcomeSuggestionsResult BuildFallbackResult(int maxItems, string fingerprint) =>
        new()
        {
            Items =
            [
                .. GetFallbackItems()
                    .Take(maxItems)
                    .Select(item => new WelcomeSuggestionItem
                    {
                        Title = item.Title,
                        Prompt = item.Prompt,
                        Reason = item.Reason
                    })
            ],
            Source = "fallback",
            GeneratedAt = DateTimeOffset.UtcNow,
            Fingerprint = fingerprint
        };

    private static IReadOnlyList<WelcomeSuggestionItem> GetFallbackItems() =>
    [
        new()
        {
            Title = "Explore this workspace",
            Prompt = "Give me a quick overview of this project: what it does, its structure, and where the main entry points are.",
            Reason = "Static fallback overview prompt."
        },
        new()
        {
            Title = "Find likely bugs",
            Prompt = "Scan the codebase for potential bugs, error-prone patterns, or unhandled edge cases and suggest fixes.",
            Reason = "Static fallback bug-finding prompt."
        },
        new()
        {
            Title = "Plan a new feature",
            Prompt = "Help me design and implement a new feature for this project. Describe what you want to build.",
            Reason = "Static fallback feature-design prompt."
        },
        new()
        {
            Title = "Write docs",
            Prompt = "Generate clear documentation for this codebase: README sections, inline comments, and API docs.",
            Reason = "Static fallback documentation prompt."
        }
    ];

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

    private static bool IsInternalThread(ThreadSummary summary)
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

    private static string NormalizeWorkspace(string? path) =>
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
        string CacheKey);

    private sealed record WelcomeSuggestionCacheEntry(
        WelcomeSuggestionsResult Result,
        DateTimeOffset ExpiresAt);
}
