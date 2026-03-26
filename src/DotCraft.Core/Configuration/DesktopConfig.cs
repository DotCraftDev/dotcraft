namespace DotCraft.Configuration;

/// <summary>
/// Optional configuration for DotCraft Desktop client behavior (see specs/desktop-client.md §9.4.1).
/// Loaded from the <c>Desktop</c> key in <c>.craft/config.json</c>.
/// </summary>
public sealed class DesktopConfig
{
    /// <summary>
    /// Origin channel names whose threads are included when the AppServer resolves
    /// <c>thread/list</c> without an explicit <c>crossChannelOrigins</c> parameter.
    /// </summary>
    public List<string>? VisibleChannels { get; set; }
}
