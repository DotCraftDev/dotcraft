using DotCraft.Localization;
using DotCraft.Protocol;
using Spectre.Console;

namespace DotCraft.CLI;

/// <summary>
/// Session selector component wrapping Spectre.Console SelectionPrompt interactions.
/// </summary>
public static class SessionPrompt
{
    /// <summary>
    /// Shows a selection prompt for loading a Session Protocol thread.
    /// </summary>
    public static string? SelectThreadToLoad(IReadOnlyList<ThreadSummary> threads, string? currentThreadId)
    {
        if (threads.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]{Strings.NoSessionsAvailable}[/]");
            return null;
        }

        AnsiConsole.WriteLine();

        var options = threads.Select(t => new SessionOption
        {
            Key = t.Id,
            CreatedAt = t.CreatedAt.ToString("O"),
            UpdatedAt = t.LastActiveAt.ToString("O"),
            IsCurrent = t.Id == currentThreadId,
            FirstUserMessage = t.DisplayName
        }).Prepend(BuildCancelOption()).ToList();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<SessionOption>()
                .Title($"[green]{Strings.SelectSessionToLoadTitle}[/]")
                .AddChoices(options)
                .UseConverter(FormatSessionOption)
                .PageSize(10));

        if (choice.IsCancel)
        {
            AnsiConsole.MarkupLine($"[grey]{Strings.Cancelled}[/]");
            AnsiConsole.WriteLine();
            return null;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] {Strings.SessionSelected}：[cyan]{EscapeMarkup(choice.Key)}[/]");
        AnsiConsole.WriteLine();
        return choice.Key;
    }

    /// <summary>
    /// Shows a selection prompt for deleting a Session Protocol thread.
    /// </summary>
    public static string? SelectThreadToDelete(IReadOnlyList<ThreadSummary> threads, string? currentThreadId)
    {
        if (threads.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]{Strings.NoSessionsToDelete}[/]");
            return null;
        }

        AnsiConsole.WriteLine();

        var options = threads.Select(t => new SessionOption
        {
            Key = t.Id,
            CreatedAt = t.CreatedAt.ToString("O"),
            UpdatedAt = t.LastActiveAt.ToString("O"),
            IsCurrent = t.Id == currentThreadId,
            FirstUserMessage = t.DisplayName
        }).Prepend(BuildCancelOption()).ToList();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<SessionOption>()
                .Title($"[red]{Strings.SelectSessionToDeleteTitle}[/]")
                .AddChoices(options)
                .UseConverter(FormatSessionOption)
                .PageSize(10));

        if (choice.IsCancel)
        {
            AnsiConsole.MarkupLine($"[grey]{Strings.Cancelled}[/]");
            AnsiConsole.WriteLine();
            return null;
        }

        AnsiConsole.MarkupLine($"[red]→[/] {Strings.SessionSelected}：[cyan]{EscapeMarkup(choice.Key)}[/]");
        AnsiConsole.WriteLine();
        return choice.Key;
    }

    /// <summary>
    /// Shows a confirmation prompt before deleting a session.
    /// </summary>
    /// <param name="sessionId">The session ID to delete.</param>
    /// <param name="isCurrent">Whether the session is the currently active one.</param>
    /// <returns>True if the user confirmed deletion.</returns>
    public static bool ConfirmDelete(string sessionId, bool isCurrent)
    {
        var escapedId = EscapeMarkup(sessionId);
        var message = isCurrent
            ? $"[yellow]{Strings.ConfirmDeleteCurrentWarning(escapedId)}[/]\n[yellow]{Strings.ConfirmDeleteCurrentSuffix}[/]"
            : $"[yellow]{Strings.ConfirmDeleteOther(escapedId)}[/]";

        var panel = new Panel(message)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        return AnsiConsole.Confirm($"[red]{Strings.ConfirmDeleteQuestion}[/]");
    }

    private const int PreviewMaxLength = 50;

    /// <summary>
    /// Formats a session option for display in the selection prompt.
    /// </summary>
    private static string FormatSessionOption(SessionOption option)
    {
        if (option.IsCancel)
            return $"[grey]  {EscapeMarkup(Strings.Cancel)}[/]";

        var prefix = option.IsCurrent ? "📍 " : "  ";
        var key = EscapeMarkup(option.Key);
        var updatedAt = ParseTimestamp(option.UpdatedAt);
        var timeAgo = GetTimeAgo(updatedAt);

        var preview = "";
        if (!string.IsNullOrWhiteSpace(option.FirstUserMessage))
        {
            var msg = option.FirstUserMessage.ReplaceLineEndings(" ").Trim();
            if (msg.Length > PreviewMaxLength)
                msg = msg[..PreviewMaxLength] + "...";
            preview = $" [dim]{EscapeMarkup(msg)}[/]";
        }

        return $"{prefix}[cyan]{key}[/] [grey]({timeAgo})[/]{preview}";
    }

    /// <summary>
    /// Parses an ISO 8601 timestamp string into a local DateTime.
    /// </summary>
    private static DateTime ParseTimestamp(string? timestamp)
    {
        if (string.IsNullOrEmpty(timestamp))
            return DateTime.MinValue;

        if (DateTimeOffset.TryParse(timestamp, out var dto))
            return dto.LocalDateTime;

        return DateTime.MinValue;
    }

    /// <summary>
    /// Returns a human-readable relative time description for the given datetime.
    /// </summary>
    private static string GetTimeAgo(DateTime dateTime)
    {
        if (dateTime == DateTime.MinValue)
            return Strings.TimeUnknown;

        var diff = DateTime.UtcNow - dateTime;

        if (diff.TotalMinutes < 1)
            return Strings.TimeJustNow;
        if (diff.TotalMinutes < 60)
            return Strings.TimeMinutesAgo((int)diff.TotalMinutes);
        if (diff.TotalHours < 24)
            return Strings.TimeHoursAgo((int)diff.TotalHours);
        if (diff.TotalDays < 7)
            return Strings.TimeDaysAgo((int)diff.TotalDays);

        return $"{dateTime:yyyy-MM-dd}";
    }

    /// <summary>
    /// Builds a cancel sentinel option using the current language.
    /// </summary>
    private static SessionOption BuildCancelOption() => new() { IsCancel = true };

    /// <summary>
    /// Session option data model used by the selection prompt.
    /// </summary>
    private sealed class SessionOption
    {
        public string Key { get; set; } = string.Empty;

        public string? CreatedAt { get; set; }

        public string? UpdatedAt { get; set; }

        public bool IsCurrent { get; set; }

        public string? FirstUserMessage { get; set; }

        public bool IsCancel { get; set; }
    }

    /// <summary>
    /// Escapes Spectre.Console markup characters in a string.
    /// </summary>
    private static string EscapeMarkup(this string text) => Markup.Escape(text);
}
