using System.ComponentModel;
using DotCraft.Abstractions;
using DotCraft.GitHubTracker.Tracker;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.GitHubTracker.Tools;

/// <summary>
/// Per-PR tool provider that exposes <c>SubmitReview</c> to the agent.
/// Injected into each PR review agent session by WorkItemAgentRunnerFactory.
/// Reviews are always submitted as COMMENT events; automated bot reviews must not
/// affect the PR's approval/rejection status on GitHub.
/// See PR Lifecycle Spec section 7.1.
/// </summary>
public sealed class PullRequestReviewToolProvider(
    string pullNumber,
    IWorkItemTracker tracker,
    ILogger<PullRequestReviewToolProvider> logger) : IAgentToolProvider
{
    public int Priority => 95;

    /// <summary>
    /// Set to true after <c>SubmitReview</c> succeeds. The runner loop checks
    /// this flag after each turn and exits normally when it is true, signaling
    /// the orchestrator to record the reviewed SHA.
    /// See PR Lifecycle Spec section 7.2.
    /// </summary>
    public bool ReviewCompleted { get; private set; }

    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        yield return AIFunctionFactory.Create(
            async (
                [Description("Review event type. Accepted for compatibility but always submitted as COMMENT.")] string reviewEvent,
                [Description("Review body summarizing your findings.")] string body) =>
            {
                // Always submit as COMMENT; automated reviews must not approve or request changes.
                // See PR Lifecycle Spec section 7.1.
                var normalized = "COMMENT";

                logger.LogInformation("Agent submitting {Event} review on PR #{Number}", normalized, pullNumber);
                try
                {
                    await tracker.SubmitReviewAsync(pullNumber, body, normalized);
                    ReviewCompleted = true;
                    return $"Review (COMMENT) submitted on PR #{pullNumber}. The review is complete.";
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to submit review on PR #{Number}", pullNumber);
                    return $"Warning: could not submit review ({ex.Message}). Ensure the token has pull-request write permission.";
                }
            },
            "SubmitReview",
            "Submit a COMMENT review on the pull request with your findings. " +
            "All automated reviews are submitted as COMMENT to avoid interfering with the human approval workflow. " +
            "Call this once you have finished reviewing all changed files.");
    }
}
