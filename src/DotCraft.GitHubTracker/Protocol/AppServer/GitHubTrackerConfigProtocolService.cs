using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.GitHubTracker.Protocol.AppServer;

public sealed class GitHubTrackerConfigProtocolService
{
    private const string MaskedSecretValue = "***";

    public GitHubTrackerConfigWire LoadWorkspaceConfig(string workspaceCraftPath)
    {
        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        var root = LoadWorkspaceConfigObject(configPath);
        var key = FindCaseInsensitiveKey(root, "GitHubTracker");
        if (key == null || root[key] is not JsonObject section)
            return new GitHubTrackerConfigWire();

        try
        {
            return NormalizeConfig(
                section.Deserialize<GitHubTrackerConfigWire>(AppConfig.SerializerOptions) ?? new GitHubTrackerConfigWire());
        }
        catch
        {
            return new GitHubTrackerConfigWire();
        }
    }

    public void SaveWorkspaceConfig(string workspaceCraftPath, GitHubTrackerConfigWire config)
    {
        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        Directory.CreateDirectory(workspaceCraftPath);
        var root = LoadWorkspaceConfigObject(configPath);
        var key = FindCaseInsensitiveKey(root, "GitHubTracker") ?? "GitHubTracker";
        root[key] = BuildGitHubTrackerNode(config);

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
    }

    public GitHubTrackerConfigWire NormalizeConfig(GitHubTrackerConfigWire? config)
    {
        var normalized = config ?? new GitHubTrackerConfigWire();
        normalized.IssuesWorkflowPath = normalized.IssuesWorkflowPath?.Trim() ?? string.Empty;
        normalized.PullRequestWorkflowPath = normalized.PullRequestWorkflowPath?.Trim() ?? string.Empty;
        normalized.Tracker ??= new GitHubTrackerTrackerConfigWire();
        normalized.Polling ??= new GitHubTrackerPollingConfigWire();
        normalized.Workspace ??= new GitHubTrackerWorkspaceConfigWire();
        normalized.Agent ??= new GitHubTrackerAgentConfigWire();
        normalized.Hooks ??= new GitHubTrackerHooksConfigWire();

        normalized.Tracker.Endpoint = NormalizeOptionalString(normalized.Tracker.Endpoint);
        normalized.Tracker.ApiKey = NormalizeOptionalString(normalized.Tracker.ApiKey);
        normalized.Tracker.Repository = NormalizeOptionalString(normalized.Tracker.Repository);
        normalized.Tracker.GitHubStateLabelPrefix = string.IsNullOrWhiteSpace(normalized.Tracker.GitHubStateLabelPrefix)
            ? "status:"
            : normalized.Tracker.GitHubStateLabelPrefix.Trim();
        normalized.Tracker.AssigneeFilter = NormalizeOptionalString(normalized.Tracker.AssigneeFilter);
        normalized.Tracker.ActiveStates = NormalizeStringList(normalized.Tracker.ActiveStates, ["Todo", "In Progress"]);
        normalized.Tracker.TerminalStates = NormalizeStringList(normalized.Tracker.TerminalStates, ["Done", "Closed", "Cancelled"]);
        normalized.Tracker.PullRequestActiveStates = NormalizeStringList(
            normalized.Tracker.PullRequestActiveStates,
            ["Pending Review", "Review Requested", "Changes Requested"]);
        normalized.Tracker.PullRequestTerminalStates = NormalizeStringList(
            normalized.Tracker.PullRequestTerminalStates,
            ["Merged", "Closed", "Approved"]);
        normalized.Workspace.Root = NormalizeOptionalString(normalized.Workspace.Root);
        normalized.Agent.MaxConcurrentByState = NormalizeConcurrencyMap(normalized.Agent.MaxConcurrentByState);
        normalized.Hooks.AfterCreate = NormalizeOptionalString(normalized.Hooks.AfterCreate);
        normalized.Hooks.BeforeRun = NormalizeOptionalString(normalized.Hooks.BeforeRun);
        normalized.Hooks.AfterRun = NormalizeOptionalString(normalized.Hooks.AfterRun);
        normalized.Hooks.BeforeRemove = NormalizeOptionalString(normalized.Hooks.BeforeRemove);

        return normalized;
    }

