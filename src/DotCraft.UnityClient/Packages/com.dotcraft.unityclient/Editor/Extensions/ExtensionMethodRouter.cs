using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace DotCraft.Editor.Extensions
{
    /// <summary>
    /// Routes ACP extension method requests (_unity/*) to registered handlers.
    /// Handlers are executed on the main thread via MainThreadDispatcher.
    /// </summary>
    public sealed class ExtensionMethodRouter
    {
        private readonly ConcurrentDictionary<string, Func<JsonElement, Task<object>>> _handlers = new();

        public ExtensionMethodRouter()
        {
            RegisterBuiltinHandlers();
        }

        /// <summary>
        /// Registers a handler for an extension method.
        /// </summary>
        public void RegisterHandler(string method, Func<JsonElement, Task<object>> handler)
        {
            _handlers[method] = handler;
        }

        /// <summary>
        /// Registers a synchronous handler for an extension method.
        /// </summary>
        public void RegisterHandler(string method, Func<JsonElement, object> handler)
        {
            _handlers[method] = paramsJson => Task.FromResult(handler(paramsJson));
        }

        /// <summary>
        /// Handles an extension method request.
        /// </summary>
        public async Task<object> HandleAsync(string method, JsonElement paramsJson)
        {
            if (!_handlers.TryGetValue(method, out var handler))
            {
                return new { error = $"Method not found: {method}" };
            }

            try
            {
                // Execute on main thread
                return await MainThreadDispatcher.RunOnMainThread(() => handler(paramsJson));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotCraft] Extension method error ({method}): {ex.Message}");
                return new { error = ex.Message };
            }
        }

        /// <summary>
        /// Checks if a handler is registered for the given method.
        /// </summary>
        public bool HasHandler(string method) => _handlers.ContainsKey(method);

        private void RegisterBuiltinHandlers()
        {
            // Scene handlers
            RegisterHandler("_unity/scene_query", UnitySceneHandlers.HandleSceneQuery);
            RegisterHandler("_unity/get_selection", UnitySceneHandlers.HandleGetSelection);
            RegisterHandler("_unity/set_selection", UnitySceneHandlers.HandleSetSelection);
            RegisterHandler("_unity/create_gameobject", UnitySceneHandlers.HandleCreateGameObject);
            RegisterHandler("_unity/modify_component", UnitySceneHandlers.HandleModifyComponent);
            RegisterHandler("_unity/delete_gameobject", UnitySceneHandlers.HandleDeleteGameObject);

            // Console handlers
            RegisterHandler("_unity/get_console_logs", UnityEditorHandlers.HandleGetConsoleLogs);

            // Editor handlers
            RegisterHandler("_unity/execute_menu_item", UnityEditorHandlers.HandleExecuteMenuItem);

            // Asset handlers
            RegisterHandler("_unity/get_asset_info", UnityAssetHandlers.HandleGetAssetInfo);
            RegisterHandler("_unity/import_asset", UnityAssetHandlers.HandleImportAsset);
            RegisterHandler("_unity/find_assets", UnityAssetHandlers.HandleFindAssets);

            // Project handlers
            RegisterHandler("_unity/get_project_info", UnityProjectHandlers.HandleGetProjectInfo);
        }
    }

    #region Scene Handlers

    public static class UnitySceneHandlers
    {
        public static Task<object> HandleSceneQuery(JsonElement paramsJson)
        {
            var query = paramsJson.TryGetProperty("query", out var q) ? q.GetString() : null;
            var includeComponents = paramsJson.TryGetProperty("includeComponents", out var ic) && ic.GetBoolean();
            var maxDepth = paramsJson.TryGetProperty("maxDepth", out var md) ? md.GetInt32() : 10;

            var results = new List<object>();

            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    var info = GetGameObjectInfo(root, "", includeComponents, maxDepth);
                    if (string.IsNullOrEmpty(query) ||
                        info.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        info.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(info);
                    }
                    else
                    {
                        // Search children
                        SearchChildren(root.transform, query, includeComponents, maxDepth, results);
                    }
                }
            }

            return Task.FromResult<object>(new { objects = results });
        }

        private static void SearchChildren(
            Transform parent,
            string query,
            bool includeComponents,
            int maxDepth,
            List<object> results,
            int depth = 0)
        {
            if (depth >= maxDepth) return;

            foreach (Transform child in parent)
            {
                var info = GetGameObjectInfo(child.gameObject, "", includeComponents, maxDepth - depth);
                if (info.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    info.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(info);
                }
                SearchChildren(child, query, includeComponents, maxDepth, results, depth + 1);
            }
        }

        private static GameObjectInfo GetGameObjectInfo(
            GameObject go,
            string parentPath,
            bool includeComponents,
            int maxDepth,
            int depth = 0)
        {
            var path = string.IsNullOrEmpty(parentPath) ? $"/{go.name}" : $"{parentPath}/{go.name}";

            var info = new GameObjectInfo
            {
                Name = go.name,
                Path = path,
                InstanceId = go.GetInstanceID(),
                Active = go.activeSelf
            };

            if (includeComponents)
            {
                var components = new List<string>();
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp != null)
                    {
                        components.Add(comp.GetType().Name);
                    }
                }
                info.Components = components;
            }

            if (depth < maxDepth)
            {
                var children = new List<GameObjectInfo>();
                foreach (Transform child in go.transform)
                {
                    children.Add(GetGameObjectInfo(child.gameObject, path, includeComponents, maxDepth, depth + 1));
                }
                info.Children = children;
            }

            return info;
        }

        public static Task<object> HandleGetSelection(JsonElement _)
        {
            var selected = Selection.gameObjects;
            var results = new List<GameObjectInfo>();

            foreach (var go in selected)
            {
                results.Add(GetGameObjectInfo(go, "", true, 0));
            }

            return Task.FromResult<object>(new { selectedObjects = results });
        }

        public static Task<object> HandleSetSelection(JsonElement paramsJson)
        {
            var paths = paramsJson.TryGetProperty("objectPaths", out var p)
                ? p.Deserialize<string[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : Array.Empty<string>();

            var objects = new List<GameObject>();

            foreach (var path in paths)
            {
                var go = FindGameObjectByPath(path);
                if (go != null)
                {
                    objects.Add(go);
                }
            }

            Selection.objects = objects.ToArray();

            return Task.FromResult<object>(new { success = true });
        }

        public static Task<object> HandleCreateGameObject(JsonElement paramsJson)
        {
            var name = paramsJson.TryGetProperty("name", out var n) ? n.GetString() : "New GameObject";
            var parentPath = paramsJson.TryGetProperty("parentPath", out var pp) ? pp.GetString() : null;
            var components = paramsJson.TryGetProperty("components", out var c)
                ? c.Deserialize<string[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null;

            var go = new GameObject(name);

            // Add components
            if (components != null)
            {
                foreach (var compName in components)
                {
                    var compType = Type.GetType(compName) ??
                                   Type.GetType($"UnityEngine.{compName}, UnityEngine");
                    if (compType != null)
                    {
                        go.AddComponent(compType);
                    }
                }
            }

            // Set parent
            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = FindGameObjectByPath(parentPath);
                if (parent != null)
                {
                    go.transform.SetParent(parent.transform);
                }
            }

            // Set position
            if (paramsJson.TryGetProperty("position", out var pos))
            {
                var posArr = pos.Deserialize<float[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (posArr != null && posArr.Length >= 3)
                {
                    go.transform.position = new Vector3(posArr[0], posArr[1], posArr[2]);
                }
            }

            return Task.FromResult<object>(new
            {
                instanceId = go.GetInstanceID(),
                path = GetGameObjectPath(go)
            });
        }

        public static Task<object> HandleModifyComponent(JsonElement paramsJson)
        {
            var objectPath = paramsJson.TryGetProperty("objectPath", out var op) ? op.GetString() : "";
            var componentType = paramsJson.TryGetProperty("componentType", out var ct) ? ct.GetString() : "";
            var properties = paramsJson.TryGetProperty("properties", out var props)
                ? props.Deserialize<Dictionary<string, object>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : new Dictionary<string, object>();

            var go = FindGameObjectByPath(objectPath);
            if (go == null)
            {
                return Task.FromResult<object>(new { success = false, error = "GameObject not found" });
            }

            var compType = Type.GetType(componentType) ??
                          Type.GetType($"UnityEngine.{componentType}, UnityEngine");

            if (compType == null)
            {
                return Task.FromResult<object>(new { success = false, error = "Component type not found" });
            }

            var component = go.GetComponent(compType);
            if (component == null)
            {
                return Task.FromResult<object>(new { success = false, error = "Component not found on object" });
            }

            var modifiedProps = new List<string>();

            // Use SerializedObject for property modification
            using (var so = new SerializedObject(component))
            {
                foreach (var kvp in properties)
                {
                    var prop = so.FindProperty(kvp.Key);
                    if (prop == null) continue;

                    var value = kvp.Value;

                    switch (prop.propertyType)
                    {
                        case SerializedPropertyType.Float:
                            prop.floatValue = Convert.ToSingle(value);
                            modifiedProps.Add(kvp.Key);
                            break;
                        case SerializedPropertyType.Integer:
                            prop.intValue = Convert.ToInt32(value);
                            modifiedProps.Add(kvp.Key);
                            break;
                        case SerializedPropertyType.Boolean:
                            prop.boolValue = Convert.ToBoolean(value);
                            modifiedProps.Add(kvp.Key);
                            break;
                        case SerializedPropertyType.String:
                            prop.stringValue = value?.ToString() ?? "";
                            modifiedProps.Add(kvp.Key);
                            break;
                    }
                }

                so.ApplyModifiedProperties();
            }

            return Task.FromResult<object>(new { success = true, modifiedProperties = modifiedProps });
        }

        public static Task<object> HandleDeleteGameObject(JsonElement paramsJson)
        {
            var objectPath = paramsJson.TryGetProperty("objectPath", out var op) ? op.GetString() : "";

            var go = FindGameObjectByPath(objectPath);
            if (go == null)
            {
                return Task.FromResult<object>(new { success = false, error = "GameObject not found" });
            }

            UnityEngine.Object.DestroyImmediate(go);

            return Task.FromResult<object>(new { success = true });
        }

        private static GameObject FindGameObjectByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // Path format: "/Parent/Child/Grandchild"
            var parts = path.Trim('/').Split('/');

            GameObject found = null;

            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name == parts[0])
                    {
                        found = root;
                        break;
                    }
                }

                if (found != null) break;
            }

            if (found == null) return null;

            // Traverse path
            for (int i = 1; i < parts.Length && found != null; i++)
            {
                var child = found.transform.Find(parts[i]);
                found = child?.gameObject;
            }

            return found;
        }

        private static string GetGameObjectPath(GameObject go)
        {
            var path = go.name;
            var current = go.transform.parent;

            while (current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }

            return $"/{path}";
        }

        private class GameObjectInfo
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public int InstanceId { get; set; }
            public bool Active { get; set; }
            public List<string> Components { get; set; } = new();
            public List<GameObjectInfo> Children { get; set; } = new();
        }
    }

    #endregion

    #region Editor Handlers

    /// <summary>
    /// Collects Unity console log entries via Application.logMessageReceived.
    /// Thread-safe and capacity-limited.
    /// </summary>
    [InitializeOnLoad]
    public static class UnityConsoleLogCollector
    {
        private static readonly object _lock = new();
        private static readonly List<ConsoleLogEntry> _logs = new();
        private const int MaxLogEntries = 2000;

        static UnityConsoleLogCollector()
        {
            Application.logMessageReceived += OnLogMessageReceived;
        }

        private static void OnLogMessageReceived(string message, string stackTrace, LogType type)
        {
            var entry = new ConsoleLogEntry
            {
                Type = type switch
                {
                    LogType.Error => "error",
                    LogType.Assert => "error",
                    LogType.Exception => "error",
                    LogType.Warning => "warning",
                    _ => "log"
                },
                Message = message,
                StackTrace = stackTrace,
                Timestamp = DateTime.UtcNow.ToString("o")
            };

            lock (_lock)
            {
                _logs.Add(entry);
                // Trim oldest entries if over capacity
                while (_logs.Count > MaxLogEntries)
                {
                    _logs.RemoveAt(0);
                }
            }
        }

        public static List<ConsoleLogEntry> GetLogs(string[] types, int limit)
        {
            lock (_lock)
            {
                IEnumerable<ConsoleLogEntry> filtered = _logs;

                if (types != null && types.Length > 0)
                {
                    var typeSet = new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);
                    filtered = filtered.Where(e => typeSet.Contains(e.Type));
                }

                return filtered.TakeLast(limit).ToList();
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _logs.Clear();
            }
        }

        public class ConsoleLogEntry
        {
            public string Type { get; set; } = "";
            public string Message { get; set; } = "";
            public string StackTrace { get; set; }
            public string Timestamp { get; set; } = "";
        }
    }

    public static class UnityEditorHandlers
    {
        public static Task<object> HandleGetConsoleLogs(JsonElement paramsJson)
        {
            var types = paramsJson.TryGetProperty("types", out var t)
                ? t.Deserialize<string[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null;

            var limit = paramsJson.TryGetProperty("limit", out var l) ? l.GetInt32() : 50;

            var logs = UnityConsoleLogCollector.GetLogs(types, limit);

            return Task.FromResult<object>(new { logs });
        }

        public static Task<object> HandleExecuteMenuItem(JsonElement paramsJson)
        {
            var menuPath = paramsJson.TryGetProperty("menuPath", out var mp) ? mp.GetString() : "";

            try
            {
                EditorApplication.ExecuteMenuItem(menuPath);
                return Task.FromResult<object>(new { success = true });
            }
            catch (Exception ex)
            {
                return Task.FromResult<object>(new { success = false, error = ex.Message });
            }
        }
    }

    #endregion

    #region Asset Handlers

    public static class UnityAssetHandlers
    {
        public static Task<object> HandleGetAssetInfo(JsonElement paramsJson)
        {
            var assetPath = paramsJson.TryGetProperty("assetPath", out var ap) ? ap.GetString() : "";

            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
            {
                return Task.FromResult<object>(new { error = "Asset not found" });
            }

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
            {
                return Task.FromResult<object>(new { error = "Failed to load asset" });
            }

            var info = new
            {
                path = assetPath,
                name = asset.name,
                type = asset.GetType().Name,
                size = new FileInfo(assetPath).Length
            };

            return Task.FromResult<object>(info);
        }

        public static Task<object> HandleImportAsset(JsonElement paramsJson)
        {
            var assetPath = paramsJson.TryGetProperty("assetPath", out var ap) ? ap.GetString() : "";

            try
            {
                AssetDatabase.ImportAsset(assetPath);
                return Task.FromResult<object>(new { success = true });
            }
            catch (Exception ex)
            {
                return Task.FromResult<object>(new { success = false, error = ex.Message });
            }
        }

        public static Task<object> HandleFindAssets(JsonElement paramsJson)
        {
            var filter = paramsJson.TryGetProperty("filter", out var f) ? f.GetString() : "";
            var searchInFolders = paramsJson.TryGetProperty("searchInFolders", out var sif)
                ? sif.Deserialize<string[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null;

            var guids = searchInFolders != null && searchInFolders.Length > 0
                ? AssetDatabase.FindAssets(filter, searchInFolders)
                : AssetDatabase.FindAssets(filter);

            var assets = new List<object>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);

                assets.Add(new
                {
                    guid,
                    path,
                    name = asset?.name ?? Path.GetFileNameWithoutExtension(path),
                    type = asset?.GetType().Name ?? "Unknown"
                });
            }

            return Task.FromResult<object>(new { assets });
        }
    }

    #endregion

    #region Project Handlers

    public static class UnityProjectHandlers
    {
        public static Task<object> HandleGetProjectInfo(JsonElement _)
        {
            var info = new
            {
                projectName = PlayerSettings.productName,
                unityVersion = Application.unityVersion,
                projectPath = Application.dataPath,
                packages = GetInstalledPackages()
            };

            return Task.FromResult<object>(info);
        }

        private static List<string> GetInstalledPackages()
        {
            var packages = new List<string>();

            try
            {
                var manifestPath = Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName,
                    "Packages",
                    "manifest.json"
                );

                if (File.Exists(manifestPath))
                {
                    var json = File.ReadAllText(manifestPath);
                    var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("dependencies", out var deps))
                    {
                        foreach (var prop in deps.EnumerateObject())
                        {
                            packages.Add(prop.Name);
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return packages;
        }
    }

    #endregion
}
