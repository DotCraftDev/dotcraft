using System.Reflection;
using System.Text.Json;

namespace DotCraft.Plugins;

/// <summary>
/// Deploys built-in plugin manifests embedded in the DotCraft assembly.
/// </summary>
public sealed class BuiltInPluginDeployer(string workspacePluginsPath)
{
    private const string ResourcePrefix = "DotCraft.Plugins.BuiltIn.";
    private const string MarkerFile = ".builtin";

    /// <summary>
    /// Deploys embedded built-in plugins into the workspace plugin directory.
    /// </summary>
    public IReadOnlyList<PluginDiagnostic> Deploy(Assembly? resourceAssembly = null)
    {
        var diagnostics = new List<PluginDiagnostic>();
        var assembly = resourceAssembly ?? typeof(BuiltInPluginDeployer).Assembly;
        var currentVersion = assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        var groups = assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name =>
            {
                var remainder = name[ResourcePrefix.Length..];
                var dotIndex = remainder.IndexOf('.');
                if (dotIndex <= 0)
                    return (PluginId: string.Empty, FileName: string.Empty, ResourceName: name);

                return (
                    PluginId: remainder[..dotIndex],
                    FileName: remainder[(dotIndex + 1)..],
                    ResourceName: name);
            })
            .Where(resource => !string.IsNullOrWhiteSpace(resource.PluginId)
                               && !string.IsNullOrWhiteSpace(resource.FileName))
            .GroupBy(resource => resource.PluginId, StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(workspacePluginsPath);
        foreach (var group in groups)
        {
            var pluginId = ReadBuiltInPluginId(assembly, group) ?? group.Key;
            var pluginDir = Path.Combine(workspacePluginsPath, pluginId);
            var markerPath = Path.Combine(pluginDir, MarkerFile);
            if (Directory.Exists(pluginDir) && !File.Exists(markerPath))
            {
                diagnostics.Add(PluginDiagnostic.Info(
                    "BuiltInPluginUserOwned",
                    $"Built-in plugin '{pluginId}' was not deployed because the target directory is user-owned.",
                    pluginId,
                    path: pluginDir));
                continue;
            }

            if (File.Exists(markerPath)
                && string.Equals(File.ReadAllText(markerPath).Trim(), currentVersion, StringComparison.Ordinal))
            {
                continue;
            }

            Directory.CreateDirectory(pluginDir);
            foreach (var resource in group)
            {
                var relativePath = NormalizeBuiltInResourceFileName(resource.FileName);
                using var stream = assembly.GetManifestResourceStream(resource.ResourceName);
                if (stream == null)
                    continue;

                var targetPath = Path.Combine(pluginDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                using var file = File.Create(targetPath);
                stream.CopyTo(file);
            }

            File.WriteAllText(markerPath, currentVersion);
        }

        return diagnostics;
    }

    private static string NormalizeBuiltInResourceFileName(string fileName)
    {
        if (fileName.StartsWith("_craft-plugin.", StringComparison.Ordinal))
            return Path.Combine(".craft-plugin", fileName["_craft-plugin.".Length..]);
        if (fileName.StartsWith("_craft_plugin.", StringComparison.Ordinal))
            return Path.Combine(".craft-plugin", fileName["_craft_plugin.".Length..]);
        if (fileName.StartsWith(".craft-plugin.", StringComparison.Ordinal))
            return Path.Combine(".craft-plugin", fileName[".craft-plugin.".Length..]);
        if (fileName.StartsWith(".craft_plugin.", StringComparison.Ordinal))
            return Path.Combine(".craft-plugin", fileName[".craft_plugin.".Length..]);
        if (fileName.StartsWith("craft-plugin.", StringComparison.Ordinal))
            return Path.Combine(".craft-plugin", fileName["craft-plugin.".Length..]);
        if (fileName.StartsWith("craft_plugin.", StringComparison.Ordinal))
            return Path.Combine(".craft-plugin", fileName["craft_plugin.".Length..]);

        if (fileName.StartsWith("skills.", StringComparison.Ordinal))
        {
            var remainder = fileName["skills.".Length..];
            var dotIndex = remainder.IndexOf('.');
            if (dotIndex > 0)
            {
                var skillName = remainder[..dotIndex] switch
                {
                    "browser_use" => "browser-use",
                    var name => name
                };
                var skillFileName = remainder[(dotIndex + 1)..];
                if (skillFileName.StartsWith("agents.", StringComparison.Ordinal))
                    return Path.Combine("skills", skillName, "agents", skillFileName["agents.".Length..]);
                if (skillFileName.StartsWith("assets.", StringComparison.Ordinal))
                    return Path.Combine("skills", skillName, "assets", skillFileName["assets.".Length..]);
                return Path.Combine("skills", skillName, skillFileName);
            }
        }

        return fileName;
    }

    private static string? ReadBuiltInPluginId(
        Assembly assembly,
        IEnumerable<(string PluginId, string FileName, string ResourceName)> resources)
    {
        var manifestResource = resources.FirstOrDefault(
            resource => string.Equals(
                NormalizeBuiltInResourceFileName(resource.FileName),
                Path.Combine(".craft-plugin", "plugin.json"),
                StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(manifestResource.ResourceName))
            return null;

        using var stream = assembly.GetManifestResourceStream(manifestResource.ResourceName);
        if (stream == null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
