using System.ComponentModel;
using DotCraft.Abstractions;
using DotCraft.GitHubTracker.Tracker;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.GitHubTracker.Tools;

/// <summary>
/// Per-PR tool provider that exposes <c>SubmitReview</c> to the agent.
/// Injected into each PR review agent session by WorkItemAgentRunnerFactory.
/// </summary>
public sealed class PullRequestReviewToolProvider(
    string pullNumber,
    IWorkItemTracker tracker,
    ILogger<PullRequestReviewToolProvider> logger) : IAgentToolProvider
{
    public int Priority => 95;

    /// <summary>
    /// Set to true after <c>SubmitReview</c> succeeds. The runner loop checks
    /// this flag after each turn and exits normally when it is true, allowing
    /// the orchestrator to remove the PullRequestLabelFilter label.
    /// </summary>
    public bool ReviewCompleted { get; private set; }

    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        yield return AIFunctionFactory.Create(
            async (
                [Description("Review event: APPROVE, REQUEST_CHANGES, or COMMENT.")] string reviewEvent,
                [Description("Review body summarizing your findings.")] string body) =>
            {
                var normalized = reviewEvent.Trim().ToUpperInvariant() switch
                {
                    "APPROVE" => "APPROVE",
                    "REQUEST_CHANGES" => "REQUEST_CHANGES",
                    _ => "COMMENT",
                };

                logger.LogInformation("Agent submitting {Event} review on PR #{Number}", normalized, pullNumber);
                try
                {
                    await tracker.SubmitReviewAsync(pullNumber, body, normalized);
                    ReviewCompleted = true;
                    return $"Review ({normalized}) submitted on PR #{pullNumber}. The review is complete.";
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to submit review on PR #{Number}", pullNumber);
                    return $"Warning: could not submit review ({ex.Message}). Ensure the token has pull-request write permission.";
                }
            },
            "SubmitReview",
            "Submit a review on the pull request. Use APPROVE when the code looks correct, " +
            "REQUEST_CHANGES when issues must be fixed, or COMMENT for general feedback. " +
            "Call this once you have finished reviewing all changed files.");
    }
}
