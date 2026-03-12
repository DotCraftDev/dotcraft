using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotCraft.Editor.Settings
{
    /// <summary>
    /// Settings provider for DotCraft configuration in Project Settings window.
    /// Uses UIElements for a modern settings UI.
    /// </summary>
    public sealed class DotCraftSettingsProvider : SettingsProvider
    {
        private const string SettingsPath = "Project/DotCraft";
        private DotCraftSettings _settings;
        private VisualElement _rootElement;

        private SerializedObject _serializedObject;
        private SerializedProperty _dotCraftCommand;
        private SerializedProperty _dotCraftArguments;
        private SerializedProperty _workspacePath;
        private SerializedProperty _autoReconnect;
        private SerializedProperty _verboseLogging;
        private SerializedProperty _requestTimeoutSeconds;
        private SerializedProperty _maxHistoryMessages;

        // Per-server foldout open/closed state (index matches McpServers list)
        private readonly List<bool> _mcpServerFoldouts = new();

        private static readonly string[] TransportOptions = { "stdio", "http" };

        public DotCraftSettingsProvider(string path, SettingsScope scope = SettingsScope.Project)
            : base(path, scope)
        {
            label = "DotCraft";
            keywords = new HashSet<string>(new[] { "DotCraft", "AI", "Agent", "ACP" });
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new DotCraftSettingsProvider(SettingsPath, SettingsScope.Project);
            return provider;
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            _settings = DotCraftSettings.Instance;
            _rootElement = rootElement;
            base.OnActivate(searchContext, rootElement);
        }

        public override void OnDeactivate()
        {
            _settings?.Save();
            base.OnDeactivate();
        }

        public override void OnGUI(string searchContext)
        {
            // Using IMGUI fallback for compatibility
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.Space(10);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("DotCraft Configuration", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                EditorGUILayout.LabelField("Connection Settings", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;

                _settings.DotCraftCommand = EditorGUILayout.TextField(
                    new GUIContent("Command", "The command to execute DotCraft"),
                    _settings.DotCraftCommand);

                _settings.DotCraftArguments = EditorGUILayout.TextField(
                    new GUIContent("Arguments", "Arguments passed to DotCraft"),
                    _settings.DotCraftArguments);

                _settings.WorkspacePath = EditorGUILayout.TextField(
                    new GUIContent("Workspace Path", "Working directory for DotCraft (empty = Unity project root)"),
                    _settings.WorkspacePath);

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(10);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Environment Variables", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Add environment variables like API keys. These will be injected into the DotCraft process.",
                    MessageType.Info);

                EditorGUI.indentLevel++;

                var keys = new List<string>(_settings.EnvironmentVariables.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    var key = EditorGUILayout.TextField(keys[i], GUILayout.Width(150));
                    var value = EditorGUILayout.TextField(_settings.EnvironmentVariables[keys[i]]);

                    if (key != keys[i])
                    {
                        _settings.EnvironmentVariables.Remove(keys[i]);
                        if (!string.IsNullOrEmpty(key))
                        {
                            _settings.EnvironmentVariables[key] = value;
                        }
                    }
                    else
                    {
                        _settings.EnvironmentVariables[keys[i]] = value;
                    }

                    if (GUILayout.Button("×", GUILayout.Width(25)))
                    {
                        _settings.EnvironmentVariables.Remove(keys[i]);
                        GUIUtility.ExitGUI();
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15);
                if (GUILayout.Button("+ Add Variable", GUILayout.Width(120)))
                {
                    _settings.EnvironmentVariables["NEW_KEY"] = "";
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(10);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("General Settings", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;

                _settings.AutoReconnect = EditorGUILayout.Toggle(
                    new GUIContent("Auto Reconnect", "Automatically reconnect after Domain Reload"),
                    _settings.AutoReconnect);

                _settings.VerboseLogging = EditorGUILayout.Toggle(
                    new GUIContent("Verbose Logging", "Enable detailed logging for debugging"),
                    _settings.VerboseLogging);

                _settings.RequestTimeoutSeconds = EditorGUILayout.IntSlider(
                    new GUIContent("Request Timeout (s)", "Timeout for ACP requests in seconds"),
                    _settings.RequestTimeoutSeconds, 5, 120);

                _settings.MaxHistoryMessages = EditorGUILayout.IntField(
                    new GUIContent("Max History Messages", "Maximum number of messages to keep in history"),
                    _settings.MaxHistoryMessages);

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(10);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Unity Tools", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;

                _settings.EnableBuiltinUnityTools = EditorGUILayout.Toggle(
                    new GUIContent("Enable Builtin Tools",
                        "Enable built-in _unity/* extension methods for reading Unity state. " +
                        "Disable if using external Unity integration."),
                    _settings.EnableBuiltinUnityTools);

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(10);

            DrawMcpServersSection();

            EditorGUILayout.Space(10);

            // General settings validation
            var errors = _settings.Validate();
            if (errors.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n", errors), MessageType.Warning);
            }

            // Workspace / .craft directory validation
            if (!_settings.ValidateWorkspace(out var workspaceError))
            {
                EditorGUILayout.HelpBox(workspaceError, MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Workspace OK: \"{_settings.EffectiveWorkspacePath}\" contains a .craft directory.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(10);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset to Defaults", GUILayout.Width(120)))
                {
                    _settings.ResetToDefaults();
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Save", GUILayout.Width(80)))
                {
                    _settings.Save();
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                _settings.Save();
            }
        }

        private void DrawMcpServersSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("MCP Servers", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "MCP servers defined here are injected into every new DotCraft session via the ACP " +
                    "mcpServers field, supplementing any servers in .craft/config.json.",
                    MessageType.Info);

                // Ensure foldout list is in sync with server list length
                while (_mcpServerFoldouts.Count < _settings.McpServers.Count)
                    _mcpServerFoldouts.Add(true);
                while (_mcpServerFoldouts.Count > _settings.McpServers.Count)
                    _mcpServerFoldouts.RemoveAt(_mcpServerFoldouts.Count - 1);

                int removeIndex = -1;

                for (int i = 0; i < _settings.McpServers.Count; i++)
                {
                    var server = _settings.McpServers[i];
                    EditorGUILayout.Space(4);

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        // Header row: foldout + enabled toggle + remove button
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var label = string.IsNullOrWhiteSpace(server.Name) ? $"Server {i + 1}" : server.Name;
                            _mcpServerFoldouts[i] = EditorGUILayout.Foldout(_mcpServerFoldouts[i], label, true, EditorStyles.foldoutHeader);

                            GUILayout.FlexibleSpace();

                            server.Enabled = EditorGUILayout.ToggleLeft(
                                "Enabled", server.Enabled, GUILayout.Width(70));

                            if (GUILayout.Button("Remove", GUILayout.Width(65)))
                                removeIndex = i;
                        }

                        if (!_mcpServerFoldouts[i])
                            continue;

                        EditorGUI.indentLevel++;

                        server.Name = EditorGUILayout.TextField(
                            new GUIContent("Name", "Unique name for this MCP server"),
                            server.Name);

                        // Transport popup
                        var transportIndex = server.Transport == "http" ? 1 : 0;
                        var newTransportIndex = EditorGUILayout.Popup(
                            new GUIContent("Transport", "Communication transport: stdio or http"),
                            transportIndex, TransportOptions);
                        server.Transport = TransportOptions[newTransportIndex];

                        if (server.Transport == "stdio")
                        {
                            DrawStdioFields(server);
                        }
                        else
                        {
                            DrawHttpFields(server);
                        }

                        EditorGUI.indentLevel--;
                    }
                }

                // Remove outside the loop to avoid modifying the list mid-iteration
                if (removeIndex >= 0)
                {
                    _settings.McpServers.RemoveAt(removeIndex);
                    _mcpServerFoldouts.RemoveAt(removeIndex);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15);

                    if (GUILayout.Button("+ Add stdio Server", GUILayout.Width(140)))
                    {
                        _settings.McpServers.Add(new McpServerEntry { Transport = "stdio" });
                        _mcpServerFoldouts.Add(true);
                    }

                    if (GUILayout.Button("+ Add http Server", GUILayout.Width(130)))
                    {
                        _settings.McpServers.Add(new McpServerEntry { Transport = "http" });
                        _mcpServerFoldouts.Add(true);
                    }
                }
            }
        }

        private static void DrawStdioFields(McpServerEntry server)
        {
            server.Command = EditorGUILayout.TextField(
                new GUIContent("Command", "Executable to launch (e.g. npx, node, python)"),
                server.Command ?? "");

            // Arguments — one per line in a text area, displayed joined
            EditorGUILayout.LabelField(new GUIContent("Arguments", "One argument per line"));
            server.Arguments ??= new List<string>();
            var argsText = string.Join("\n", server.Arguments);
            var newArgsText = EditorGUILayout.TextArea(argsText, GUILayout.MinHeight(40));
            if (newArgsText != argsText)
            {
                server.Arguments = new List<string>(
                    newArgsText.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }

            // Environment variables
            EditorGUILayout.LabelField("Environment Variables", EditorStyles.boldLabel);
            server.EnvironmentVariables ??= new Dictionary<string, string>();

            var envKeys = new List<string>(server.EnvironmentVariables.Keys);
            for (int j = 0; j < envKeys.Count; j++)
            {
                EditorGUILayout.BeginHorizontal();
                var envKey = EditorGUILayout.TextField(envKeys[j], GUILayout.Width(150));
                var envVal = EditorGUILayout.TextField(server.EnvironmentVariables[envKeys[j]]);

                if (envKey != envKeys[j])
                {
                    server.EnvironmentVariables.Remove(envKeys[j]);
                    if (!string.IsNullOrEmpty(envKey))
                        server.EnvironmentVariables[envKey] = envVal;
                }
                else
                {
                    server.EnvironmentVariables[envKeys[j]] = envVal;
                }

                if (GUILayout.Button("×", GUILayout.Width(25)))
                {
                    server.EnvironmentVariables.Remove(envKeys[j]);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15);
            if (GUILayout.Button("+ Add Env Var", GUILayout.Width(110)))
                server.EnvironmentVariables["NEW_KEY"] = "";
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawHttpFields(McpServerEntry server)
        {
            server.Url = EditorGUILayout.TextField(
                new GUIContent("URL", "HTTP endpoint for the MCP server (e.g. https://mcp.example.com/mcp)"),
                server.Url ?? "");

            // Headers
            EditorGUILayout.LabelField("Headers", EditorStyles.boldLabel);
            server.Headers ??= new Dictionary<string, string>();

            var headerKeys = new List<string>(server.Headers.Keys);
            for (int j = 0; j < headerKeys.Count; j++)
            {
                EditorGUILayout.BeginHorizontal();
                var hKey = EditorGUILayout.TextField(headerKeys[j], GUILayout.Width(150));
                var hVal = EditorGUILayout.TextField(server.Headers[headerKeys[j]]);

                if (hKey != headerKeys[j])
                {
                    server.Headers.Remove(headerKeys[j]);
                    if (!string.IsNullOrEmpty(hKey))
                        server.Headers[hKey] = hVal;
                }
                else
                {
                    server.Headers[headerKeys[j]] = hVal;
                }

                if (GUILayout.Button("×", GUILayout.Width(25)))
                {
                    server.Headers.Remove(headerKeys[j]);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15);
            if (GUILayout.Button("+ Add Header", GUILayout.Width(100)))
                server.Headers["Authorization"] = "Bearer ";
            EditorGUILayout.EndHorizontal();
        }
    }
}
