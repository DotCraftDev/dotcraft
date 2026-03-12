using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.DashBoard;
using DotCraft.Modules;
using DotCraft.Skills;
using DotCraft.GitHubTracker.Execution;
using DotCraft.GitHubTracker.Orchestrator;
using DotCraft.GitHubTracker.Tracker;
using DotCraft.GitHubTracker.Workflow;
using DotCraft.GitHubTracker.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotCraft.GitHubTracker;

/// <summary>
/// Autonomous issue orchestrator module. Polls issue trackers and dispatches
/// coding agents to work on issues independently.
/// </summary>
[DotCraftModule("github-tracker", Priority = 50, Description = "Autonomous GitHub issue tracker orchestrator")]
public sealed partial class GitHubTrackerModule : ModuleBase
{
    public override bool IsEnabled(AppConfig config) => config.GitHubTracker.Enabled;

    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
    {
        var errors = new List<string>();
        var tracker = config.GitHubTracker;

        if (string.IsNullOrWhiteSpace(tracker.Tracker.Repository))
            errors.Add("GitHubTracker: tracker.repository is required");

        var prOverlap = tracker.Tracker.PullRequestActiveStates
            .Intersect(tracker.Tracker.PullRequestTerminalStates, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (prOverlap.Count > 0)
            errors.Add($"GitHubTracker: PullRequestActiveStates and PullRequestTerminalStates must not overlap. Conflicting states: {string.Join(", ", prOverlap)}");

        var issueOverlap = tracker.Tracker.ActiveStates
            .Intersect(tracker.Tracker.TerminalStates, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (issueOverlap.Count > 0)
            errors.Add($"GitHubTracker: ActiveStates and TerminalStates must not overlap. Conflicting states: {string.Join(", ", issueOverlap)}");

        return errors;
    }

    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        var config = context.Config.GitHubTracker;

        var workspacePath = context.Paths.WorkspacePath;

        services.AddSingleton(config);
        services.AddSingleton(sp => new WorkflowLoader(
            config,
            sp.GetRequiredService<ILogger<WorkflowLoader>>()));
        services.AddSingleton(sp => new IssueWorkspaceManager(
            config,
            sp.GetRequiredService<ILogger<IssueWorkspaceManager>>()));
        services.AddSingleton<IIssueTracker>(sp => CreateTracker(config, workspacePath, sp));
        services.AddSingleton(sp => new IssueAgentRunnerFactory(
            sp.GetRequiredService<AppConfig>(),
            sp.GetRequiredService<IIssueTracker>(),
            sp.GetRequiredService<IssueWorkspaceManager>(),
            sp.GetRequiredService<ModuleRegistry>(),
            sp.GetRequiredService<SkillsLoader>(),
            sp.GetRequiredService<ILogger<IssueAgentRunnerFactory>>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetService<TraceCollector>()));
        services.AddSingleton(sp =>
        {
            var issueWorkflowLoader = sp.GetRequiredService<WorkflowLoader>();
            // Dedicated loader for PR review workflows (separate file watch, separate cache).
            var prWorkflowLoader = new WorkflowLoader(
                config,
                sp.GetRequiredService<ILogger<WorkflowLoader>>());

            return new GitHubTrackerOrchestrator(
                sp.GetRequiredService<IIssueTracker>(),
                issueWorkflowLoader,
                prWorkflowLoader,
                sp.GetRequiredService<IssueWorkspaceManager>(),
                sp.GetRequiredService<IssueAgentRunnerFactory>(),
                config,
                workspacePath,
                sp.GetRequiredService<ILogger<GitHubTrackerOrchestrator>>());
        });
        services.AddSingleton<IOrchestratorSnapshotProvider>(
            sp => sp.GetRequiredService<GitHubTrackerOrchestrator>());
    }

    public override IChannelService CreateChannelService(IServiceProvider sp, ModuleContext context)
        => ActivatorUtilities.CreateInstance<GitHubTrackerChannelService>(sp);

    private static IIssueTracker CreateTracker(GitHubTrackerConfig config, string workspacePath, IServiceProvider sp)
        => new GitHubTrackerAdapter(config, workspacePath, sp.GetRequiredService<ILogger<GitHubTrackerAdapter>>());
}
