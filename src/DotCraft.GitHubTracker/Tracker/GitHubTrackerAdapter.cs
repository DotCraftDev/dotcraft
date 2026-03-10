using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DotCraft.Configuration;
using Microsoft.Extensions.Logging;

namespace DotCraft.GitHubTracker.Tracker;

/// <summary>
/// GitHub Issues adapter implementing IIssueTracker.
/// Maps GitHub Open/Closed state to GitHubTracker states via configurable labels.
/// </summary>
public sealed class GitHubTrackerAdapter : IIssueTracker, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repo;
    private readonly GitHubTrackerTrackerConfig _config;
    private readonly ILogger<GitHubTrackerAdapter> _logger;

    public GitHubTrackerAdapter(GitHubTrackerTrackerConfig config, ILogger<GitHubTrackerAdapter> logger)
    {
        _config = config;
        _logger = logger;

        var repoParts = (config.Repository ?? "").Split('/', 2);
        if (repoParts.Length != 2 || string.IsNullOrWhiteSpace(repoParts[0]) || string.IsNullOrWhiteSpace(repoParts[1]))
            throw new ArgumentException("GitHub repository must be in 'owner/repo' format");

        _owner = repoParts[0];
        _repo = repoParts[1];

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(config.Endpoint ?? "https://api.github.com"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DotCraft-GitHubTracker", "1.0"));

        var token = ResolveToken(config.ApiKey);
        if (!string.IsNullOrEmpty(token))
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<IReadOnlyList<TrackedIssue>> FetchCandidateIssuesAsync(CancellationToken ct = default)
    {
        var allIssues = new List<TrackedIssue>();
        var page = 1;

        while (true)
        {
            var url = $"/repos/{_owner}/{_repo}/issues?state=open&per_page=50&page={page}&sort=created&direction=asc";

            if (!string.IsNullOrEmpty(_config.AssigneeFilter))
                url += $"&assignee={Uri.EscapeDataString(_config.AssigneeFilter)}";

            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var items = await response.Content.ReadFromJsonAsync<List<GitHubIssue>>(JsonOptions, ct) ?? [];

            if (items.Count == 0) break;

            foreach (var item in items)
            {
                if (item.PullRequest != null) continue;

                var issue = NormalizeIssue(item);
                var normalizedState = issue.State.Trim().ToLowerInvariant();

                var isActive = _config.ActiveStates
                    .Any(s => string.Equals(s.Trim(), normalizedState, StringComparison.OrdinalIgnoreCase));

                if (isActive)
                    allIssues.Add(issue);
            }

            if (items.Count < 50) break;
            page++;
        }

        _logger.LogDebug("Fetched {Count} candidate issues from GitHub", allIssues.Count);
        return allIssues;
    }

    public async Task<IReadOnlyList<IssueStateSnapshot>> FetchIssueStatesByIdsAsync(
        IReadOnlyList<string> issueIds, CancellationToken ct = default)
    {
        var snapshots = new List<IssueStateSnapshot>();

        foreach (var id in issueIds)
        {
            try
            {
                var url = $"/repos/{_owner}/{_repo}/issues/{id}";
                var response = await _httpClient.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                var item = await response.Content.ReadFromJsonAsync<GitHubIssue>(JsonOptions, ct);
                if (item == null) continue;

                var state = DeriveState(item);
                snapshots.Add(new IssueStateSnapshot { Id = id, State = state });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch state for issue {IssueId}", id);
            }
        }

        return snapshots;
    }

    public async Task<IReadOnlyList<TrackedIssue>> FetchIssuesByStatesAsync(
        IReadOnlyList<string> stateNames, CancellationToken ct = default)
    {
        if (stateNames.Count == 0) return [];

        var needsClosed = stateNames.Any(s =>
            string.Equals(s.Trim(), "done", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Trim(), "closed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Trim(), "cancelled", StringComparison.OrdinalIgnoreCase));

        if (!needsClosed) return [];

        var issues = new List<TrackedIssue>();
        var page = 1;

        while (true)
        {
            var url = $"/repos/{_owner}/{_repo}/issues?state=closed&per_page=50&page={page}&sort=updated&direction=desc";
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var items = await response.Content.ReadFromJsonAsync<List<GitHubIssue>>(JsonOptions, ct) ?? [];
            if (items.Count == 0) break;

            foreach (var item in items)
            {
                if (item.PullRequest != null) continue;
                issues.Add(NormalizeIssue(item));
            }

            // Only fetch first page for terminal cleanup
            break;
        }

        return issues;
    }

    public async Task CloseIssueAsync(string issueId, string reason, CancellationToken ct = default)
    {
        // First, remove active-state labels
        var url = $"/repos/{_owner}/{_repo}/issues/{issueId}/labels";
        var labelsResponse = await _httpClient.GetAsync(url, ct);

        if (labelsResponse.IsSuccessStatusCode)
        {
            var labels = await labelsResponse.Content.ReadFromJsonAsync<List<GitHubLabel>>(JsonOptions, ct) ?? [];
            var prefix = _config.GitHubStateLabelPrefix.ToLowerInvariant();

            foreach (var label in labels)
            {
                var name = label.Name;
                if (name == null) continue;
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var encodedName = Uri.EscapeDataString(name);
                    var deleteUrl = $"/repos/{_owner}/{_repo}/issues/{issueId}/labels/{encodedName}";
                    await _httpClient.DeleteAsync(deleteUrl, ct);
                }
            }
        }

        // Close the issue via PATCH
        var patchUrl = $"/repos/{_owner}/{_repo}/issues/{issueId}";
        var body = JsonSerializer.Serialize(new { state = "closed" }, JsonOptions);
        var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PatchAsync(patchUrl, content, ct);

        if (response.IsSuccessStatusCode)
            _logger.LogInformation("Closed GitHub issue #{IssueId}: {Reason}", issueId, reason);
        else
            _logger.LogWarning("Failed to close issue #{IssueId}: {Status}", issueId, response.StatusCode);
    }

    private TrackedIssue NormalizeIssue(GitHubIssue gh)
    {
        var labels = gh.Labels?.Select(l => l.Name?.ToLowerInvariant() ?? "").Where(l => l.Length > 0).ToList()
            ?? [];

        return new TrackedIssue
        {
            Id = gh.Number.ToString(),
            Identifier = $"#{gh.Number}",
            Title = gh.Title ?? "",
            Description = gh.Body,
            Priority = DerivePriority(labels),
            State = DeriveState(gh),
            BranchName = null,
            Url = gh.HtmlUrl,
            Labels = labels,
            BlockedBy = ParseBlockedBy(gh.Body),
            CreatedAt = gh.CreatedAt,
            UpdatedAt = gh.UpdatedAt,
        };
    }

    // Matches common GitHub "blocked by #N" / "depends on #N" patterns in issue bodies.
    private static readonly Regex BlockedByPattern = new(
        @"(?:blocked\s+by|depends\s+on)\s+#(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static IReadOnlyList<BlockerRef> ParseBlockedBy(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];

        var blockers = new List<BlockerRef>();
        foreach (Match match in BlockedByPattern.Matches(body))
        {
            var number = match.Groups[1].Value;
            blockers.Add(new BlockerRef { Id = number, Identifier = $"#{number}" });
        }
        return blockers;
    }

    private string DeriveState(GitHubIssue gh)
    {
        if (string.Equals(gh.State, "closed", StringComparison.OrdinalIgnoreCase))
            return "Done";

        var labels = gh.Labels?.Select(l => l.Name?.ToLowerInvariant() ?? "").ToList() ?? [];
        var prefix = _config.GitHubStateLabelPrefix.ToLowerInvariant();

        foreach (var label in labels)
        {
            if (label.StartsWith(prefix, StringComparison.Ordinal))
            {
                var state = label[prefix.Length..].Trim();
                // Capitalize first letter for consistency
                if (state.Length > 0)
                    return char.ToUpperInvariant(state[0]) + state[1..];
            }
        }

        return "Todo";
    }

    private static int? DerivePriority(List<string> labels)
    {
        foreach (var label in labels)
        {
            if (label.StartsWith("priority:", StringComparison.OrdinalIgnoreCase))
            {
                var val = label["priority:".Length..].Trim();
                if (int.TryParse(val, out var priority))
                    return priority;
            }
        }
        return null;
    }

    private static string? ResolveToken(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;
        if (configured.StartsWith('$'))
        {
            var envName = configured[1..];
            return Environment.GetEnvironmentVariable(envName);
        }
        return configured;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public void Dispose() => _httpClient.Dispose();

    private sealed class GitHubIssue
    {
        public int Number { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? State { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("pull_request")]
        public object? PullRequest { get; set; }

        public List<GitHubLabel>? Labels { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class GitHubLabel
    {
        public string? Name { get; set; }
    }
}
