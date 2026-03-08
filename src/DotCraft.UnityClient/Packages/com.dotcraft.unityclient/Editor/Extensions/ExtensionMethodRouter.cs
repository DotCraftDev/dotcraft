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
    /// Only read-only handlers are provided. For full Unity manipulation capabilities,
    /// install SkillsForUnity package.
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
            // Scene handlers (read-only)
            RegisterHandler("_unity/scene_query", UnitySceneHandlers.HandleSceneQuery);
            RegisterHandler("_unity/get_selection", UnitySceneHandlers.HandleGetSelection);

            // Console handlers (read-only)
            RegisterHandler("_unity/get_console_logs", UnityEditorHandlers.HandleGetConsoleLogs);

            // Project handlers (read-only)
            RegisterHandler("_unity/get_project_info", UnityProjectHandlers.HandleGetProjectInfo);
        }
    }

    #region Scene Handlers

    /// <summary>
    /// Handles read-only scene query operations.
    /// </summary>
    public static class UnitySceneHandlers
    {
        /// <summary>
        /// Queries the Unity scene hierarchy and returns GameObject information.
        /// </summary>
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

        /// <summary>
        /// Gets the currently selected objects in the Unity Editor.
        /// </summary>
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

    /// <summary>
    /// Handles read-only Unity Editor operations.
    /// </summary>
    public static class UnityEditorHandlers
    {
        /// <summary>
        /// Gets recent Unity console log entries.
        /// </summary>
        public static Task<object> HandleGetConsoleLogs(JsonElement paramsJson)
        {
            var types = paramsJson.TryGetProperty("types", out var t)
                ? t.Deserialize<string[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null;

            var limit = paramsJson.TryGetProperty("limit", out var l) ? l.GetInt32() : 50;

            var logs = UnityConsoleLogCollector.GetLogs(types, limit);

            return Task.FromResult<object>(new { logs });
        }
    }

    #endregion

    #region Project Handlers

    /// <summary>
    /// Handles read-only Unity project information queries.
    /// </summary>
    public static class UnityProjectHandlers
    {
        /// <summary>
        /// Gets Unity project information including version and installed packages.
        /// </summary>
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
