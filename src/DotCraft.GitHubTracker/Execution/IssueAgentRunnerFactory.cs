using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.DashBoard;
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

    /// <summary>The issue transitioned to a non-active state mid-run; no further work needed.</summary>
    IssueStateChanged,
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
}

/// <summary>
/// Creates and runs per-issue agent execution with the full tool pipeline.
/// Each issue gets its own workspace, session, and memory.
/// </summary>
public sealed class IssueAgentRunnerFactory(
    AppConfig config,
    IIssueTracker tracker,
    IssueWorkspaceManager workspaceManager,
    ModuleRegistry moduleRegistry,
    SkillsLoader skillsLoader,
    ILogger<IssueAgentRunnerFactory> logger,
    ILoggerFactory loggerFactory,
    TraceCollector? traceCollector = null) : IDisposable
{
    private readonly SemaphoreSlim _concurrencyGate = new(config.GitHubTracker.Agent.MaxConcurrentAgents);

    /// <summary>
    /// Computes the deterministic dashboard session key for an issue identifier.
    /// </summary>
    public static string GetSessionKey(string identifier) =>
        $"github-tracker:{IssueWorkspaceManager.SanitizeIdentifier(identifier)}";

    /// <summary>
    /// Runs an issue through the full agent pipeline with multi-turn support.
    /// Invokes <paramref name="onTurnCompleted"/> after each turn with live token metrics.
    /// </summary>
    public async Task<AgentRunOutcome> RunAsync(
        TrackedIssue issue,
        WorkflowDefinition workflow,
        int? attempt,
        CancellationToken ct,
        Action<int, long, long, long>? onTurnCompleted = null)
    {
        await _concurrencyGate.WaitAsync(ct);
        try
        {
            return await RunCoreAsync(issue, workflow, attempt, ct, onTurnCompleted);
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private async Task<AgentRunOutcome> RunCoreAsync(
        TrackedIssue issue,
        WorkflowDefinition workflow,
        int? attempt,
        CancellationToken ct,
        Action<int, long, long, long>? onTurnCompleted)
    {
        var workspace = await workspaceManager.EnsureWorkspaceAsync(issue.Identifier, ct);

        logger.LogInformation("Starting agent for {Identifier} in workspace {Path}",
            issue.Identifier, workspace.Path);

        await workspaceManager.RunBeforeRunHookAsync(workspace.Path, ct);

        var sessionKey = GetSessionKey(issue.Identifier);
        var craftPath = workspace.CraftPath;
        var workspacePath = workspace.Path;

        var memoryStore = new MemoryStore(craftPath);
        var sessionStore = new SessionStore(craftPath, config.CompactSessions);
        var approvalService = new AutoApproveApprovalService();
        var blacklist = new PathBlacklist([]);

        var toolProviders = ToolProviderCollector.Collect(moduleRegistry, config);

        // Add per-issue completion tool so the agent can signal task completion
        toolProviders.Add(new IssueCompletionToolProvider(
            issue.Id,
            tracker,
            loggerFactory.CreateLogger<IssueCompletionToolProvider>()));
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

        try
        {
            var maxTurns = workflow.Config.Agent.MaxTurns;
            var turnTimeoutMs = workflow.Config.Agent.TurnTimeoutMs;

            for (var turn = 1; turn <= maxTurns; turn++)
            {
                ct.ThrowIfCancellationRequested();

                var prompt = BuildTurnPrompt(workflow, issue, attempt, turn, maxTurns);
                logger.LogDebug("Running turn {Turn}/{MaxTurns} for {Identifier}", turn, maxTurns, issue.Identifier);

                using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                if (turnTimeoutMs > 0)
                    turnCts.CancelAfter(turnTimeoutMs);

                await agentRunner.RunAsync(prompt, sessionKey, turnCts.Token);

                // Accumulate token usage reported by the token tracker for this turn
                cumulativeInput += tokenTracker.LastInputTokens;
                var cumulativeOutput = tokenTracker.TotalOutputTokens;
                turnsCompleted = turn;
                onTurnCompleted?.Invoke(turn, cumulativeInput, cumulativeOutput, cumulativeInput + cumulativeOutput);

                // Check issue state after each turn
                try
                {
                    var states = await tracker.FetchIssueStatesByIdsAsync([issue.Id], ct);
                    if (states.Count > 0)
                    {
                        var currentState = states[0].State;
                        var activeStates = workflow.Config.Tracker.ActiveStates;
                        var isStillActive = activeStates.Any(a =>
                            string.Equals(a.Trim(), currentState.Trim(), StringComparison.OrdinalIgnoreCase));

                        if (!isStillActive)
                        {
                            logger.LogInformation("Issue {Identifier} is no longer active after turn {Turn}, stopping",
                                issue.Identifier, turn);
                            result = AgentRunResult.IssueStateChanged;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to check issue state after turn {Turn}", turn);
                    result = AgentRunResult.IssueStateChanged;
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
            InputTokens = cumulativeInput,
            OutputTokens = finalOutput,
            TotalTokens = cumulativeInput + finalOutput,
            TurnsCompleted = turnsCompleted,
        };
    }

    /// <inheritdoc />
    public void Dispose() => _concurrencyGate.Dispose();

    private string BuildTurnPrompt(WorkflowDefinition workflow, TrackedIssue issue, int? attempt, int turn, int maxTurns)
    {
        if (turn == 1)
        {
            var issueData = new Dictionary<string, object?>
            {
                ["id"] = issue.Id,
                ["identifier"] = issue.Identifier,
                ["title"] = issue.Title,
                ["description"] = issue.Description,
                ["priority"] = issue.Priority,
                ["state"] = issue.State,
                ["branch_name"] = issue.BranchName,
                ["url"] = issue.Url,
                ["labels"] = issue.Labels.ToList(),
                ["created_at"] = issue.CreatedAt?.ToString("o"),
                ["updated_at"] = issue.UpdatedAt?.ToString("o"),
            };

            try
            {
                return new WorkflowLoader(workflow.Config, logger as ILogger<WorkflowLoader>
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkflowLoader>.Instance)
                    .RenderPrompt(workflow.PromptTemplate, issueData, attempt);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to render workflow prompt, using fallback");
                return $"You are working on issue {issue.Identifier}: {issue.Title}\n\n{issue.Description}";
            }
        }

        return $"""
            Continue working on issue {issue.Identifier}: {issue.Title}
            This is turn {turn} of {maxTurns}. Check your progress and continue where you left off.
            If the task is complete, summarize what was done.
            """;
    }
}
