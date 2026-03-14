using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tracing;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.GitHubTracker.Tracker;
using DotCraft.GitHubTracker.Tools;
using DotCraft.GitHubTracker.Workflow;
using DotCraft.GitHubTracker.Workspace;
using DotCraft.Tools;
using Microsoft.Extensions.Logging;

namespace DotCraft.GitHubTracker.Execution;

/// <summary>
/// Indicates why an agent run ended.
/// </summary>
public enum AgentRunResult
{
    /// <summary>All allocated turns were consumed; the issue is still active on the tracker.</summary>
    TurnsExhausted,

    /// <summary>The work item transitioned to a non-active state mid-run; no further work needed.</summary>
    WorkItemStateChanged,
}

/// <summary>
/// Full outcome of an agent run including result reason, token usage, and turn count.
/// </summary>
public sealed class AgentRunOutcome
{
    public required AgentRunResult Result { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long TotalTokens { get; init; }
    public int TurnsCompleted { get; init; }

    /// <summary>
    /// True only when the PR review agent successfully called SubmitReview.
    /// Distinct from <see cref="AgentRunResult.WorkItemStateChanged"/>, which can also
    /// be set on transient state-check failures or when the PR becomes inactive mid-run.
    /// </summary>
    public bool ReviewSubmitted { get; init; }
}

/// <summary>
/// Creates and runs per-work-item agent execution with the full tool pipeline.
/// Each work item gets its own workspace, session, and memory.
/// </summary>
public sealed class WorkItemAgentRunnerFactory(
    AppConfig config,
    IWorkItemTracker tracker,
    WorkItemWorkspaceManager workspaceManager,
    ModuleRegistry moduleRegistry,
    SkillsLoader skillsLoader,
    ILogger<WorkItemAgentRunnerFactory> logger,
    ILoggerFactory loggerFactory,
    TraceCollector? traceCollector = null) : IDisposable
{
    private readonly SemaphoreSlim _concurrencyGate = new(config.GetSection<GitHubTrackerConfig>("GitHubTracker").Agent.MaxConcurrentAgents);

    /// <summary>
    /// Computes the deterministic dashboard session key for a work-item identifier.
    /// </summary>
    public static string GetSessionKey(string identifier) =>
        $"github-tracker:{WorkItemWorkspaceManager.SanitizeIdentifier(identifier)}";

    /// <summary>
    /// Runs a work item through the full agent pipeline with multi-turn support.
    /// Invokes <paramref name="onTurnCompleted"/> after each turn with live token metrics.
    /// </summary>
    public async Task<AgentRunOutcome> RunAsync(
        TrackedWorkItem workItem,
        WorkflowDefinition workflow,
        int? attempt,
        CancellationToken ct,
        Action<int, long, long, long>? onTurnCompleted = null)
    {
        await _concurrencyGate.WaitAsync(ct);
        try
        {
            return await RunCoreAsync(workItem, workflow, attempt, ct, onTurnCompleted);
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private async Task<AgentRunOutcome> RunCoreAsync(
        TrackedWorkItem workItem,
        WorkflowDefinition workflow,
        int? attempt,
        CancellationToken ct,
        Action<int, long, long, long>? onTurnCompleted)
    {
        var workspace = await workspaceManager.EnsureWorkspaceAsync(workItem, ct);

        logger.LogInformation("Starting agent for {Identifier} ({Kind}) in workspace {Path}",
            workItem.Identifier, workItem.Kind, workspace.Path);

        await workspaceManager.RunBeforeRunHookAsync(workspace.Path, ct);

        var sessionKey = GetSessionKey(workItem.Identifier);
        var craftPath = workspace.CraftPath;
        var workspacePath = workspace.Path;

        var memoryStore = new MemoryStore(craftPath);
        var sessionStore = new SessionStore(craftPath, config.CompactSessions);
        var approvalService = new AutoApproveApprovalService();
        var blacklist = new PathBlacklist([]);

        var toolProviders = ToolProviderCollector.Collect(moduleRegistry, config);

        // Inject kind-specific completion tool
        PullRequestReviewToolProvider? prReviewTool = null;
        if (workItem.Kind == WorkItemKind.PullRequest)
        {
            prReviewTool = new PullRequestReviewToolProvider(
                workItem.Id,
                tracker,
                loggerFactory.CreateLogger<PullRequestReviewToolProvider>());
            toolProviders.Add(prReviewTool);
        }
        else
        {
            toolProviders.Add(new IssueCompletionToolProvider(
                workItem.Id,
                tracker,
                loggerFactory.CreateLogger<IssueCompletionToolProvider>()));
        }

        var toolProviderContext = new ToolProviderContext
        {
            Config = config,
            ChatClient = null!,
            WorkspacePath = workspacePath,
            BotPath = craftPath,
            MemoryStore = memoryStore,
            SkillsLoader = skillsLoader,
            ApprovalService = approvalService,
            PathBlacklist = blacklist,
            TraceCollector = traceCollector,
        };

        await using var agentFactory = new AgentFactory(
            dotcraftPath: craftPath,
            workspacePath: workspacePath,
            config: config,
            memoryStore: memoryStore,
            skillsLoader: skillsLoader,
            approvalService: approvalService,
            blacklist: blacklist,
            toolProviders: toolProviders,
            toolProviderContext: toolProviderContext,
            traceCollector: traceCollector);

        var agent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        var agentRunner = new AgentRunner(agent, sessionStore, agentFactory, traceCollector);

        var result = AgentRunResult.TurnsExhausted;
        var tokenTracker = agentFactory.GetOrCreateTokenTracker(sessionKey);
        long cumulativeInput = 0;
        var turnsCompleted = 0;

        // For PRs, pre-fetch the diff to include in the first-turn prompt.
        string? prDiff = null;
        if (workItem.Kind == WorkItemKind.PullRequest)
        {
            try
            {
                prDiff = await tracker.FetchPullRequestDiffAsync(workItem.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch diff for PR {Identifier}", workItem.Identifier);
            }
        }

        try
        {
            var maxTurns = workflow.Config.Agent.MaxTurns;
            var turnTimeoutMs = workflow.Config.Agent.TurnTimeoutMs;

            for (var turn = 1; turn <= maxTurns; turn++)
            {
                ct.ThrowIfCancellationRequested();

                var prompt = BuildTurnPrompt(workflow, workItem, attempt, turn, maxTurns, prDiff);
                logger.LogDebug("Running turn {Turn}/{MaxTurns} for {Identifier}", turn, maxTurns, workItem.Identifier);

                using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                if (turnTimeoutMs > 0)
                    turnCts.CancelAfter(turnTimeoutMs);

                await agentRunner.RunAsync(prompt, sessionKey, turnCts.Token);

                cumulativeInput += tokenTracker.LastInputTokens;
                var cumulativeOutput = tokenTracker.TotalOutputTokens;
                turnsCompleted = turn;
                onTurnCompleted?.Invoke(turn, cumulativeInput, cumulativeOutput, cumulativeInput + cumulativeOutput);

                // For PR reviews: exit as soon as SubmitReview succeeds so the
                // orchestrator can remove the label and avoid re-dispatch.
                if (prReviewTool?.ReviewCompleted == true)
                {
                    logger.LogInformation("{Identifier} review submitted, stopping after turn {Turn}",
                        workItem.Identifier, turn);
                    result = AgentRunResult.WorkItemStateChanged;
                    break;
                }

                // Check work-item state after each turn
                try
                {
                    var states = await tracker.FetchWorkItemStatesByIdsAsync([workItem.Id], ct);
                    if (states.Count > 0)
                    {
                        var currentState = states[0].State;
                        var activeStates = GetActiveStatesForKind(workItem.Kind, workflow.Config);
                        var isStillActive = activeStates.Any(a =>
                            string.Equals(a.Trim(), currentState.Trim(), StringComparison.OrdinalIgnoreCase));

                        if (!isStillActive)
                        {
                            logger.LogInformation("{Identifier} is no longer active after turn {Turn}, stopping",
                                workItem.Identifier, turn);
                            result = AgentRunResult.WorkItemStateChanged;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to check state after turn {Turn}", turn);
                    result = AgentRunResult.WorkItemStateChanged;
                    break;
                }
            }
        }
        finally
        {
            await workspaceManager.RunAfterRunHookAsync(workspace.Path, ct);
        }

        var finalOutput = tokenTracker.TotalOutputTokens;
        return new AgentRunOutcome
        {
            Result = result,
            ReviewSubmitted = prReviewTool?.ReviewCompleted == true,
            InputTokens = cumulativeInput,
            OutputTokens = finalOutput,
            TotalTokens = cumulativeInput + finalOutput,
            TurnsCompleted = turnsCompleted,
        };
    }

    /// <inheritdoc />
    public void Dispose() => _concurrencyGate.Dispose();

    private static List<string> GetActiveStatesForKind(WorkItemKind kind, GitHubTrackerConfig cfg) =>
        kind == WorkItemKind.PullRequest
            ? cfg.Tracker.PullRequestActiveStates
            : cfg.Tracker.ActiveStates;

    private string BuildTurnPrompt(
        WorkflowDefinition workflow, TrackedWorkItem workItem, int? attempt,
        int turn, int maxTurns, string? prDiff = null)
    {
        if (turn == 1)
        {
            var workItemData = new Dictionary<string, object?>
            {
                ["id"] = workItem.Id,
                ["identifier"] = workItem.Identifier,
                ["title"] = workItem.Title,
                ["description"] = workItem.Description,
                ["priority"] = workItem.Priority,
                ["state"] = workItem.State,
                ["kind"] = workItem.Kind.ToString(),
                ["branch_name"] = workItem.BranchName,
                ["url"] = workItem.Url,
                ["labels"] = workItem.Labels.ToList(),
                ["created_at"] = workItem.CreatedAt?.ToString("o"),
                ["updated_at"] = workItem.UpdatedAt?.ToString("o"),
                ["head_branch"] = workItem.HeadBranch,
                ["base_branch"] = workItem.BaseBranch,
                ["diff_url"] = workItem.DiffUrl,
                ["review_state"] = workItem.ReviewState.ToString(),
                ["is_draft"] = workItem.IsDraft,
                ["diff"] = prDiff,
            };

            try
            {
                return new WorkflowLoader(workflow.Config, logger as ILogger<WorkflowLoader>
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkflowLoader>.Instance)
                    .RenderPrompt(workflow.PromptTemplate, workItemData, attempt);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to render workflow prompt, using fallback");
                return workItem.Kind == WorkItemKind.PullRequest
                    ? BuildFallbackPrPrompt(workItem, prDiff)
                    : $"You are working on issue {workItem.Identifier}: {workItem.Title}\n\n{workItem.Description}";
            }
        }

        if (workItem.Kind == WorkItemKind.PullRequest)
        {
            return $"""
                Continue reviewing PR {workItem.Identifier}: {workItem.Title}
                This is turn {turn} of {maxTurns}. Check your progress and continue the review.
                When finished, call SubmitReview with your verdict.
                """;
        }

        return $"""
            Continue working on issue {workItem.Identifier}: {workItem.Title}
            This is turn {turn} of {maxTurns}. Check your progress and continue where you left off.
            If the task is complete, summarize what was done.
            """;
    }

    private static string BuildFallbackPrPrompt(TrackedWorkItem pr, string? diff)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"You are reviewing pull request {pr.Identifier}: {pr.Title}");
        sb.AppendLine($"Branch: {pr.HeadBranch} -> {pr.BaseBranch}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(pr.Description))
        {
            sb.AppendLine("## PR Description");
            sb.AppendLine(pr.Description);
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(diff))
        {
            sb.AppendLine("## Diff");
            sb.AppendLine("```diff");
            sb.AppendLine(diff);
            sb.AppendLine("```");
        }
        sb.AppendLine();
        sb.AppendLine("Review the changes carefully. When done, call SubmitReview with APPROVE, REQUEST_CHANGES, or COMMENT.");
        return sb.ToString();
    }
}
