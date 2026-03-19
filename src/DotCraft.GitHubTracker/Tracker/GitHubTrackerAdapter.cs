using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DotCraft.GitHubTracker.Tracker;

/// <summary>
/// GitHub adapter implementing <see cref="IWorkItemTracker"/> for both Issues and Pull Requests.
/// Maps GitHub open/closed state to GitHubTracker states via configurable labels (issues)
/// or review status (PRs).
/// <para>
/// Implements Symphony SPEC section 11.1 (tracker adapter operations).
/// PR candidate selection follows PR Lifecycle Spec section 3: all open, non-draft PRs in
/// active review states are candidates — no label gate.
/// </para>
/// </summary>
public sealed class GitHubTrackerAdapter : IWorkItemTracker, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repo;
    private readonly GitHubTrackerTrackerConfig _config;
    private readonly string? _issuesWorkflowPath;
    private readonly string? _pullRequestWorkflowPath;
    private readonly ILogger<GitHubTrackerAdapter> _logger;

    public GitHubTrackerAdapter(GitHubTrackerConfig config, string workspacePath, ILogger<GitHubTrackerAdapter> logger)
    {
        _config = config.Tracker;
        _issuesWorkflowPath = ResolveWorkflowPath(config.IssuesWorkflowPath, workspacePath);
        _pullRequestWorkflowPath = ResolveWorkflowPath(config.PullRequestWorkflowPath, workspacePath);
        _logger = logger;

        var repoParts = (_config.Repository ?? "").Split('/', 2);
        if (repoParts.Length != 2 || string.IsNullOrWhiteSpace(repoParts[0]) || string.IsNullOrWhiteSpace(repoParts[1]))
            throw new ArgumentException("GitHub repository must be in 'owner/repo' format");

        _owner = repoParts[0];
        _repo = repoParts[1];

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_config.Endpoint ?? "https://api.github.com"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DotCraft-GitHubTracker", "1.0"));

        var apiKey = ResolveConfigToken(_config.ApiKey);
        if (apiKey != null)
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Internal constructor for unit tests. Accepts a custom <see cref="HttpMessageHandler"/>
    /// so tests can inject preconfigured responses without hitting the real GitHub API.
    /// PR tracking is always considered enabled in this mode.
    /// </summary>
    internal GitHubTrackerAdapter(GitHubTrackerConfig config, HttpMessageHandler handler, ILogger<GitHubTrackerAdapter> logger)
    {
        _config = config.Tracker;
        _logger = logger;

        var repoParts = (_config.Repository ?? "").Split('/', 2);
        if (repoParts.Length != 2 || string.IsNullOrWhiteSpace(repoParts[0]) || string.IsNullOrWhiteSpace(repoParts[1]))
            throw new ArgumentException("GitHub repository must be in 'owner/repo' format");

        _owner = repoParts[0];
        _repo = repoParts[1];

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_config.Endpoint ?? "https://api.github.com"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DotCraft-GitHubTracker", "1.0"));

        // In test mode, enable PR tracking unconditionally by using a sentinel path
        // that the test-only IsEnabled override recognizes.
        _issuesWorkflowPath = null;
        _pullRequestWorkflowPath = TestModeSentinel;
    }

    // Used by the test constructor to bypass file-existence checks.
    private const string TestModeSentinel = "__test__";

    private bool IsIssueTrackingEnabled() => WorkflowFileExists(_issuesWorkflowPath);

    private bool IsPullRequestTrackingEnabled() =>
        _pullRequestWorkflowPath == TestModeSentinel || WorkflowFileExists(_pullRequestWorkflowPath);

    #region IWorkItemTracker – candidate fetching

    public async Task<IReadOnlyList<TrackedWorkItem>> FetchCandidateWorkItemsAsync(CancellationToken ct = default)
    {
        var candidates = new List<TrackedWorkItem>();

        if (IsIssueTrackingEnabled())
            candidates.AddRange(await FetchCandidateIssuesOnlyAsync(ct));

        if (IsPullRequestTrackingEnabled())
            candidates.AddRange(await FetchCandidatePullRequestsAsync(ct));

        _logger.LogDebug("Fetched {Count} candidate work items from GitHub", candidates.Count);
        return candidates;
    }

    private async Task<List<TrackedWorkItem>> FetchCandidateIssuesOnlyAsync(CancellationToken ct)
    {
        var allIssues = new List<TrackedWorkItem>();
        var page = 1;

        while (true)
        {
            var url = $"/repos/{_owner}/{_repo}/issues?state=open&per_page=50&page={page}&sort=created&direction=asc";

            if (!string.IsNullOrEmpty(_config.AssigneeFilter))
                url += $"&assignee={Uri.EscapeDataString(_config.AssigneeFilter)}";

            var response = await _httpClient.GetAsync(url, ct);
            await EnsureGitHubSuccessAsync(response, $"GET issues page {page} for {_owner}/{_repo}", ct);

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

        return allIssues;
    }

    /// <summary>
    /// Fetches all open non-draft pull requests in active review states.
    /// Populates <see cref="TrackedWorkItem.HeadSha"/> for re-review detection.
    /// See PR Lifecycle Spec sections 3.1–3.3; Symphony SPEC section 11.1.
    /// </summary>
    private async Task<List<TrackedWorkItem>> FetchCandidatePullRequestsAsync(CancellationToken ct)
    {
        var result = new List<TrackedWorkItem>();
        var page = 1;

        while (true)
        {
            var url = $"/repos/{_owner}/{_repo}/pulls?state=open&per_page=50&page={page}&sort=created&direction=asc";
            var response = await _httpClient.GetAsync(url, ct);
            await EnsureGitHubSuccessAsync(response, $"GET pull requests page {page} for {_owner}/{_repo}", ct);

            var items = await response.Content.ReadFromJsonAsync<List<GitHubPull>>(JsonOptions, ct) ?? [];
            if (items.Count == 0) break;

            foreach (var pr in items)
            {
                if (pr.Draft == true) continue;

                // All open non-draft PRs are candidates; no label gate.
                // See PR Lifecycle Spec section 3.1; Symphony SPEC section 11.1 (fetch_candidate_issues).
                var reviewState = await FetchAggregatedReviewStateAsync(pr.Number, ct);
                var tracked = NormalizePullRequest(pr, reviewState);
                var normalizedState = tracked.State.Trim().ToLowerInvariant();

                var isActive = _config.PullRequestActiveStates
                    .Any(s => string.Equals(s.Trim(), normalizedState, StringComparison.OrdinalIgnoreCase));

                if (isActive)
                    result.Add(tracked);
            }

            if (items.Count < 50) break;
            page++;
        }

        _logger.LogDebug("Fetched {Count} candidate pull requests from GitHub", result.Count);
        return result;
    }

    #endregion

    #region IWorkItemTracker – state reconciliation

    public async Task<IReadOnlyList<WorkItemStateSnapshot>> FetchWorkItemStatesByIdsAsync(
        IReadOnlyList<string> workItemIds, CancellationToken ct = default)
    {
        var snapshots = new List<WorkItemStateSnapshot>();

        foreach (var id in workItemIds)
        {
            try
            {
                // The /issues/{n} endpoint works for both issues and PRs on GitHub.
                var url = $"/repos/{_owner}/{_repo}/issues/{id}";
                var response = await _httpClient.GetAsync(url, ct);
                await EnsureGitHubSuccessAsync(response, $"GET issue/PR #{id} state for {_owner}/{_repo}", ct);

                var item = await response.Content.ReadFromJsonAsync<GitHubIssue>(JsonOptions, ct);
                if (item == null) continue;

                string state;
                if (item.PullRequest != null)
                {
                    var prState = await FetchPullRequestGitHubStateAsync(int.Parse(id), ct);
                    if (string.Equals(prState, "merged", StringComparison.OrdinalIgnoreCase))
                    {
                        state = "Merged";
                    }
                    else if (string.Equals(item.State, "closed", StringComparison.OrdinalIgnoreCase))
                    {
                        state = "Closed";
                    }
                    else
                    {
                        var reviewState = await FetchAggregatedReviewStateAsync(int.Parse(id), ct);
                        state = DerivePullRequestState(item.State ?? "open", reviewState);
                    }
                }
                else
                {
                    state = DeriveState(item);
                }

                snapshots.Add(new WorkItemStateSnapshot { Id = id, State = state });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch state for work item {Id}", id);
            }
        }

        return snapshots;
    }

    public async Task<IReadOnlyList<TrackedWorkItem>> FetchWorkItemsByStatesAsync(
        IReadOnlyList<string> stateNames, CancellationToken ct = default)
    {
        if (stateNames.Count == 0) return [];

        var needsClosed = stateNames.Any(s =>
            string.Equals(s.Trim(), "done", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Trim(), "closed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Trim(), "cancelled", StringComparison.OrdinalIgnoreCase));

        var needsMerged = IsPullRequestTrackingEnabled() && stateNames.Any(s =>
            string.Equals(s.Trim(), "merged", StringComparison.OrdinalIgnoreCase));

        var issues = new List<TrackedWorkItem>();

        if (needsClosed)
        {
            var url = $"/repos/{_owner}/{_repo}/issues?state=closed&per_page=50&page=1&sort=updated&direction=desc";
            var response = await _httpClient.GetAsync(url, ct);
            await EnsureGitHubSuccessAsync(response, $"GET closed issues for {_owner}/{_repo}", ct);

            var items = await response.Content.ReadFromJsonAsync<List<GitHubIssue>>(JsonOptions, ct) ?? [];
            foreach (var item in items)
            {
                if (item.PullRequest != null) continue;
                issues.Add(NormalizeIssue(item));
            }
        }

        if (needsMerged || (needsClosed && IsPullRequestTrackingEnabled()))
        {
            var url = $"/repos/{_owner}/{_repo}/pulls?state=closed&per_page=50&page=1&sort=updated&direction=desc";
            var response = await _httpClient.GetAsync(url, ct);
            await EnsureGitHubSuccessAsync(response, $"GET closed pull requests for {_owner}/{_repo}", ct);

            var items = await response.Content.ReadFromJsonAsync<List<GitHubPull>>(JsonOptions, ct) ?? [];
            foreach (var pr in items)
            {
                var prState = pr.Merged == true ? "Merged" : "Closed";
                var isRequested = stateNames.Any(s =>
                    string.Equals(s.Trim(), prState, StringComparison.OrdinalIgnoreCase));
                if (isRequested)
                    issues.Add(NormalizePullRequest(pr, PullRequestReviewState.None));
            }
        }

        return issues;
    }

    #endregion

    #region IWorkItemTracker – mutations

    public async Task CloseIssueAsync(string issueId, string reason, CancellationToken ct = default)
    {
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

        var patchUrl = $"/repos/{_owner}/{_repo}/issues/{issueId}";
        var body = JsonSerializer.Serialize(new { state = "closed" }, JsonOptions);
        var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PatchAsync(patchUrl, content, ct);

        if (response.IsSuccessStatusCode)
            _logger.LogInformation("Closed GitHub issue #{IssueId}: {Reason}", issueId, reason);
        else
            _logger.LogWarning("Failed to close issue #{IssueId}: {Status}", issueId, response.StatusCode);
    }

    public async Task SubmitReviewAsync(string pullNumber, string body, string @event, CancellationToken ct = default)
    {
        var url = $"/repos/{_owner}/{_repo}/pulls/{pullNumber}/reviews";
        var payload = JsonSerializer.Serialize(new { body, @event }, JsonOptions);
        var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, ct);

        if (response.IsSuccessStatusCode)
            _logger.LogInformation("Submitted {Event} review on PR #{Number}", @event, pullNumber);
        else
            await EnsureGitHubSuccessAsync(response, $"POST review ({@event}) on PR #{pullNumber} for {_owner}/{_repo}", ct);
    }

    public async Task<string> FetchPullRequestDiffAsync(string pullNumber, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/repos/{_owner}/{_repo}/pulls/{pullNumber}");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.diff"));

        var response = await _httpClient.SendAsync(request, ct);
        await EnsureGitHubSuccessAsync(response, $"GET diff for PR #{pullNumber} for {_owner}/{_repo}", ct);

        return await response.Content.ReadAsStringAsync(ct);
    }

    #endregion

    #region Pull-request helpers

    private async Task<PullRequestReviewState> FetchAggregatedReviewStateAsync(int prNumber, CancellationToken ct)
    {
        try
        {
            var url = $"/repos/{_owner}/{_repo}/pulls/{prNumber}/reviews";
            var response = await _httpClient.GetAsync(url, ct);
            await EnsureGitHubSuccessAsync(response, $"GET reviews for PR #{prNumber} in {_owner}/{_repo}", ct);

            var reviews = await response.Content.ReadFromJsonAsync<List<GitHubReview>>(JsonOptions, ct) ?? [];

            // Aggregate: latest review per user wins.
            var latestByUser = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in reviews)
            {
                if (r.User?.Login == null || string.IsNullOrEmpty(r.State)) continue;
                latestByUser[r.User.Login] = r.State;
            }

            if (latestByUser.Values.Any(s => string.Equals(s, "CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase)))
                return PullRequestReviewState.ChangesRequested;

            if (latestByUser.Values.Any(s => string.Equals(s, "APPROVED", StringComparison.OrdinalIgnoreCase)))
                return PullRequestReviewState.Approved;

            if (latestByUser.Count > 0)
                return PullRequestReviewState.Pending;

            return PullRequestReviewState.None;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch reviews for PR #{Number}", prNumber);
            return PullRequestReviewState.None;
        }
    }

    /// <summary>
    /// Fetch the merged/open/closed status from the pulls endpoint
    /// (the issues endpoint does not expose the merged flag).
    /// </summary>
    private async Task<string> FetchPullRequestGitHubStateAsync(int prNumber, CancellationToken ct)
    {
        try
        {
            var url = $"/repos/{_owner}/{_repo}/pulls/{prNumber}";
            var response = await _httpClient.GetAsync(url, ct);
            await EnsureGitHubSuccessAsync(response, $"GET PR #{prNumber} state for {_owner}/{_repo}", ct);

            var pr = await response.Content.ReadFromJsonAsync<GitHubPull>(JsonOptions, ct);
            if (pr?.Merged == true) return "merged";
            return pr?.State ?? "open";
        }
        catch
        {
            return "open";
        }
    }

    private static string DerivePullRequestState(string ghState, PullRequestReviewState reviewState)
    {
        if (string.Equals(ghState, "closed", StringComparison.OrdinalIgnoreCase))
            return "Closed";

        return reviewState switch
        {
            PullRequestReviewState.Approved => "Approved",
            PullRequestReviewState.ChangesRequested => "Changes Requested",
            _ => "Pending Review",
        };
    }

    private TrackedWorkItem NormalizePullRequest(GitHubPull pr, PullRequestReviewState reviewState)
    {
        var labels = pr.Labels?.Select(l => l.Name?.ToLowerInvariant() ?? "").Where(l => l.Length > 0).ToList() ?? [];
        var ghState = pr.Merged == true ? "merged" : (pr.State ?? "open");

        return new TrackedWorkItem
        {
            Id = pr.Number.ToString(),
            Identifier = $"#{pr.Number}",
            Title = pr.Title ?? "",
            Description = pr.Body,
            Priority = DerivePriority(labels),
            State = ghState == "merged" ? "Merged" : DerivePullRequestState(ghState, reviewState),
            Kind = WorkItemKind.PullRequest,
            BranchName = pr.Head?.Ref,
            HeadBranch = pr.Head?.Ref,
            BaseBranch = pr.Base?.Ref,
            DiffUrl = pr.DiffUrl,
            ReviewState = reviewState,
            IsDraft = pr.Draft == true,
            HeadSha = pr.Head?.Sha,
            Url = pr.HtmlUrl,
            Labels = labels,
            BlockedBy = [],
            CreatedAt = pr.CreatedAt,
            UpdatedAt = pr.UpdatedAt,
        };
    }

    #endregion

    #region Issue helpers

    private TrackedWorkItem NormalizeIssue(GitHubIssue gh)
    {
        var labels = gh.Labels?.Select(l => l.Name?.ToLowerInvariant() ?? "").Where(l => l.Length > 0).ToList()
            ?? [];

        return new TrackedWorkItem
        {
            Id = gh.Number.ToString(),
            Identifier = $"#{gh.Number}",
            Title = gh.Title ?? "",
            Description = gh.Body,
            Priority = DerivePriority(labels),
            State = DeriveState(gh),
            Kind = WorkItemKind.Issue,
            BranchName = null,
            Url = gh.HtmlUrl,
            Labels = labels,
            BlockedBy = ParseBlockedBy(gh.Body),
            CreatedAt = gh.CreatedAt,
            UpdatedAt = gh.UpdatedAt,
        };
    }

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

    #endregion

    #region Shared utilities

    /// <summary>
    /// Validates a GitHub API response and throws with actionable diagnostics on failure.
    /// Surfaces the required permissions header and response body so operators can fix
    /// token configuration without digging through raw exceptions.
    /// </summary>
    private async Task EnsureGitHubSuccessAsync(HttpResponseMessage response, string context, CancellationToken ct = default)
    {
        if (response.IsSuccessStatusCode) return;

        var status = (int)response.StatusCode;
        string? body = null;

        try { body = await response.Content.ReadAsStringAsync(ct); }
        catch { /* best-effort */ }

        // GitHub returns the required scope in this header on 403s.
        var requiredPermissions = response.Headers.TryGetValues("x-accepted-github-permissions", out var vals)
            ? string.Join(", ", vals)
            : null;

        var hint = status switch
        {
            401 => "Token is missing or invalid. Set a valid GITHUB_TOKEN with the required permissions.",
            403 when requiredPermissions != null =>
                $"Token lacks required permission(s): [{requiredPermissions}]. " +
                "Update the Fine-grained PAT in your config to grant these permissions.",
            403 => "Token does not have permission for this operation. Check the PAT scopes/permissions.",
            404 => $"Resource not found: {context}. Verify the repository name and that the token has 'Contents: Read' access.",
            _ => null
        };

        var message = $"GitHub API error {status} ({response.ReasonPhrase}) for {context}.";
        if (hint != null) message += $" Hint: {hint}";
        if (!string.IsNullOrWhiteSpace(body)) message += $" Response: {body}";

        _logger.LogError("{Message}", message);
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string? ResolveWorkflowPath(string? configuredPath, string workspacePath) =>
        string.IsNullOrWhiteSpace(configuredPath)
            ? null
            : Path.GetFullPath(configuredPath, workspacePath);

    private static bool WorkflowFileExists(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    /// <summary>
    /// Returns the token value only if it is a resolved, non-empty string.
    /// Values that still look like unexpanded env var placeholders (starting with '$')
    /// are treated as absent so that no auth header is sent.
    /// </summary>
    private static string? ResolveConfigToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.StartsWith('$')) return null;
        return value;
    }

    public void Dispose() => _httpClient.Dispose();

    #endregion

    #region GitHub API DTOs

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

    private sealed class GitHubPull
    {
        public int Number { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? State { get; set; }
        public bool? Draft { get; set; }
        public bool? Merged { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("diff_url")]
        public string? DiffUrl { get; set; }

        public GitHubPullRef? Head { get; set; }
        public GitHubPullRef? Base { get; set; }
        public List<GitHubLabel>? Labels { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class GitHubPullRef
    {
        public string? Ref { get; set; }
        public string? Sha { get; set; }
    }

    private sealed class GitHubReview
    {
        public string? State { get; set; }
        public GitHubUser? User { get; set; }
    }

    private sealed class GitHubUser
    {
        public string? Login { get; set; }
    }

    private sealed class GitHubLabel
    {
        public string? Name { get; set; }
    }

    #endregion
}
