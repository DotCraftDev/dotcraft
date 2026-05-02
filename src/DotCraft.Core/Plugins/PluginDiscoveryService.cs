using DotCraft.Configuration;

namespace DotCraft.Plugins;

/// <summary>
/// Describes where a discovered plugin came from.
/// </summary>
public enum PluginDiscoverySourceKind
{
    Workspace,
    Explicit,
    UserGlobal
}

/// <summary>
/// A discovered plugin manifest.
/// </summary>
public sealed record DiscoveredPlugin(
    PluginManifest Manifest,
    PluginDiscoverySourceKind SourceKind,
    string SourceRoot,
    bool Enabled);

/// <summary>
/// Result of a plugin discovery pass.
/// </summary>
public sealed record PluginDiscoveryResult(
    IReadOnlyList<DiscoveredPlugin> Plugins,
    IReadOnlyList<PluginDiagnostic> Diagnostics);

/// <summary>
/// Finds local DotCraft plugin manifests from workspace, configured, and user-global roots.
/// </summary>
public sealed class PluginDiscoveryService(string? userGlobalPluginsPath = null)
{
    /// <summary>
    /// Discovers enabled local plugin manifests for the current workspace.
    /// </summary>
    public PluginDiscoveryResult Discover(AppConfig config, string workspacePath, string botPath)
    {
        var result = DiscoverAll(config, workspacePath, botPath);
        return new PluginDiscoveryResult(
            result.Plugins.Where(plugin => plugin.Enabled).ToArray(),
            result.Diagnostics);
    }

    /// <summary>
    /// Discovers all local plugin manifests for the current workspace, including disabled plugins.
    /// </summary>
    public PluginDiscoveryResult DiscoverAll(AppConfig config, string workspacePath, string botPath)
    {
        var diagnostics = new List<PluginDiagnostic>();
        var candidates = EnumerateCandidates(config, workspacePath, botPath, diagnostics);
        var discovered = new List<DiscoveredPlugin>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var parse = PluginManifestParser.Load(candidate.PluginRoot);
            diagnostics.AddRange(parse.Diagnostics);
            var manifest = parse.Manifest;
            if (manifest == null)
                continue;

            if (PluginIds.EqualsCanonical(manifest.Id, PluginIds.LegacyNodeRepl)
                && string.Equals(manifest.Id, PluginIds.LegacyNodeRepl, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(PluginDiagnostic.Info(
                    "LegacyPluginIgnored",
                    "Legacy plugin id 'node-repl' is ignored; Browser Use is now provided by 'browser-use'.",
                    manifest.Id,
                    path: manifest.ManifestPath));
                continue;
            }

            if (!seen.Add(manifest.Id))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "DuplicatePluginId",
                    $"Plugin '{manifest.Id}' was skipped because a higher-priority root already provided it.",
                    manifest.Id,
                    path: manifest.ManifestPath));
                continue;
            }

            var enabled = config.Plugins.IsPluginEnabled(manifest.Id, defaultEnabled: true);
            if (!enabled)
            {
                diagnostics.Add(PluginDiagnostic.Info(
                    "PluginDisabled",
                    $"Plugin '{manifest.Id}' is disabled by configuration.",
                    manifest.Id,
                    path: manifest.ManifestPath));
            }

            discovered.Add(new DiscoveredPlugin(manifest, candidate.SourceKind, candidate.SourceRoot, enabled));
        }

        return new PluginDiscoveryResult(discovered, diagnostics);
    }

    private IReadOnlyList<PluginCandidate> EnumerateCandidates(
        AppConfig config,
        string workspacePath,
        string botPath,
        List<PluginDiagnostic> diagnostics)
    {
        var roots = new List<(string Path, PluginDiscoverySourceKind SourceKind)>
        {
            (Path.Combine(botPath, "plugins"), PluginDiscoverySourceKind.Workspace)
        };

        foreach (var explicitRoot in config.Plugins.PluginRoots)
        {
            if (string.IsNullOrWhiteSpace(explicitRoot))
                continue;

            var resolved = Path.IsPathRooted(explicitRoot)
                ? explicitRoot
                : Path.Combine(workspacePath, explicitRoot);
            roots.Add((resolved, PluginDiscoverySourceKind.Explicit));
        }

        roots.Add((
            userGlobalPluginsPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".craft",
                "plugins"),
            PluginDiscoverySourceKind.UserGlobal));

        var candidates = new List<PluginCandidate>();
        var candidateRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, sourceKind) in roots)
        {
            var fullRoot = Path.GetFullPath(root);
            if (!Directory.Exists(fullRoot))
            {
                diagnostics.Add(PluginDiagnostic.Info(
                    "PluginRootMissing",
                    $"Plugin root '{fullRoot}' does not exist.",
                    path: fullRoot));
                continue;
            }

            foreach (var pluginRoot in ExpandRoot(fullRoot))
            {
                var fullPluginRoot = Path.GetFullPath(pluginRoot);
                if (!candidateRoots.Add(fullPluginRoot))
                    continue;

                candidates.Add(new PluginCandidate(fullPluginRoot, sourceKind, fullRoot));
            }
        }

        return candidates;
    }

    private static IEnumerable<string> ExpandRoot(string root)
    {
        if (PluginManifestParser.IsValidPluginRoot(root))
        {
            yield return root;
            yield break;
        }

        foreach (var child in Directory.GetDirectories(root).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (PluginManifestParser.IsValidPluginRoot(child))
                yield return child;
        }
    }

    private sealed record PluginCandidate(
        string PluginRoot,
        PluginDiscoverySourceKind SourceKind,
        string SourceRoot);
}
