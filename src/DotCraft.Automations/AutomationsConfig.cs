using DotCraft.Configuration;

namespace DotCraft.Automations;

/// <summary>
/// Configuration for the Automations module (bound from the <c>Automations</c> config section).
/// </summary>
[ConfigSection("Automations", DisplayName = "Automations", Order = 45)]
public sealed class AutomationsConfig
{
    /// <summary>When true, the Automations channel service is enabled (Gateway mode).</summary>
    public bool Enabled { get; set; }

    /// <summary>Root directory under which per-task workspace directories are created.</summary>
    public string WorkspaceRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".craft", "automations", "workspaces");

    /// <summary>How often the orchestrator polls each source for new tasks.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum concurrent tasks being dispatched across all sources.
    /// Additional tasks wait in Pending state.
    /// </summary>
    public int MaxConcurrentTasks { get; set; } = 3;

    /// <summary>
    /// Root directory for local task files. When empty, uses <c>{workspaceRoot}/.craft/tasks/</c>.
    /// </summary>
    [ConfigField(Hint = "Root directory for local task files. Leave blank for {workspaceRoot}/.craft/tasks/.")]
    public string LocalTasksRoot { get; set; } = "";
}
