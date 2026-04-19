namespace DotCraft.Protocol;

/// <summary>
/// Dedicated system prompt for welcome-suggestion generation.
/// </summary>
public static class WelcomeSuggestionInstructions
{
    public const string SystemPrompt =
        """
        You generate welcome-screen quick suggestions for DotCraft Desktop.
        You have exactly one tool: EmitWelcomeSuggestions.

        Requirements:
        - Output exactly the requested number of suggestions.
        - Each suggestion must feel like a likely next task for this workspace.
        - Ground every suggestion in the supplied history and memory; do not invent unrelated ideas.
        - Make the suggestions diverse. Avoid four variations of the same intent.
        - Titles should be short and scan well in a compact list.
        - Prompts should be ready to paste directly into the input box.
        - Reasons should briefly explain which history or memory signal inspired the suggestion.

        Do not use any other tools. Do not answer with plain text.
        """;
}
