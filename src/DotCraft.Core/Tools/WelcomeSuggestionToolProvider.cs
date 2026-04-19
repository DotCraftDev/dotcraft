using System.ComponentModel;
using DotCraft.Abstractions;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Single-tool profile for ephemeral welcome-suggestion threads.
/// </summary>
public sealed class WelcomeSuggestionToolProvider : IAgentToolProvider
{
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        yield return AIFunctionFactory.Create(WelcomeSuggestionMethods.EmitWelcomeSuggestions);
    }
}

public sealed class WelcomeSuggestionToolItem
{
    [Description("Short list title shown in the welcome suggestions UI.")]
    public string Title { get; set; } = string.Empty;

    [Description("Full prompt text inserted into the welcome composer when clicked.")]
    public string Prompt { get; set; } = string.Empty;

    [Description("Brief explanation of which history or memory signals inspired this suggestion.")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Tool invoked by the model to submit welcome suggestions.
/// </summary>
public static class WelcomeSuggestionMethods
{
    public const string ToolName = "EmitWelcomeSuggestions";

    [Description("Submit the generated welcome suggestions as one batch.")]
    public static string EmitWelcomeSuggestions(
        [Description("Exactly the requested number of welcome suggestions.")]
        WelcomeSuggestionToolItem[] items)
    {
        _ = items;
        return "Recorded.";
    }
}
