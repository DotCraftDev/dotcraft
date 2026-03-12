namespace DotCraft.Configuration;

public sealed class GitHubTrackerConfig
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Path to the WORKFLOW.md file relative to workspace root.
    /// WORKFLOW.md values override matching AppConfig.GitHubTracker settings.
    /// </summary>
    public string WorkflowPath { get; set; } = "WORKFLOW.md";

    /// <summary>
    /// Path to the PR review workflow file relative to workspace root.
    /// Used when dispatching agents for pull request reviews.
    /// </summary>
    public string PullRequestWorkflowPath { get; set; } = "PR_WORKFLOW.md";

    public GitHubTrackerTrackerConfig Tracker { get; set; } = new();

    public GitHubTrackerPollingConfig Polling { get; set; } = new();

    public GitHubTrackerWorkspaceConfig Workspace { get; set; } = new();

    public GitHubTrackerAgentConfig Agent { get; set; } = new();

    public GitHubTrackerHooksConfig Hooks { get; set; } = new();
}

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
    public string? ApiKey { get; set; }

    /// <summary>
    /// GitHub repository in "owner/repo" format.
    /// </summary>
    public string? Repository { get; set; }

    /// <summary>
    /// Issue states considered active (eligible for dispatch).
    /// GitHub default mapping uses labels (e.g. "status:todo", "status:in-progress").
    /// </summary>
    public List<string> ActiveStates { get; set; } = ["Todo", "In Progress"];

    /// <summary>
    /// Issue states considered terminal (stop running agents, clean workspaces).
    /// </summary>
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
    /// Enable tracking of pull requests for automatic PR review. Default: false.
    /// </summary>
    public bool TrackPullRequests { get; set; }

    /// <summary>
    /// PR states considered active (eligible for dispatch).
    /// Derived from the PR review status and draft state.
    /// </summary>
    public List<string> PullRequestActiveStates { get; set; } = ["Pending Review", "Review Requested", "Changes Requested"];

    /// <summary>
    /// PR states considered terminal (stop running agents, clean workspaces).
    /// </summary>
    public List<string> PullRequestTerminalStates { get; set; } = ["Merged", "Closed", "Approved"];

    /// <summary>
    /// Optional: only track PRs carrying this label (e.g. "auto-review").
    /// When null or empty, all non-draft PRs are eligible.
    /// </summary>
    public string? PullRequestLabelFilter { get; set; }
}

public sealed class GitHubTrackerPollingConfig
{
    /// <summary>
    /// Interval in milliseconds between poll ticks (default: 30s).
    /// </summary>
    public int IntervalMs { get; set; } = 30_000;
}

public sealed class GitHubTrackerWorkspaceConfig
{
    /// <summary>
    /// Root directory for per-issue workspaces.
    /// Supports ~ for home directory and $VAR for environment variables.
    /// Default: {temp}/github_tracker_workspaces
    /// </summary>
    public string? Root { get; set; }
}

public sealed class GitHubTrackerAgentConfig
{
    /// <summary>
    /// Global maximum number of concurrent issue agents.
    /// </summary>
    public int MaxConcurrentAgents { get; set; } = 3;

    /// <summary>
    /// Maximum agent turns per run attempt.
    /// </summary>
    public int MaxTurns { get; set; } = 20;

    /// <summary>
    /// Maximum backoff delay for failure-driven retries in milliseconds (default: 5 min).
    /// </summary>
    public int MaxRetryBackoffMs { get; set; } = 300_000;

    /// <summary>
    /// Maximum time allowed for a single agent turn in milliseconds (default: 1 hour).
    /// </summary>
    public int TurnTimeoutMs { get; set; } = 3_600_000;

    /// <summary>
    /// Stall detection timeout in milliseconds (default: 5 min).
    /// If no activity is observed within this period, the run is terminated.
    /// Set to 0 or negative to disable stall detection.
    /// </summary>
    public int StallTimeoutMs { get; set; } = 300_000;

    /// <summary>
    /// Per-state concurrency overrides. Key is state name (case-insensitive), value is max concurrent.
    /// </summary>
    public Dictionary<string, int> MaxConcurrentByState { get; set; } = [];

    /// <summary>
    /// Maximum concurrent agents dedicated to PR reviews.
    /// 0 means no dedicated limit (shares the global <see cref="MaxConcurrentAgents"/> pool).
    /// </summary>
    public int MaxConcurrentPullRequestAgents { get; set; }
}

public sealed class GitHubTrackerHooksConfig
{
    public string? AfterCreate { get; set; }
    public string? BeforeRun { get; set; }
    public string? AfterRun { get; set; }
    public string? BeforeRemove { get; set; }

    /// <summary>
    /// Hook execution timeout in milliseconds (default: 60s).
    /// </summary>
    public int TimeoutMs { get; set; } = 60_000;
}
