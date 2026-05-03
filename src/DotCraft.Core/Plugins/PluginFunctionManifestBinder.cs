namespace DotCraft.Plugins;

/// <summary>
/// Applies discovered plugin manifest metadata to C# Plugin Function registrations.
/// </summary>
public static class PluginFunctionManifestBinder
{
    /// <summary>
    /// Replaces fallback descriptors with discovered manifest descriptors when a supported backend is available.
    /// </summary>
    public static IReadOnlyList<PluginFunctionRegistration> Bind(
        IEnumerable<PluginFunctionRegistration> fallbackRegistrations,
        IReadOnlyList<DiscoveredPlugin> discoveredPlugins,
        List<PluginDiagnostic> diagnostics,
        PluginDynamicToolProcessManager? processManager = null)
    {
        var fallbacks = fallbackRegistrations.ToArray();
        var fallbackByBackend = fallbacks
            .GroupBy(
                registration => BackendKey(registration.Descriptor.PluginId, registration.Descriptor.Name),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var pluginIdsWithManifest = new HashSet<string>(
            discoveredPlugins.Select(plugin => plugin.Manifest.Id),
            StringComparer.OrdinalIgnoreCase);
        var boundBackendKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bound = new List<PluginFunctionRegistration>();

        foreach (var plugin in discoveredPlugins)
        {
            foreach (var function in plugin.Manifest.Functions)
            {
                var backend = function.Backend;
                if (backend.Kind.Equals("process", StringComparison.OrdinalIgnoreCase))
                {
                    if (processManager == null)
                    {
                        diagnostics.Add(PluginDiagnostic.Warning(
                            "PluginProcessBackendUnavailable",
                            $"Plugin tool '{function.Name}' uses a process backend, but process dynamic tools are not available.",
                            plugin.Manifest.Id,
                            function.Name,
                            plugin.Manifest.ManifestPath));
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(backend.ProcessId))
                    {
                        diagnostics.Add(PluginDiagnostic.Warning(
                            "InvalidPluginProcessBackend",
                            $"Plugin tool '{function.Name}' must declare backend.processId.",
                            plugin.Manifest.Id,
                            function.Name,
                            plugin.Manifest.ManifestPath));
                        continue;
                    }

                    if (!plugin.Manifest.Processes.TryGetValue(backend.ProcessId, out var process))
                    {
                        diagnostics.Add(PluginDiagnostic.Warning(
                            "PluginProcessUnavailable",
                            $"Plugin tool '{function.Name}' references unavailable process '{backend.ProcessId}'.",
                            plugin.Manifest.Id,
                            function.Name,
                            plugin.Manifest.ManifestPath));
                        continue;
                    }

                    bound.Add(new PluginFunctionRegistration(
                        function.ToDescriptor(plugin.Manifest.Id),
                        new PluginDynamicToolProcessInvoker(
                            processManager,
                            plugin.Manifest,
                            process,
                            backend.ToolName ?? function.Name)));
                    continue;
                }

                if (!backend.Kind.Equals("builtin", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(PluginDiagnostic.Warning(
                        "UnsupportedPluginBackend",
                        $"Plugin tool '{function.Name}' uses unsupported backend kind '{backend.Kind}'.",
                        plugin.Manifest.Id,
                        function.Name,
                        plugin.Manifest.ManifestPath));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(backend.ProviderId)
                    || string.IsNullOrWhiteSpace(backend.FunctionName))
                {
                    diagnostics.Add(PluginDiagnostic.Warning(
                        "InvalidBuiltinBackend",
                        $"Plugin tool '{function.Name}' must declare backend.providerId and backend.functionName.",
                        plugin.Manifest.Id,
                        function.Name,
                        plugin.Manifest.ManifestPath));
                    continue;
                }

                if (!backend.ProviderId.Equals(plugin.Manifest.Id, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(PluginDiagnostic.Warning(
                        "InvalidBuiltinBackendProvider",
                        $"Plugin tool '{function.Name}' cannot bind to provider '{backend.ProviderId}' because providerId must match plugin id '{plugin.Manifest.Id}'.",
                        plugin.Manifest.Id,
                        function.Name,
                        plugin.Manifest.ManifestPath));
                    continue;
                }

                var key = BackendKey(backend.ProviderId, backend.FunctionName);
                if (!fallbackByBackend.TryGetValue(key, out var fallback))
                {
                    diagnostics.Add(PluginDiagnostic.Warning(
                        "BuiltinBackendUnavailable",
                        $"Plugin tool '{function.Name}' references unavailable built-in backend '{backend.ProviderId}/{backend.FunctionName}'.",
                        plugin.Manifest.Id,
                        function.Name,
                        plugin.Manifest.ManifestPath));
                    continue;
                }

                bound.Add(new PluginFunctionRegistration(
                    function.ToDescriptor(plugin.Manifest.Id),
                    fallback.Invoker));
                boundBackendKeys.Add(key);
            }
        }

        foreach (var fallback in fallbacks)
        {
            var key = BackendKey(fallback.Descriptor.PluginId, fallback.Descriptor.Name);
            if (boundBackendKeys.Contains(key))
                continue;

            if (pluginIdsWithManifest.Contains(fallback.Descriptor.PluginId))
                continue;

            bound.Add(fallback);
        }

        return bound;
    }

    private static string BackendKey(string pluginId, string functionName) =>
        pluginId + "\u001f" + functionName;
}
