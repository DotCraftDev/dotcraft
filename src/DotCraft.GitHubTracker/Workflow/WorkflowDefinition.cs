using DotCraft.Configuration;

namespace DotCraft.GitHubTracker.Workflow;

/// <summary>
/// Parsed WORKFLOW.md payload containing config and prompt template.
/// </summary>
public sealed class WorkflowDefinition(GitHubTrackerConfig config, string promptTemplate)
{
    /// <summary>
    /// Configuration parsed from YAML front matter, merged over AppConfig defaults.
    /// </summary>
    public GitHubTrackerConfig Config { get; } = config;

    /// <summary>
    /// Trimmed Markdown body after front matter, used as Liquid prompt template.
    /// </summary>
    public string PromptTemplate { get; } = promptTemplate;
}
