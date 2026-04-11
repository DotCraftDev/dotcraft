using System.Net;
using System.Text;
using System.Text.Json;
using DotCraft.GitHubTracker.Tracker;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.Tracker;

public sealed class StructuredReviewSubmissionTests
{
    [Fact]
    public async Task SubmitStructuredReviewAsync_BatchedReview_IncludesSummaryCommentsAndSuggestion()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.Enqueue(HttpMethod.Get, "/pulls/123", HttpStatusCode.OK, BuildPullRequestJson("head-sha-123"));
        handler.Enqueue(HttpMethod.Get, "/pulls/123/files", HttpStatusCode.OK, BuildPullRequestFilesJson("src/Example.cs"));
        handler.Enqueue(HttpMethod.Get, "/pulls/123", HttpStatusCode.OK, BuildPullRequestDiff("src/Example.cs", 40, 42));
        handler.Enqueue(HttpMethod.Post, "/pulls/123/reviews", HttpStatusCode.OK, "{}");

        var adapter = CreateAdapter(handler);
        var summary = new PullRequestReviewSummary
        {
            MajorCount = 1,
            MinorCount = 0,
            SuggestionCount = 1,
            Body = "Found 1 major issue.",
        };
        var comments = new[]
        {
            new PullRequestInlineComment
            {
                Severity = ReviewFindingSeverity.Red,
                Title = "Missing null guard",
                Body = "A null payload will throw before validation can run.",
                Path = "src/Example.cs",
                Line = 42,
                StartLine = 40,
                Suggestion = new PullRequestSuggestion
                {
                    Replacement = "if (payload is null)\n{\n    return Result.Invalid(\"payload is required\");\n}",
                },
            },
        };

        var result = await adapter.SubmitStructuredReviewAsync("123", summary, comments);

        Assert.True(result.SummaryPosted);
        Assert.Equal(1, result.InlineRequestedCount);
        Assert.Equal(1, result.InlinePostedCount);
        Assert.Equal(0, result.InlineFailedCount);
        Assert.False(result.UsedFallback);

        var reviewRequest = Assert.Single(
            handler.Requests,
            r => r.Method == HttpMethod.Post &&
                r.Path.EndsWith("/pulls/123/reviews", StringComparison.Ordinal));

        using var payload = JsonDocument.Parse(reviewRequest.Body);
        var root = payload.RootElement;
        Assert.Equal("COMMENT", root.GetProperty("event").GetString());
        Assert.Equal("head-sha-123", root.GetProperty("commit_id").GetString());
        Assert.Equal(summary.Body, root.GetProperty("body").GetString());

        var reviewComments = root.GetProperty("comments");
        Assert.Equal(1, reviewComments.GetArrayLength());

        var inline = reviewComments[0];
        Assert.Equal("src/Example.cs", inline.GetProperty("path").GetString());
        Assert.Equal(42, inline.GetProperty("line").GetInt32());
        Assert.Equal(40, inline.GetProperty("start_line").GetInt32());
        Assert.Equal("RIGHT", inline.GetProperty("side").GetString());
        Assert.Equal("RIGHT", inline.GetProperty("start_side").GetString());

