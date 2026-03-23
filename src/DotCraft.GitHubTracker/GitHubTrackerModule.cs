using DotCraft.Abstractions;
using DotCraft.Automations.Abstractions;
using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.GitHubTracker.GitHub;
using DotCraft.GitHubTracker.Tracker;
using DotCraft.GitHubTracker.Workflow;
using DotCraft.GitHubTracker.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotCraft.GitHubTracker;

/// <summary>
/// GitHub issue/PR automation module. Registers <see cref="GitHubAutomationSource"/>
/// as an <see cref="IAutomationSource"/> discovered by the Automations orchestrator.
/// </summary>
[DotCraftModule("github-tracker", Priority = 50, Description = "Autonomous GitHub issue tracker orchestrator")]
public sealed partial class GitHubTrackerModule : ModuleBase
{
    public override bool IsEnabled(AppConfig config) => config.GetSection<GitHubTrackerConfig>("GitHubTracker").Enabled;

    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
    {
        var errors = new List<string>();
        var tracker = config.GetSection<GitHubTrackerConfig>("GitHubTracker");

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
        var config = context.Config.GetSection<GitHubTrackerConfig>("GitHubTracker");
        var workspacePath = context.Paths.WorkspacePath;

        services.AddSingleton(config);
        services.AddSingleton(sp => new WorkflowLoader(
            config,
            sp.GetRequiredService<ILogger<WorkflowLoader>>()));
        services.AddSingleton(sp => new WorkItemWorkspaceManager(
            config,
            sp.GetRequiredService<ILogger<WorkItemWorkspaceManager>>()));
        services.AddSingleton<IWorkItemTracker>(sp => CreateTracker(config, workspacePath, sp));

        services.AddSingleton<IAutomationSource>(sp =>
        {
            var issueWorkflowLoader = sp.GetRequiredService<WorkflowLoader>();
            var prWorkflowLoader = new WorkflowLoader(
                config,
                sp.GetRequiredService<ILogger<WorkflowLoader>>());

            return new GitHubAutomationSource(
                sp.GetRequiredService<IWorkItemTracker>(),
                issueWorkflowLoader,
                prWorkflowLoader,
                config,
                workspacePath,
                sp.GetRequiredService<ILoggerFactory>());
        });
    }

    private static GitHubTrackerAdapter CreateTracker(GitHubTrackerConfig config, string workspacePath, IServiceProvider sp)
        => new(config, workspacePath, sp.GetRequiredService<ILogger<GitHubTrackerAdapter>>());
}
