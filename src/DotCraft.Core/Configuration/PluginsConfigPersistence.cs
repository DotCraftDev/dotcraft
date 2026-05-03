using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;

namespace DotCraft.Configuration;

public static class PluginsConfigPersistence
{
    public static void WriteWorkspaceDisabledPlugins(string craftPath, IReadOnlyList<string> disabledPlugins)
    {
        var path = Path.Combine(craftPath, "config.json");
        JsonObject root;
        if (File.Exists(path))
        {
            var text = File.ReadAllText(path);
            root = (JsonObject)(JsonNode.Parse(text) ?? new JsonObject());
        }
        else
        {
            root = new JsonObject();
        }

        var pluginsObj = root["Plugins"] as JsonObject ?? new JsonObject();
        var arr = new JsonArray();
        foreach (var pluginId in NormalizeDisabledPluginIds(disabledPlugins))
            arr.Add(pluginId);
        pluginsObj["DisabledPlugins"] = arr;
        root["Plugins"] = pluginsObj;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static IReadOnlyList<string> NormalizeDisabledPluginIds(IEnumerable<string> disabledPlugins)
    {
        var result = new List<string>();
        foreach (var pluginId in disabledPlugins)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
                continue;

            var canonical = PluginIds.Canonicalize(pluginId.Trim());
            if (!result.Contains(canonical, StringComparer.OrdinalIgnoreCase))
                result.Add(canonical);
        }

        return result;
    }
}
