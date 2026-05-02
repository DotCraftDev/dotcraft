namespace DotCraft.Plugins;

public static class PluginIds
{
    public const string BrowserUse = "browser-use";
    public const string LegacyNodeRepl = "node-repl";

    public static string Canonicalize(string pluginId) =>
        string.Equals(pluginId, LegacyNodeRepl, StringComparison.OrdinalIgnoreCase)
            ? BrowserUse
            : pluginId;

    public static bool EqualsCanonical(string left, string right) =>
        string.Equals(Canonicalize(left), Canonicalize(right), StringComparison.OrdinalIgnoreCase);
}