    public GitHubTrackerConfigWire MaskConfig(GitHubTrackerConfigWire config)
    {
        var masked = NormalizeConfig(new GitHubTrackerConfigWire
        {
            Enabled = config.Enabled,
            IssuesWorkflowPath = config.IssuesWorkflowPath,
            PullRequestWorkflowPath = config.PullRequestWorkflowPath,
            Tracker = new GitHubTrackerTrackerConfigWire
            {
                Endpoint = config.Tracker.Endpoint,
                ApiKey = config.Tracker.ApiKey,
                Repository = config.Tracker.Repository,
                ActiveStates = [.. config.Tracker.ActiveStates],
                TerminalStates = [.. config.Tracker.TerminalStates],
                GitHubStateLabelPrefix = config.Tracker.GitHubStateLabelPrefix,
                AssigneeFilter = config.Tracker.AssigneeFilter,
                PullRequestActiveStates = [.. config.Tracker.PullRequestActiveStates],
                PullRequestTerminalStates = [.. config.Tracker.PullRequestTerminalStates]
            },
            Polling = new GitHubTrackerPollingConfigWire
            {
                IntervalMs = config.Polling.IntervalMs
            },
            Workspace = new GitHubTrackerWorkspaceConfigWire
            {
                Root = config.Workspace.Root
            },
            Agent = new GitHubTrackerAgentConfigWire
            {
                MaxConcurrentAgents = config.Agent.MaxConcurrentAgents,
                MaxTurns = config.Agent.MaxTurns,
                MaxRetryBackoffMs = config.Agent.MaxRetryBackoffMs,
                TurnTimeoutMs = config.Agent.TurnTimeoutMs,
                StallTimeoutMs = config.Agent.StallTimeoutMs,
                MaxConcurrentByState = new Dictionary<string, int>(config.Agent.MaxConcurrentByState, StringComparer.OrdinalIgnoreCase),
                MaxConcurrentPullRequestAgents = config.Agent.MaxConcurrentPullRequestAgents
            },
            Hooks = new GitHubTrackerHooksConfigWire
            {
                AfterCreate = config.Hooks.AfterCreate,
                BeforeRun = config.Hooks.BeforeRun,
                AfterRun = config.Hooks.AfterRun,
                BeforeRemove = config.Hooks.BeforeRemove,
                TimeoutMs = config.Hooks.TimeoutMs
            }
        });

        if (!string.IsNullOrWhiteSpace(masked.Tracker.ApiKey))
            masked.Tracker.ApiKey = MaskedSecretValue;

        return masked;
    }

    public GitHubTrackerConfigWire PreserveMaskedApiKey(GitHubTrackerConfigWire incoming, GitHubTrackerConfigWire existing)
    {
        if (string.Equals(incoming.Tracker.ApiKey, MaskedSecretValue, StringComparison.Ordinal))
            incoming.Tracker.ApiKey = existing.Tracker.ApiKey;

        return incoming;
    }

