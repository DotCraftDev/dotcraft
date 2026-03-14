using DotCraft.Configuration;

namespace DotCraft.GitHubTracker;

[ConfigSection("GitHubTracker", DisplayName = "GitHubTracker", Order = 300)]
public sealed class GitHubTrackerConfig
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Path to the issue workflow file relative to the workspace root.
    /// Workflow values override matching GitHubTrackerConfig settings.
    /// </summary>
    [ConfigField(Hint = "Relative to the workspace root. Leave blank to disable issue dispatch.")]
    public string IssuesWorkflowPath { get; set; } = "WORKFLOW.md";

    /// <summary>
    /// Path to the PR review workflow file relative to workspace root.
    /// Used when dispatching agents for pull request reviews.
    /// </summary>
    [ConfigField(Hint = "Relative to the workspace root. Leave blank to disable PR review dispatch.")]
    public string PullRequestWorkflowPath { get; set; } = "PR_WORKFLOW.md";

    [ConfigField(Ignore = true)]
    public GitHubTrackerTrackerConfig Tracker { get; set; } = new();

    [ConfigField(Ignore = true)]
    public GitHubTrackerPollingConfig Polling { get; set; } = new();

    [ConfigField(Ignore = true)]
    public GitHubTrackerWorkspaceConfig Workspace { get; set; } = new();

    [ConfigField(Ignore = true)]
    public GitHubTrackerAgentConfig Agent { get; set; } = new();

    [ConfigField(Ignore = true)]
    public GitHubTrackerHooksConfig Hooks { get; set; } = new();
}

[ConfigSection("GitHubTracker.Tracker", DisplayName = "GitHubTracker > Tracker", Order = 310)]
public sealed class GitHubTrackerTrackerConfig
{
    /// <summary>
    /// API endpoint override. Defaults to https://api.github.com.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// API key or token. Supports $ENV_VAR syntax for environment variable indirection.
    /// Example: $GITHUB_TOKEN
    /// </summary>
    [ConfigField(Sensitive = true, Hint = "Supports $ENV_VAR indirection")]
    public string? ApiKey { get; set; }

    /// <summary>
    /// GitHub repository in "owner/repo" format.
    /// </summary>
    [ConfigField(Hint = "owner/repo")]
    public string? Repository { get; set; }

    /// <summary>
    /// Issue states considered active (eligible for dispatch).
    /// GitHub default mapping uses labels (e.g. "status:todo", "status:in-progress").
    /// </summary>
    [ConfigField(Hint = "JSON array of active state names")]
    public List<string> ActiveStates { get; set; } = ["Todo", "In Progress"];

    /// <summary>
    /// Issue states considered terminal (stop running agents, clean workspaces).
    /// </summary>
    [ConfigField(Hint = "JSON array of terminal state names")]
    public List<string> TerminalStates { get; set; } = ["Done", "Closed", "Cancelled"];

    /// <summary>
    /// GitHub label prefix used to derive GitHubTracker state from GitHub labels.
    /// For example, prefix "status:" maps label "status:todo" to state "todo".
    /// </summary>
    public string GitHubStateLabelPrefix { get; set; } = "status:";

    /// <summary>
    /// Optional: only process issues assigned to this user.
    /// </summary>
    public string? AssigneeFilter { get; set; }

    /// <summary>
    /// PR states considered active (eligible for dispatch).
    /// </summary>
    [ConfigField(Hint = "JSON array of PR states considered active (e.g. \"Pending Review\", \"Review Requested\", \"Changes Requested\")")]
    public List<string> PullRequestActiveStates { get; set; } = ["Pending Review", "Review Requested", "Changes Requested"];

    /// <summary>
    /// PR states considered terminal (stop running agents, clean workspaces).
    /// </summary>
    [ConfigField(Hint = "JSON array of PR states considered terminal (e.g. \"Merged\", \"Closed\", \"Approved\")")]
    public List<string> PullRequestTerminalStates { get; set; } = ["Merged", "Closed", "Approved"];

