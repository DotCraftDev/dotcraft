using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Search result returned by <see cref="DeferredToolRegistry.SearchAndActivate"/>.
/// </summary>
public sealed record ToolSearchResult(string Name, string Description);

/// <summary>
/// Holds all deferred MCP tool definitions and tracks which have been activated
/// by the model via <see cref="ToolSearchTool"/>. Activated tools are exposed
/// through <see cref="ActivatedToolsList"/> as a live reference that is shared
/// with <c>FunctionInvokingChatClient.AdditionalTools</c>, allowing the tool
/// invocation loop to find and execute them without rebuilding the agent.
/// </summary>
public sealed class DeferredToolRegistry
{
    private readonly Dictionary<string, AITool> _deferredTools;
    private readonly List<AITool> _activatedTools = [];
    private readonly HashSet<string> _activatedNames = [];
    private readonly object _lock = new();

    /// <summary>
    /// Initialises the registry with the given deferred tool definitions.
    /// </summary>
    public DeferredToolRegistry(IEnumerable<AITool> deferredTools)
    {
        _deferredTools = deferredTools.ToDictionary(t => t.Name, t => t);
    }

    /// <summary>
    /// All deferred tools keyed by tool name.
    /// </summary>
    public IReadOnlyDictionary<string, AITool> DeferredTools => _deferredTools;

    /// <summary>
    /// Live list of activated tools. Pass this directly as
    /// <c>FunctionInvokingChatClient.AdditionalTools</c> — it is read by
    /// the invocation loop on every iteration without snapshotting.
    /// </summary>
    public IList<AITool> ActivatedToolsList => _activatedTools;

    /// <summary>
    /// Returns an immutable snapshot of activated tool names.
    /// Used by <c>DynamicToolInjectionChatClient</c> to detect newly
    /// activated tools since the last LLM call.
    /// </summary>
    public IReadOnlySet<string> GetActivatedToolNames()
    {
        lock (_lock)
        {
            return new HashSet<string>(_activatedNames);
        }
    }

    /// <summary>
    /// Searches deferred tools by keyword and activates matching ones.
    /// Activation means the tool is added to <see cref="ActivatedToolsList"/>
    /// so the invocation loop can execute it, and noted in the name set so
    /// <c>DynamicToolInjectionChatClient</c> can inject the schema into the
    /// next LLM call.
    /// </summary>
    /// <param name="query">Case-insensitive keywords to match against tool names and descriptions.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    public IReadOnlyList<ToolSearchResult> SearchAndActivate(string query, int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Score every deferred tool: name match outranks description match.
        var scored = new List<(AITool Tool, int Score)>();
        foreach (var tool in _deferredTools.Values)
        {
            int score = ScoreTool(tool, terms);
            if (score > 0)
                scored.Add((tool, score));
        }

        scored.Sort(static (a, b) => b.Score.CompareTo(a.Score));

        var results = new List<ToolSearchResult>(Math.Min(scored.Count, maxResults));

        lock (_lock)
        {
            foreach (var (tool, _) in scored.Take(maxResults))
            {
                results.Add(new ToolSearchResult(tool.Name, tool.Description ?? string.Empty));

                if (_activatedNames.Add(tool.Name))
                    _activatedTools.Add(tool);
            }
        }

        return results;
    }

    /// <summary>
    /// Scores a tool against the given search terms.
    /// A name match is worth 2 points per term; a description match is worth 1.
    /// Returns 0 when no term matches.
    /// </summary>
    private static int ScoreTool(AITool tool, string[] terms)
    {
        int score = 0;
        var nameLower = tool.Name.ToLowerInvariant();
        var descLower = (tool.Description ?? string.Empty).ToLowerInvariant();

        foreach (var term in terms)
        {
            var termLower = term.ToLowerInvariant();
            if (nameLower.Contains(termLower))
                score += 2;
            else if (descLower.Contains(termLower))
                score += 1;
        }

        return score;
    }
}
