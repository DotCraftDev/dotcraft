using System.Text.RegularExpressions;
using Spectre.Console;

namespace DotCraft.WeCom;

/// <summary>
/// IWeComLogger implementation using Spectre.Console.
/// </summary>
public partial class WeComServerLogger : IWeComLogger
{
    public void LogInformation(string message, params object[] args)
    {
        var formatted = FormatMessage(message, args);
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(formatted)}[/]");
    }

    public void LogWarning(string message, params object[] args)
    {
        var formatted = FormatMessage(message, args);
        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(formatted)}[/]");
    }

    public void LogError(Exception? exception, string message, params object[] args)
    {
        var formatted = FormatMessage(message, args);
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(formatted)}[/]");
        if (exception != null)
        {
            AnsiConsole.MarkupLine($"[red]  Exception: {Markup.Escape(exception.Message)}[/]");
        }
    }

    /// <summary>
    /// Format message supporting both {0} style and {Name} style placeholders.
    /// </summary>
    private static string FormatMessage(string message, object[] args)
    {
        if (args.Length == 0) return message;

        // If message contains named placeholders like {Path}, replace them sequentially with args
        if (NamedPlaceholderRegex().IsMatch(message))
        {
            var index = 0;
            return NamedPlaceholderRegex().Replace(message, match =>
                index < args.Length ? args[index++]?.ToString() ?? "" : match.Value);
        }

        return string.Format(message, args);
    }

    [GeneratedRegex(@"\{[A-Za-z]\w*\}")]
    private static partial Regex NamedPlaceholderRegex();
}
