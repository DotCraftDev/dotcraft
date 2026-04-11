using DotCraft.GitHubTracker.GitHub;
using DotCraft.GitHubTracker.Tracker;

namespace DotCraft.GitHubTracker.Tests.GitHub;

public sealed class StructuredInlineCommentParsingTests
{
    [Fact]
    public void ParseStructuredInlineComments_NormalizesSingleLineRange()
    {
        const string json = """
            [
              {
                "severity": "YELLOW",
                "title": "🟡 Guard edge case",
                "body": "Body text",
                "path": "./src/Foo.cs",
                "line": 25,
                "startLine": 25,
                "suggestionReplacement": "return;"
              }
            ]
            """;

        var comments = GitHubAutomationSource.GitHubTaskToolProvider.ParseStructuredInlineComments(json);
        var comment = Assert.Single(comments);

        Assert.Equal("src/Foo.cs", comment.Path);
        Assert.Equal(25, comment.Line);
        Assert.Null(comment.StartLine);
        Assert.Null(comment.StartSide);
        Assert.NotNull(comment.Suggestion);
        Assert.Equal("return;", comment.Suggestion!.Replacement);
    }

    [Fact]
    public void ParseStructuredInlineComments_RejectsInvalidStartLine()
    {
        const string json = """
            [
              {
                "severity": "YELLOW",
                "title": "🟡 Guard edge case",
                "body": "Body text",
                "path": "src/Foo.cs",
                "line": 25,
                "startLine": 0
              }
            ]
            """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => GitHubAutomationSource.GitHubTaskToolProvider.ParseStructuredInlineComments(json));

        Assert.Contains("StartLine must be a positive line number", ex.Message);
    }

    [Fact]
    public void ParseStructuredInlineComments_RejectsPathTraversal()
    {
        const string json = """
            [
              {
                "severity": "RED",
                "title": "🔴 Invalid path",
                "body": "Body text",
                "path": "../src/Foo.cs",
                "line": 10
              }
            ]
            """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => GitHubAutomationSource.GitHubTaskToolProvider.ParseStructuredInlineComments(json));

        Assert.Contains("must not contain path traversal", ex.Message);
    }
}