    /// <summary>
    /// Optional: only track PRs carrying this label (e.g. "auto-review").
    /// When null or empty, all non-draft PRs are eligible.
    /// </summary>
    [ConfigField(Hint = "Optional: only track PRs with this label (e.g. auto-review). Empty = all non-draft PRs. Orchestrator removes this label after each review when set.")]
    public string? PullRequestLabelFilter { get; set; }
}

[ConfigSection("GitHubTracker.Polling", DisplayName = "GitHubTracker > Polling", Order = 320)]
public sealed class GitHubTrackerPollingConfig
{
    /// <summary>
    /// Interval in milliseconds between poll ticks (default: 30s).
    /// </summary>
    [ConfigField(Min = 1)]
    public int IntervalMs { get; set; } = 30_000;
}

[ConfigSection("GitHubTracker.Workspace", DisplayName = "GitHubTracker > Workspace", Order = 330)]
public sealed class GitHubTrackerWorkspaceConfig
{
    /// <summary>
    /// Root directory for per-issue workspaces.
    /// Supports ~ for home directory and $VAR for environment variables.
    /// Default: {temp}/github_tracker_workspaces
    /// </summary>
    [ConfigField(Hint = "Leave blank to use the default github_tracker_workspaces temp directory")]
    public string? Root { get; set; }
}

[ConfigSection("GitHubTracker.Agent", DisplayName = "GitHubTracker > Agent", Order = 340)]
public sealed class GitHubTrackerAgentConfig
{
    /// <summary>
    /// Global maximum number of concurrent issue agents.
    /// </summary>
    [ConfigField(Min = 1)]
    public int MaxConcurrentAgents { get; set; } = 3;

    /// <summary>
    /// Maximum agent turns per run attempt.
    /// </summary>
    [ConfigField(Min = 1)]
    public int MaxTurns { get; set; } = 20;

    /// <summary>
    /// Maximum backoff delay for failure-driven retries in milliseconds (default: 5 min).
    /// </summary>
    [ConfigField(Min = 0)]
    public int MaxRetryBackoffMs { get; set; } = 300_000;

    /// <summary>
    /// Maximum time allowed for a single agent turn in milliseconds (default: 1 hour).
    /// </summary>
    [ConfigField(Min = 0)]
    public int TurnTimeoutMs { get; set; } = 3_600_000;

    /// <summary>
    /// Stall detection timeout in milliseconds (default: 5 min).
    /// If no activity is observed within this period, the run is terminated.
    /// Set to 0 or negative to disable stall detection.
    /// </summary>
    [ConfigField(Hint = "0 or negative disables stall detection")]
    public int StallTimeoutMs { get; set; } = 300_000;

    /// <summary>
    /// Per-state concurrency overrides. Key is state name (case-insensitive), value is max concurrent.
    /// </summary>
    [ConfigField(Hint = "JSON object of state->maxConcurrency")]
    public Dictionary<string, int> MaxConcurrentByState { get; set; } = [];

    /// <summary>
    /// Maximum concurrent agents dedicated to PR reviews.
    /// 0 means no dedicated limit (shares the global <see cref="MaxConcurrentAgents"/> pool).
    /// </summary>
    [ConfigField(Min = 0, Hint = "0 = PR agents share global MaxConcurrentAgents pool; positive = dedicated concurrency limit for PR review agents")]
    public int MaxConcurrentPullRequestAgents { get; set; }
}

[ConfigSection("GitHubTracker.Hooks", DisplayName = "GitHubTracker > Hooks", Order = 350)]
public sealed class GitHubTrackerHooksConfig
{
    public string? AfterCreate { get; set; }
    public string? BeforeRun { get; set; }
    public string? AfterRun { get; set; }
    public string? BeforeRemove { get; set; }

    /// <summary>
    /// Hook execution timeout in milliseconds (default: 60s).
    /// </summary>
    [ConfigField(Min = 0)]
    public int TimeoutMs { get; set; } = 60_000;
}
