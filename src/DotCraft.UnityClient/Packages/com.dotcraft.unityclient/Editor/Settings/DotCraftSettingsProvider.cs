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
    }
}
