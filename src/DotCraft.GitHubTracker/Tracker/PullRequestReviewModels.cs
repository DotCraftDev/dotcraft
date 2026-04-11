namespace DotCraft.GitHubTracker.Tracker;

public enum ReviewFindingSeverity
{
    Red,
    Yellow,
}

public enum PullRequestReviewCommentSide
{
    Left,
    Right,
}

public sealed class PullRequestChangedFile
{
    public required string Filename { get; init; }

    public required string Status { get; init; }

    public int Additions { get; init; }

    public int Deletions { get; init; }
}

public sealed class PullRequestReviewFinding
{
    public required ReviewFindingSeverity Severity { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public string? FilePath { get; init; }

    public bool IsResolved { get; init; }
}

public sealed class PullRequestReviewSummary
{
    public int MajorCount { get; init; }

    public int MinorCount { get; init; }

    public int SuggestionCount { get; init; }

    public required string Body { get; init; }
}

public sealed class PullRequestSuggestion
{
    /// <summary>
    /// Replacement text rendered inside a GitHub suggestion block.
    /// </summary>
    public required string Replacement { get; init; }
}

public sealed class PullRequestInlineComment
{
    public required ReviewFindingSeverity Severity { get; init; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    public required string Path { get; init; }

    /// <summary>
    /// Final line of the diff range targeted by the inline comment.
    /// </summary>
    public int Line { get; init; }

    public PullRequestReviewCommentSide Side { get; init; } = PullRequestReviewCommentSide.Right;

    /// <summary>
    /// Optional first line of the diff range for multi-line inline comments.
    /// </summary>
    public int? StartLine { get; init; }

    public PullRequestReviewCommentSide? StartSide { get; init; }

    public PullRequestSuggestion? Suggestion { get; init; }
}
