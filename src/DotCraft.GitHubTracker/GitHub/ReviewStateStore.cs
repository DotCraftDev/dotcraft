using System.Text.Json;
using DotCraft.GitHubTracker.Tracker;
using Microsoft.Extensions.Logging;

namespace DotCraft.GitHubTracker.GitHub;

internal sealed class ReviewStateStore(string craftPath, ILogger<ReviewStateStore> logger)
{
    private readonly string _rootDirectory = Path.Combine(craftPath, "review-state");
    private readonly Lock _gate = new();

    public IReadOnlyDictionary<string, StoredReviewState> LoadAll()
    {
        var result = new Dictionary<string, StoredReviewState>(StringComparer.Ordinal);
        if (!Directory.Exists(_rootDirectory))
            return result;

        foreach (var filePath in Directory.GetFiles(_rootDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var state = JsonSerializer.Deserialize<StoredReviewState>(json, JsonOptions);
                if (state == null || string.IsNullOrWhiteSpace(state.PullRequestId))
                    continue;

                result[state.PullRequestId] = state;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load review state file {FilePath}", filePath);
            }
        }

        return result;
    }

    public void Save(StoredReviewState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_gate)
        {
            Directory.CreateDirectory(_rootDirectory);
            var path = Path.Combine(_rootDirectory, $"{state.PullRequestId}.json");
            var json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(path, json);
        }
    }

    public void Delete(string pullRequestId)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(pullRequestId) || pullRequestId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || pullRequestId.Contains("..") || pullRequestId.Contains('/') || pullRequestId.Contains('\\'))
            {
                logger.LogWarning("Rejected invalid pullRequestId for review state deletion: {PullRequestId}", pullRequestId);
                return;
            }

            var path = Path.Combine(_rootDirectory, $"{pullRequestId}.json");
            if (!File.Exists(path))
                return;

            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete review state file for PR {PullRequestId}", pullRequestId);
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

internal sealed class StoredReviewState
{
    public required string PullRequestId { get; init; }

    public string? LastReviewedSha { get; init; }

    public DateTimeOffset ReviewedAtUtc { get; init; }

    public List<PullRequestReviewFinding> Findings { get; init; } = [];
}
