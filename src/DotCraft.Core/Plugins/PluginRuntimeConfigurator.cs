using DotCraft.Configuration;
using DotCraft.Skills;

namespace DotCraft.Plugins;

public static class PluginRuntimeConfigurator
{
    public static PluginDiscoveryResult ConfigureSkillsLoader(
        SkillsLoader? skillsLoader,
        AppConfig config,
        string workspacePath,
        string botPath,
        PluginDiagnosticsStore? diagnosticsStore = null)
    {
        var discovery = new PluginDiscoveryService().DiscoverAll(config, workspacePath, botPath);
        if (skillsLoader != null)
        {
            var sources = discovery.Plugins
                .Where(plugin => plugin.Enabled && !string.IsNullOrWhiteSpace(plugin.Manifest.SkillsPath))
                .Select(plugin => new SkillsLoader.PluginSkillSource(
                    plugin.Manifest.Id,
                    plugin.Manifest.Interface?.DisplayName ?? plugin.Manifest.DisplayName,
                    plugin.Manifest.SkillsPath!))
                .ToArray();
            var disabledSkillNames = ResolveDisabledPluginSkillNames(config, discovery.Plugins);
            skillsLoader.SetPluginSkillSources(sources, disabledSkillNames);
        }

        diagnosticsStore?.Append(discovery.Diagnostics);
        PluginDiagnosticsLogger.Write(discovery.Diagnostics);
        return discovery;
    }

    private static IReadOnlyList<string> ResolveDisabledPluginSkillNames(
        AppConfig config,
        IReadOnlyList<DiscoveredPlugin> plugins)
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in plugins.Where(plugin => !plugin.Enabled))
        {
            AddSkillDirectoryNames(disabled, plugin.Manifest.SkillsPath);
        }

        if (!config.Plugins.IsPluginEnabled(PluginIds.BrowserUse, defaultEnabled: true))
            disabled.Add("browser-use");

        return disabled.ToArray();
    }

    private static void AddSkillDirectoryNames(HashSet<string> names, string? skillsPath)
    {
        if (string.IsNullOrWhiteSpace(skillsPath) || !Directory.Exists(skillsPath))
            return;

        foreach (var dir in Directory.GetDirectories(skillsPath))
        {
            if (File.Exists(Path.Combine(dir, "SKILL.md")))
                names.Add(Path.GetFileName(dir));
        }
    }
}
