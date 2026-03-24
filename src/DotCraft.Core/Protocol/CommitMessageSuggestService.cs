using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Protocol;

/// <summary>
/// Orchestrates commit-message suggestion via an ephemeral thread and the <see cref="CommitSuggestToolProvider"/> profile.
/// </summary>
public interface ICommitMessageSuggestService
{
    Task<WorkspaceCommitMessageSuggestResult> SuggestAsync(
        WorkspaceCommitMessageSuggestParams parameters,
        CancellationToken cancellationToken = default);
}

public sealed class CommitMessageSuggestService(
    ISessionService sessionService,
    string workspaceRoot,
    ILogger<CommitMessageSuggestService>? logger = null) : ICommitMessageSuggestService
{
    private const int DefaultMaxDiffChars = 100_000;
    private const int MaxContextChars = 60_000;
    private static readonly TimeSpan SuggestTimeout = TimeSpan.FromMinutes(2);

    public async Task<WorkspaceCommitMessageSuggestResult> SuggestAsync(
        WorkspaceCommitMessageSuggestParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.ThreadId))
            throw new InvalidOperationException("threadId is required.");
        if (parameters.Paths is not { Length: > 0 })
            throw new InvalidOperationException("paths must contain at least one path.");

        var ws = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        ValidateRelativePaths(ws, parameters.Paths);

        var sourceThread = await sessionService.GetThreadAsync(parameters.ThreadId, cancellationToken)
            .ConfigureAwait(false);

        var sourceWs = Path.GetFullPath(sourceThread.WorkspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.Equals(sourceWs, ws, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source thread belongs to a different workspace.");

        var maxDiff = parameters.MaxDiffChars is > 0 and int m ? m : DefaultMaxDiffChars;
        var diffText = RunGitDiff(ws, parameters.Paths, maxDiff, logger);
        if (string.IsNullOrWhiteSpace(diffText))
            throw new InvalidOperationException("No diff for the given paths (nothing to commit or not a git repository).");

        var history = BuildHistoryMessages(sourceThread, MaxContextChars);
        if (history.Count == 0)
            history.Add(new ChatMessage(ChatRole.User, "[No prior user or agent messages in this thread.]"));
        var userPrompt = BuildUserPrompt(parameters.Paths, diffText);

        string? tempThreadId = null;
        try
        {
            using var timeoutCts = new CancellationTokenSource(SuggestTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var identity = new SessionIdentity
            {
                ChannelName = CommitMessageSuggestConstants.ChannelName,
                UserId = CommitMessageSuggestConstants.InternalUserId,
                WorkspacePath = ws,
                ChannelContext = $"commit-suggest:{parameters.ThreadId}"
            };

            var config = new ThreadConfiguration
            {
                Mode = "agent",
                ToolProfile = CommitMessageSuggestConstants.ToolProfileName,
                UseToolProfileOnly = true,
                ApprovalPolicy = ApprovalPolicy.AutoApprove,
                AgentInstructions = CommitMessageSuggestInstructions.SystemPrompt
            };

            var tempThread = await sessionService.CreateThreadAsync(
                    identity,
                    config,
                    HistoryMode.Client,
                    displayName: "[internal] Commit suggestion",
                    ct: linked.Token)
                .ConfigureAwait(false);

            tempThreadId = tempThread.Id;
            tempThread.Metadata[CommitMessageSuggestConstants.InternalMetadataKey] =
                CommitMessageSuggestConstants.InternalMetadataValue;

            string? summary = null;
            string? body = null;

            await foreach (var evt in sessionService.SubmitInputAsync(
                               tempThreadId,
                               userPrompt,
                               messages: history.ToArray(),
                               ct: linked.Token).ConfigureAwait(false))
            {
                if (evt.EventType != SessionEventType.ItemCompleted || evt.ItemPayload == null)
                    continue;
                if (evt.ItemPayload.Type != ItemType.ToolCall)
                    continue;
                var tc = evt.ItemPayload.AsToolCall;
                if (tc == null || !string.Equals(tc.ToolName, CommitSuggestMethods.ToolName, StringComparison.Ordinal))
                    continue;
                (summary, body) = ParseCommitSuggestArgs(tc.Arguments);
                if (!string.IsNullOrWhiteSpace(summary))
                    break;
            }

            if (string.IsNullOrWhiteSpace(summary))
                throw new InvalidOperationException(
                    "The model did not call CommitSuggest with a summary. Try again or edit the message manually.");

            var message = string.IsNullOrWhiteSpace(body)
                ? summary.Trim()
                : summary.Trim() + Environment.NewLine + Environment.NewLine + body.Trim();

            return new WorkspaceCommitMessageSuggestResult { Message = message };
        }
        finally
        {
            if (tempThreadId != null)
            {
                try
                {
                    await sessionService.DeleteThreadPermanentlyAsync(tempThreadId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to delete ephemeral commit-suggest thread {ThreadId}", tempThreadId);
                }
            }
        }
    }

    private static string BuildUserPrompt(string[] paths, string diffText)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Produce a git commit message for the following changes.");
        sb.AppendLine("Paths (relative to workspace): " + string.Join(", ", paths));
        sb.AppendLine();
        sb.AppendLine("--- unified diff ---");
        sb.AppendLine(diffText);
        return sb.ToString();
    }

    private static (string? Summary, string? Body) ParseCommitSuggestArgs(JsonObject? arguments)
    {
        if (arguments == null)
            return (null, null);
        string? summary = null;
        string? body = null;
        if (arguments.TryGetPropertyValue("summary", out var sNode))
            summary = sNode?.GetValue<string>();
        if (arguments.TryGetPropertyValue("body", out var bNode))
            body = bNode?.GetValue<string>();
        return (summary, body);
    }

    private static List<ChatMessage> BuildHistoryMessages(SessionThread thread, int maxChars)
    {
        var pairs = new List<(ChatRole Role, string Text)>();
        foreach (var turn in thread.Turns)
        {
            foreach (var item in turn.Items)
            {
                if (item.Type == ItemType.UserMessage && item.Payload is UserMessagePayload u &&
                    !string.IsNullOrWhiteSpace(u.Text))
                    pairs.Add((ChatRole.User, u.Text.Trim()));
                else if (item.Type == ItemType.AgentMessage && item.Payload is AgentMessagePayload a &&
                         !string.IsNullOrWhiteSpace(a.Text))
                    pairs.Add((ChatRole.Assistant, a.Text.Trim()));
            }
        }

        while (pairs.Count > 0)
        {
            var total = pairs.Sum(p => p.Text.Length);
            if (total <= maxChars)
                break;
            pairs.RemoveAt(0);
        }

        return pairs.Select(p => new ChatMessage(p.Role, p.Text)).ToList();
    }

    private static void ValidateRelativePaths(string workspaceRoot, string[] paths)
    {
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p))
                throw new InvalidOperationException("paths must not contain empty entries.");
            if (p.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException("paths must not contain '..'.");
            var combined = Path.GetFullPath(Path.Combine(workspaceRoot, p));
            if (!combined.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase) ||
                (combined.Length > workspaceRoot.Length &&
                 combined[workspaceRoot.Length] != Path.DirectorySeparatorChar &&
                 combined[workspaceRoot.Length] != Path.AltDirectorySeparatorChar))
                throw new InvalidOperationException($"Path escapes workspace: {p}");
        }
    }

    private static string RunGitDiff(string workspaceRoot, string[] paths, int maxChars, ILogger? logger)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("diff");
        psi.ArgumentList.Add("--no-color");
        psi.ArgumentList.Add("HEAD");
        psi.ArgumentList.Add("--");
        foreach (var p in paths)
            psi.ArgumentList.Add(p.Replace('/', Path.DirectorySeparatorChar));

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
                throw new InvalidOperationException("Could not start git.");
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(60_000);
            if (proc.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
            {
                logger?.LogWarning("git diff failed: {Stderr}", stderr);
                return string.Empty;
            }

            if (stdout.Length > maxChars)
                return stdout[..maxChars] + "\n\n[diff truncated]";
            return stdout;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            logger?.LogWarning(ex, "git diff execution failed");
            throw new InvalidOperationException("Failed to run git diff. Ensure git is installed and the workspace is a repository.");
        }
    }
}
