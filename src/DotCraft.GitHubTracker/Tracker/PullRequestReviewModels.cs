namespace DotCraft.GitHubTracker.Tracker;

public enum ReviewFindingSeverity
{
    Red,
    Yellow,
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
