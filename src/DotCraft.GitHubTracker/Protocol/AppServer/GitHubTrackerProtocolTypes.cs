using System.Text.Json.Serialization;

namespace DotCraft.GitHubTracker.Protocol.AppServer;

public static class GitHubTrackerAppServerMethods
{
    public const string Get = "githubTracker/get";
    public const string Update = "githubTracker/update";
}

public sealed class GitHubTrackerConfigWire
{
    public bool Enabled { get; set; }

    public string IssuesWorkflowPath { get; set; } = "WORKFLOW.md";

    public string PullRequestWorkflowPath { get; set; } = "PR_WORKFLOW.md";

    public GitHubTrackerTrackerConfigWire Tracker { get; set; } = new();

    public GitHubTrackerPollingConfigWire Polling { get; set; } = new();

    public GitHubTrackerWorkspaceConfigWire Workspace { get; set; } = new();

    public GitHubTrackerAgentConfigWire Agent { get; set; } = new();

    public GitHubTrackerHooksConfigWire Hooks { get; set; } = new();
}

public sealed class GitHubTrackerTrackerConfigWire
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Endpoint { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApiKey { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Repository { get; set; }

    public List<string> ActiveStates { get; set; } = ["Todo", "In Progress"];

    public List<string> TerminalStates { get; set; } = ["Done", "Closed", "Cancelled"];

    public string GitHubStateLabelPrefix { get; set; } = "status:";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AssigneeFilter { get; set; }

    public List<string> PullRequestActiveStates { get; set; } = ["Pending Review", "Review Requested", "Changes Requested"];

    public List<string> PullRequestTerminalStates { get; set; } = ["Merged", "Closed", "Approved"];
}

public sealed class GitHubTrackerPollingConfigWire
{
    public int IntervalMs { get; set; } = 30_000;
}

public sealed class GitHubTrackerWorkspaceConfigWire
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Root { get; set; }
}

public sealed class GitHubTrackerAgentConfigWire
{
    public int MaxConcurrentAgents { get; set; } = 3;

    public int MaxTurns { get; set; } = 20;

    public int MaxRetryBackoffMs { get; set; } = 300_000;

    public int TurnTimeoutMs { get; set; } = 3_600_000;

    public int StallTimeoutMs { get; set; } = 300_000;

    public Dictionary<string, int> MaxConcurrentByState { get; set; } = [];

    public int MaxConcurrentPullRequestAgents { get; set; }
}

public sealed class GitHubTrackerHooksConfigWire
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AfterCreate { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BeforeRun { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AfterRun { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BeforeRemove { get; set; }

    public int TimeoutMs { get; set; } = 60_000;
}

public sealed class GitHubTrackerGetResult
{
    public GitHubTrackerConfigWire Config { get; set; } = new();
}

public sealed class GitHubTrackerUpdateParams
{
    public GitHubTrackerConfigWire Config { get; set; } = new();
}

public sealed class GitHubTrackerUpdateResult
{
    public GitHubTrackerConfigWire Config { get; set; } = new();
}