        var body = inline.GetProperty("body").GetString();
        Assert.NotNull(body);
        Assert.Contains("**Missing null guard**", body);
        Assert.Contains("```suggestion", body);
        Assert.Contains("payload is required", body);
    }

    [Fact]
    public async Task SubmitStructuredReviewAsync_WhenBatchFails_FallsBackToSummaryThenInlineWithoutSuggestion()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.Enqueue(HttpMethod.Get, "/pulls/123", HttpStatusCode.OK, BuildPullRequestJson("head-sha-123"));
        handler.Enqueue(HttpMethod.Get, "/pulls/123/files", HttpStatusCode.OK, BuildPullRequestFilesJson("src/Example.cs"));
        handler.Enqueue(HttpMethod.Get, "/pulls/123", HttpStatusCode.OK, BuildPullRequestDiff("src/Example.cs", 12, 12));
        handler.Enqueue(HttpMethod.Post, "/pulls/123/reviews", HttpStatusCode.UnprocessableEntity, "{\"message\":\"bad inline\"}");
        handler.Enqueue(HttpMethod.Post, "/pulls/123/reviews", HttpStatusCode.OK, "{}");
        handler.Enqueue(HttpMethod.Post, "/pulls/123/comments", HttpStatusCode.UnprocessableEntity, "{\"message\":\"bad suggestion\"}");
        handler.Enqueue(HttpMethod.Post, "/pulls/123/comments", HttpStatusCode.Created, "{}");

        var adapter = CreateAdapter(handler);
        var summary = new PullRequestReviewSummary
        {
            MajorCount = 0,
            MinorCount = 1,
            SuggestionCount = 1,
            Body = "Found 1 minor issue.",
        };
        var comments = new[]
        {
            new PullRequestInlineComment
            {
                Severity = ReviewFindingSeverity.Yellow,
                Title = "Prefer explicit guard",
                Body = "This edge case should be handled before the loop runs.",
                Path = "src/Example.cs",
                Line = 12,
                Suggestion = new PullRequestSuggestion
                {
                    Replacement = "if (items.Count == 0)\n{\n    return;\n}",
                },
            },
        };

        var result = await adapter.SubmitStructuredReviewAsync("123", summary, comments);

        Assert.True(result.SummaryPosted);
        Assert.True(result.UsedFallback);
        Assert.Equal(1, result.InlineRequestedCount);
        Assert.Equal(1, result.InlinePostedCount);
        Assert.Equal(0, result.InlineFailedCount);
        Assert.NotEmpty(result.Warnings);

        var reviewRequests = handler.Requests
            .Where(r => r.Method == HttpMethod.Post && r.Path.EndsWith("/pulls/123/reviews", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, reviewRequests.Count);

        using var fallbackSummaryPayload = JsonDocument.Parse(reviewRequests[1].Body);
        Assert.False(fallbackSummaryPayload.RootElement.TryGetProperty("comments", out _));
        Assert.Equal(summary.Body, fallbackSummaryPayload.RootElement.GetProperty("body").GetString());

        var commentRequests = handler.Requests
            .Where(r => r.Method == HttpMethod.Post && r.Path.EndsWith("/pulls/123/comments", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, commentRequests.Count);

        using var firstInlinePayload = JsonDocument.Parse(commentRequests[0].Body);
        using var secondInlinePayload = JsonDocument.Parse(commentRequests[1].Body);

        var firstInlineBody = firstInlinePayload.RootElement.GetProperty("body").GetString();
        var secondInlineBody = secondInlinePayload.RootElement.GetProperty("body").GetString();

        Assert.NotNull(firstInlineBody);
        Assert.NotNull(secondInlineBody);
        Assert.Contains("```suggestion", firstInlineBody);
        Assert.DoesNotContain("```suggestion", secondInlineBody);
        Assert.Contains("**Prefer explicit guard**", secondInlineBody);
    }

    [Fact]
    public async Task SubmitStructuredReviewAsync_WhenAnchorIsNotInDiff_PostsSummaryOnlyAndReportsWarning()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.Enqueue(HttpMethod.Get, "/pulls/123", HttpStatusCode.OK, BuildPullRequestJson("head-sha-123"));
        handler.Enqueue(HttpMethod.Get, "/pulls/123/files", HttpStatusCode.OK, BuildPullRequestFilesJson("src/Example.cs"));
        handler.Enqueue(HttpMethod.Get, "/pulls/123", HttpStatusCode.OK, BuildPullRequestDiff("src/Example.cs", 10, 12));
        handler.Enqueue(HttpMethod.Post, "/pulls/123/reviews", HttpStatusCode.OK, "{}");

        var adapter = CreateAdapter(handler);
        var summary = new PullRequestReviewSummary
        {
            MajorCount = 0,
            MinorCount = 1,
            SuggestionCount = 0,
            Body = "Found 1 minor issue.",
        };
        var comments = new[]
        {
            new PullRequestInlineComment
            {
                Severity = ReviewFindingSeverity.Yellow,
                Title = "Outside diff",
                Body = "This line is not commentable in the current PR diff.",
                Path = "src/Example.cs",
                Line = 99,
            },
        };

        var result = await adapter.SubmitStructuredReviewAsync("123", summary, comments);

        Assert.True(result.SummaryPosted);
        Assert.False(result.UsedFallback);
        Assert.Equal(1, result.InlineRequestedCount);
        Assert.Equal(0, result.InlinePostedCount);
        Assert.Equal(1, result.InlineFailedCount);
        Assert.NotEmpty(result.Warnings);

        var reviewRequests = handler.Requests
            .Where(r => r.Method == HttpMethod.Post && r.Path.EndsWith("/pulls/123/reviews", StringComparison.Ordinal))
            .ToList();
        Assert.Single(reviewRequests);

        using var payload = JsonDocument.Parse(reviewRequests[0].Body);
        Assert.False(payload.RootElement.TryGetProperty("comments", out _));

        Assert.DoesNotContain(
            handler.Requests,
            r => r.Path.EndsWith("/pulls/123/comments", StringComparison.Ordinal));
    }

    private static GitHubTrackerAdapter CreateAdapter(HttpMessageHandler handler)
    {
        var config = new GitHubTrackerConfig
        {
            PullRequestWorkflowPath = "PR_WORKFLOW.md",
            Tracker = new GitHubTrackerTrackerConfig
            {
                Repository = "owner/repo",
                ApiKey = "test-token",
                PullRequestActiveStates = ["Pending Review"],
                PullRequestTerminalStates = ["Merged", "Closed", "Approved"],
            },
        };

        return new GitHubTrackerAdapter(
            config,
            handler,
            NullLogger<GitHubTrackerAdapter>.Instance);
    }

    private static string BuildPullRequestJson(string headSha) =>
        JsonSerializer.Serialize(new
        {
            number = 123,
            title = "PR #123",
            state = "open",
            merged = false,
            draft = false,
            html_url = "https://github.com/owner/repo/pull/123",
            diff_url = "https://github.com/owner/repo/pull/123.diff",
            head = new
            {
                @ref = "feature/123",
                sha = headSha,
            },
            @base = new
            {
                @ref = "main",
            },
        });

    private static string BuildPullRequestFilesJson(string path) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                filename = path,
                status = "modified",
                additions = 3,
                deletions = 1,
            },
        });

    private static string BuildPullRequestDiff(string path, int startLine, int endLine)
    {
        var lines = new List<string>
        {
            $"diff --git a/{path} b/{path}",
            $"--- a/{path}",
            $"+++ b/{path}",
            $"@@ -{startLine},0 +{startLine},{Math.Max(1, endLine - startLine + 1)} @@",
        };

        for (var line = startLine; line <= endLine; line++)
            lines.Add($"+line {line}");

        return string.Join("\n", lines);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Expectation> _expectations = new();

        public List<CapturedRequest> Requests { get; } = [];

        public void Enqueue(HttpMethod method, string pathContains, HttpStatusCode statusCode, string body)
        {
            _expectations.Enqueue(new Expectation(method, pathContains, statusCode, body));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_expectations.Count == 0)
                throw new InvalidOperationException($"Unexpected request with no expectation configured: {request.Method} {request.RequestUri}");

            var expected = _expectations.Dequeue();
            var path = request.RequestUri?.ToString() ?? string.Empty;
            if (request.Method != expected.Method || !path.Contains(expected.PathContains, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Unexpected request. Expected {expected.Method} {expected.PathContains}, got {request.Method} {path}");
            }

            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(request.Method, path, body));

            return new HttpResponseMessage(expected.StatusCode)
            {
                Content = new StringContent(expected.Body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record Expectation(HttpMethod Method, string PathContains, HttpStatusCode StatusCode, string Body);

    private sealed record CapturedRequest(HttpMethod Method, string Path, string Body);
}
