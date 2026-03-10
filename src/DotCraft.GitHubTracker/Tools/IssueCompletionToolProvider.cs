using DotCraft.Abstractions;
using DotCraft.GitHubTracker.Tracker;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.GitHubTracker.Tools;

/// <summary>
/// Per-issue tool provider that exposes <c>complete_issue</c> to the agent.
/// Injected into each issue's agent session by IssueAgentRunnerFactory.
/// </summary>
public sealed class IssueCompletionToolProvider(
    string issueId,
    IIssueTracker tracker,
    ILogger<IssueCompletionToolProvider> logger) : IAgentToolProvider
{
    public int Priority => 95;

    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        yield return AIFunctionFactory.Create(
            async ([System.ComponentModel.Description("Brief description of what was done to complete the issue.")] string reason) =>
            {
                logger.LogInformation("Agent calling complete_issue for {IssueId}: {Reason}", issueId, reason);
                try
                {
                    await tracker.CloseIssueAsync(issueId, reason);
                    return $"Issue #{issueId} has been marked as complete and closed on the tracker.";
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to close issue #{IssueId}", issueId);
                    return $"Warning: could not close issue on tracker ({ex.Message}). Ensure the token has Issues write permission.";
                }
            },
            "complete_issue",
            "Call this when the task is fully complete. Marks the issue as done and closes it on the tracker. " +
            "Only call after you have committed and pushed all required code changes.");
    }
}
