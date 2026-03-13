using System.Text.RegularExpressions;
using DotCraft.Configuration;
using Fluid;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DotCraft.GitHubTracker.Workflow;

/// <summary>
/// Loads and watches WORKFLOW.md, parses YAML front matter + Liquid prompt template.
/// Supports hot reload per SPEC.md Section 6.2.
/// </summary>
public sealed partial class WorkflowLoader(GitHubTrackerConfig baseConfig, ILogger<WorkflowLoader> logger)
    : IDisposable
{
    private static readonly Regex FrontMatterRegex = GetFrontMatterRegex();

    private readonly FluidParser _fluidParser = new();
    private readonly Lock _lock = new();

    private FileSystemWatcher? _watcher;
    private WorkflowDefinition? _cached;
    private string? _watchedPath;

    /// <summary>
    /// Load workflow from the given file path. Caches the result and starts watching for changes.
    /// </summary>
    public WorkflowDefinition Load(string path)
    {
        var fullPath = Path.GetFullPath(path);

        lock (_lock)
        {
            if (_cached != null && _watchedPath == fullPath)
                return _cached;
        }

        var definition = ParseFile(fullPath);

        lock (_lock)
        {
            _cached = definition;

            if (_watchedPath != fullPath)
            {
                _watcher?.Dispose();
                _watchedPath = fullPath;
                StartWatching(fullPath);
            }
        }

        return definition;
    }

    /// <summary>
    /// Get the currently cached workflow definition, or null if not yet loaded.
    /// </summary>
    public WorkflowDefinition? Current
    {
        get { lock (_lock) { return _cached; } }
    }

    /// <summary>
    /// Force reload from disk.
    /// </summary>
    public WorkflowDefinition? Reload()
    {
        string? path;
        lock (_lock) { path = _watchedPath; }

        if (path == null) return null;

        try
        {
            var definition = ParseFile(path);
            lock (_lock) { _cached = definition; }
            logger.LogInformation("WORKFLOW.md reloaded successfully");
            return definition;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reload WORKFLOW.md, keeping last known good config");
            return Current;
        }
    }

    /// <summary>
    /// Render a Liquid prompt template with work_item and attempt variables.
    /// Uses strict variable checking per SPEC.md Section 5.4.
    /// </summary>
    public string RenderPrompt(string template, object workItemData, int? attempt)
    {
        if (string.IsNullOrWhiteSpace(template))
            return "You are working on an issue from the tracker.";

        if (!_fluidParser.TryParse(template, out var fluidTemplate, out var error))
            throw new InvalidOperationException($"Template parse error: {error}");

        var options = new TemplateOptions();
        options.MemberAccessStrategy.Register<Dictionary<string, object?>>();

        var context = new TemplateContext(options);
        context.SetValue("work_item", workItemData);
        if (attempt.HasValue)
            context.SetValue("attempt", attempt.Value);

        return fluidTemplate.Render(context);
    }

    private WorkflowDefinition ParseFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Workflow file not found: {path}");

        var content = File.ReadAllText(path);
        return Parse(content);
    }

    internal WorkflowDefinition Parse(string content)
    {
        var match = FrontMatterRegex.Match(content);

        if (match.Success)
        {
            var yamlContent = match.Groups[1].Value;
            var promptBody = match.Groups[2].Value.Trim();
            var config = ParseYamlConfig(yamlContent);
            return new WorkflowDefinition(config, promptBody);
        }

        // No front matter: entire content is the prompt body
        return new WorkflowDefinition(CloneBaseConfig(), content.Trim());
    }

    private GitHubTrackerConfig ParseYamlConfig(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var raw = deserializer.Deserialize<Dictionary<string, object?>>(yaml);

        var config = CloneBaseConfig();
        ApplyYamlOverrides(config, raw);
        return config;
    }

    private GitHubTrackerConfig CloneBaseConfig()
    {
        return new GitHubTrackerConfig
        {
            Enabled = baseConfig.Enabled,
            IssuesWorkflowPath = baseConfig.IssuesWorkflowPath,
            PullRequestWorkflowPath = baseConfig.PullRequestWorkflowPath,
            Tracker = new GitHubTrackerTrackerConfig
            {
                Endpoint = baseConfig.Tracker.Endpoint,
                ApiKey = baseConfig.Tracker.ApiKey,
                Repository = baseConfig.Tracker.Repository,
                ActiveStates = [.. baseConfig.Tracker.ActiveStates],
                TerminalStates = [.. baseConfig.Tracker.TerminalStates],
                GitHubStateLabelPrefix = baseConfig.Tracker.GitHubStateLabelPrefix,
                AssigneeFilter = baseConfig.Tracker.AssigneeFilter,
                PullRequestActiveStates = [.. baseConfig.Tracker.PullRequestActiveStates],
                PullRequestTerminalStates = [.. baseConfig.Tracker.PullRequestTerminalStates],
                PullRequestLabelFilter = baseConfig.Tracker.PullRequestLabelFilter,
            },
            Polling = new GitHubTrackerPollingConfig
            {
                IntervalMs = baseConfig.Polling.IntervalMs,
            },
            Workspace = new GitHubTrackerWorkspaceConfig
            {
                Root = baseConfig.Workspace.Root,
            },
            Agent = new GitHubTrackerAgentConfig
            {
                MaxConcurrentAgents = baseConfig.Agent.MaxConcurrentAgents,
                MaxTurns = baseConfig.Agent.MaxTurns,
                MaxRetryBackoffMs = baseConfig.Agent.MaxRetryBackoffMs,
                TurnTimeoutMs = baseConfig.Agent.TurnTimeoutMs,
                StallTimeoutMs = baseConfig.Agent.StallTimeoutMs,
                MaxConcurrentByState = new Dictionary<string, int>(baseConfig.Agent.MaxConcurrentByState),
                MaxConcurrentPullRequestAgents = baseConfig.Agent.MaxConcurrentPullRequestAgents,
            },
            Hooks = new GitHubTrackerHooksConfig
            {
                AfterCreate = baseConfig.Hooks.AfterCreate,
                BeforeRun = baseConfig.Hooks.BeforeRun,
                AfterRun = baseConfig.Hooks.AfterRun,
                BeforeRemove = baseConfig.Hooks.BeforeRemove,
                TimeoutMs = baseConfig.Hooks.TimeoutMs,
            },
        };
    }

    private static void ApplyYamlOverrides(GitHubTrackerConfig config, Dictionary<string, object?> raw)
    {
        if (raw.TryGetValue("tracker", out var trackerObj) && trackerObj is Dictionary<object, object> tracker)
        {
            if (tracker.TryGetValue("endpoint", out var ep)) config.Tracker.Endpoint = ep?.ToString();
            if (tracker.TryGetValue("api_key", out var key)) config.Tracker.ApiKey = key?.ToString();
            if (tracker.TryGetValue("repository", out var repo)) config.Tracker.Repository = repo?.ToString();
            if (tracker.TryGetValue("active_states", out var active)) config.Tracker.ActiveStates = ParseStringList(active);
            if (tracker.TryGetValue("terminal_states", out var terminal)) config.Tracker.TerminalStates = ParseStringList(terminal);
            if (tracker.TryGetValue("pull_request_active_states", out var prActive))
                config.Tracker.PullRequestActiveStates = ParseStringList(prActive);
            if (tracker.TryGetValue("pull_request_terminal_states", out var prTerminal))
                config.Tracker.PullRequestTerminalStates = ParseStringList(prTerminal);
            if (tracker.TryGetValue("pull_request_label_filter", out var prLabel))
                config.Tracker.PullRequestLabelFilter = prLabel?.ToString();
        }

        if (raw.TryGetValue("polling", out var pollingObj) && pollingObj is Dictionary<object, object> polling)
        {
            if (polling.TryGetValue("interval_ms", out var interval) && int.TryParse(interval?.ToString(), out var ms))
                config.Polling.IntervalMs = ms;
        }

        if (raw.TryGetValue("workspace", out var wsObj) && wsObj is Dictionary<object, object> ws)
        {
            if (ws.TryGetValue("root", out var root)) config.Workspace.Root = root?.ToString();
        }

        if (raw.TryGetValue("agent", out var agentObj) && agentObj is Dictionary<object, object> agent)
        {
            if (agent.TryGetValue("max_concurrent_agents", out var mca) && int.TryParse(mca?.ToString(), out var mcaVal))
                config.Agent.MaxConcurrentAgents = mcaVal;
            if (agent.TryGetValue("max_turns", out var mt) && int.TryParse(mt?.ToString(), out var mtVal))
                config.Agent.MaxTurns = mtVal;
            if (agent.TryGetValue("max_retry_backoff_ms", out var mrb) && int.TryParse(mrb?.ToString(), out var mrbVal))
                config.Agent.MaxRetryBackoffMs = mrbVal;
            if (agent.TryGetValue("max_concurrent_pull_request_agents", out var mcpra) && int.TryParse(mcpra?.ToString(), out var mcpraVal))
                config.Agent.MaxConcurrentPullRequestAgents = mcpraVal;
        }

        if (raw.TryGetValue("hooks", out var hooksObj) && hooksObj is Dictionary<object, object> hooks)
        {
            if (hooks.TryGetValue("after_create", out var ac)) config.Hooks.AfterCreate = ac?.ToString();
            if (hooks.TryGetValue("before_run", out var br)) config.Hooks.BeforeRun = br?.ToString();
            if (hooks.TryGetValue("after_run", out var ar)) config.Hooks.AfterRun = ar?.ToString();
            if (hooks.TryGetValue("before_remove", out var brm)) config.Hooks.BeforeRemove = brm?.ToString();
            if (hooks.TryGetValue("timeout_ms", out var hto) && int.TryParse(hto?.ToString(), out var htoVal))
                config.Hooks.TimeoutMs = htoVal;
        }
    }

    private static List<string> ParseStringList(object? value)
    {
        if (value is List<object> list)
            return list.Select(x => x.ToString() ?? "").Where(x => x.Length > 0).ToList();
        if (value is string str)
            return str.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        return [];
    }

    private void StartWatching(string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath);
        var file = Path.GetFileName(fullPath);

        if (dir == null) return;

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += (_, _) =>
        {
            Task.Delay(100).ContinueWith(_ => Reload());
        };
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    [GeneratedRegex(@"^---\s*\r?\n(.*?)---\s*\r?\n(.*)$", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex GetFrontMatterRegex();
}