    public void ValidateConfig(GitHubTrackerConfigWire config)
    {
        if (!config.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(config.Tracker.Repository))
            throw AppServerErrors.GitHubTrackerConfigValidationFailed("'config.tracker.repository' is required when GitHub tracker is enabled.");

        var issueOverlap = config.Tracker.ActiveStates
            .Intersect(config.Tracker.TerminalStates, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (issueOverlap.Count > 0)
        {
            throw AppServerErrors.GitHubTrackerConfigValidationFailed(
                $"ActiveStates and TerminalStates must not overlap. Conflicting states: {string.Join(", ", issueOverlap)}");
        }

        var prOverlap = config.Tracker.PullRequestActiveStates
            .Intersect(config.Tracker.PullRequestTerminalStates, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (prOverlap.Count > 0)
        {
            throw AppServerErrors.GitHubTrackerConfigValidationFailed(
                $"PullRequestActiveStates and PullRequestTerminalStates must not overlap. Conflicting states: {string.Join(", ", prOverlap)}");
        }
    }

    public static T GetParams<T>(AppServerIncomingMessage msg) where T : new()
    {
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind == JsonValueKind.Null)
            return new T();

        try
        {
            return JsonSerializer.Deserialize<T>(
                msg.Params.Value.GetRawText(),
                SessionWireJsonOptions.Default) ?? new T();
        }
        catch (JsonException ex)
        {
            throw AppServerErrors.InvalidParams($"Failed to deserialize params: {ex.Message}");
        }
    }

    private static JsonObject BuildGitHubTrackerNode(GitHubTrackerConfigWire config)
    {
        var tracker = new JsonObject
        {
            ["activeStates"] = JsonSerializer.SerializeToNode(config.Tracker.ActiveStates),
            ["terminalStates"] = JsonSerializer.SerializeToNode(config.Tracker.TerminalStates),
            ["gitHubStateLabelPrefix"] = config.Tracker.GitHubStateLabelPrefix,
            ["pullRequestActiveStates"] = JsonSerializer.SerializeToNode(config.Tracker.PullRequestActiveStates),
            ["pullRequestTerminalStates"] = JsonSerializer.SerializeToNode(config.Tracker.PullRequestTerminalStates)
        };
        if (!string.IsNullOrWhiteSpace(config.Tracker.Endpoint))
            tracker["endpoint"] = config.Tracker.Endpoint;
        if (!string.IsNullOrWhiteSpace(config.Tracker.ApiKey))
            tracker["apiKey"] = config.Tracker.ApiKey;
        if (!string.IsNullOrWhiteSpace(config.Tracker.Repository))
            tracker["repository"] = config.Tracker.Repository;
        if (!string.IsNullOrWhiteSpace(config.Tracker.AssigneeFilter))
            tracker["assigneeFilter"] = config.Tracker.AssigneeFilter;

        var workspace = new JsonObject();
        if (!string.IsNullOrWhiteSpace(config.Workspace.Root))
            workspace["root"] = config.Workspace.Root;

        var agent = new JsonObject
        {
            ["maxConcurrentAgents"] = config.Agent.MaxConcurrentAgents,
            ["maxTurns"] = config.Agent.MaxTurns,
            ["maxRetryBackoffMs"] = config.Agent.MaxRetryBackoffMs,
            ["turnTimeoutMs"] = config.Agent.TurnTimeoutMs,
            ["stallTimeoutMs"] = config.Agent.StallTimeoutMs,
            ["maxConcurrentByState"] = JsonSerializer.SerializeToNode(
                config.Agent.MaxConcurrentByState
                    .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)),
            ["maxConcurrentPullRequestAgents"] = config.Agent.MaxConcurrentPullRequestAgents
        };

        var hooks = new JsonObject
        {
            ["timeoutMs"] = config.Hooks.TimeoutMs
        };
        if (!string.IsNullOrWhiteSpace(config.Hooks.AfterCreate))
            hooks["afterCreate"] = config.Hooks.AfterCreate;
        if (!string.IsNullOrWhiteSpace(config.Hooks.BeforeRun))
            hooks["beforeRun"] = config.Hooks.BeforeRun;
        if (!string.IsNullOrWhiteSpace(config.Hooks.AfterRun))
            hooks["afterRun"] = config.Hooks.AfterRun;
        if (!string.IsNullOrWhiteSpace(config.Hooks.BeforeRemove))
            hooks["beforeRemove"] = config.Hooks.BeforeRemove;

        return new JsonObject
        {
            ["enabled"] = config.Enabled,
            ["issuesWorkflowPath"] = config.IssuesWorkflowPath,
            ["pullRequestWorkflowPath"] = config.PullRequestWorkflowPath,
            ["tracker"] = tracker,
            ["polling"] = new JsonObject
            {
                ["intervalMs"] = config.Polling.IntervalMs
            },
            ["workspace"] = workspace,
            ["agent"] = agent,
            ["hooks"] = hooks
        };
    }

    private static List<string> NormalizeStringList(List<string>? values, IReadOnlyList<string> defaults)
    {
        if (values == null)
            return [.. defaults];

        var normalized = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count > 0 ? normalized : [.. defaults];
    }

    private static Dictionary<string, int> NormalizeConcurrencyMap(Dictionary<string, int>? values)
    {
        if (values == null || values.Count == 0)
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            var trimmed = NormalizeOptionalString(key);
            if (trimmed == null)
                continue;
            normalized[trimmed] = value;
        }

        return normalized;
    }

    private static string? NormalizeOptionalString(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static JsonObject LoadWorkspaceConfigObject(string configPath)
    {
        if (!File.Exists(configPath))
            return new JsonObject();

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(configPath));
            return node as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static string? FindCaseInsensitiveKey(JsonObject obj, string expectedKey)
    {
        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, expectedKey, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        }

        return null;
    }
}
