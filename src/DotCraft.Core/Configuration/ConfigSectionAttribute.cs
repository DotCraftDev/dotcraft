namespace DotCraft.Configuration;

/// <summary>
/// Marks a class as a configuration section that will be automatically discovered
/// by the source generator and included in the Dashboard config schema.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ConfigSectionAttribute : Attribute
{
    /// <summary>
    /// The JSON key used for this section in config.json.
    /// Empty string means the section is the root (AppConfig itself).
    /// For nested sections, use dot-separated keys, e.g. "Tools.File".
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// The display name shown in the Dashboard UI.
    /// Defaults to <see cref="Key"/> if not set.
    /// Supports path separator › for sub-sections, e.g. "Tools › File".
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Controls the order of this section in the Dashboard UI.
    /// Lower values appear first. Default is 100.
    /// </summary>
    public int Order { get; set; } = 100;

    /// <summary>
    /// When set, this section is treated as a top-level JSON collection key in config.json.
    /// The Dashboard may render it as a structured object list when item fields are available.
    /// Use this for collection sections like McpServers or ExternalChannels.
    /// When set, <see cref="Key"/> and dot-separated paths are ignored.
    /// </summary>
    public string? RootKey { get; set; }

    public ConfigSectionAttribute(string key)
    {
        Key = key;
    }
}
