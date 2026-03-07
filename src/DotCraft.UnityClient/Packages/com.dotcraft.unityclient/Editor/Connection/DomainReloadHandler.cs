using System;
using DotCraft.Editor.Settings;
using UnityEditor;
using UnityEngine;

namespace DotCraft.Editor.Connection
{
    /// <summary>
    /// Handles Unity Domain Reload events for DotCraft.
    /// Implements the "Kill + session/load" recovery strategy.
    /// </summary>
    [InitializeOnLoad]
    public static class DomainReloadHandler
    {
        private const string SessionIdKey = "DotCraft_SessionId";
        
        private const string WasConnectedKey = "DotCraft_WasConnected";

        static DomainReloadHandler()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Event raised when a session should be restored after Domain Reload.
        /// </summary>
        public static event Action<string> OnRestoreSession;

        /// <summary>
        /// Saves the current session state before Domain Reload.
        /// </summary>
        public static void SaveSessionState(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;

            EditorPrefs.SetString(SessionIdKey, sessionId);
            EditorPrefs.SetBool(WasConnectedKey, true);
            if (DotCraftSettings.Instance.VerboseLogging)
            {
                Debug.Log($"[DotCraft] Saved session state: {sessionId}");
            }
        }

        /// <summary>
        /// Clears the saved session state.
        /// </summary>
        public static void ClearSessionState()
        {
            EditorPrefs.DeleteKey(SessionIdKey);
            EditorPrefs.DeleteKey(WasConnectedKey);
        }

        /// <summary>
        /// Gets the saved session ID.
        /// </summary>
        public static string GetSavedSessionId()
        {
            return EditorPrefs.GetString(SessionIdKey, "");
        }

        /// <summary>
        /// Checks if there was an active session before Domain Reload.
        /// </summary>
        public static bool WasConnected()
        {
            return EditorPrefs.GetBool(WasConnectedKey, false);
        }


        /// <summary>
        /// Handles play mode state changes.
        /// </summary>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    // Save session before entering play mode
                    if (DotCraftSettings.Instance.VerboseLogging)
                    {
                        Debug.Log("[DotCraft] Entering play mode");
                    }

                    break;

                case PlayModeStateChange.EnteredEditMode:
                    // Optionally restore session after returning from play mode
                    if (DotCraftSettings.Instance.AutoReconnect && WasConnected())
                    {
                        var sessionId = GetSavedSessionId();
                        if (!string.IsNullOrEmpty(sessionId))
                        {
                            if (DotCraftSettings.Instance.VerboseLogging)
                            {
                                Debug.Log($"[DotCraft] Play mode ended - session {sessionId} can be restored");
                            }

                            OnRestoreSession?.Invoke(sessionId);
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Requests session restoration. Call this from EditorWindow.OnEnable.
        /// </summary>
        public static void RequestSessionRestore()
        {
            if (!DotCraftSettings.Instance.AutoReconnect) return;
            if (!WasConnected()) return;

            var sessionId = GetSavedSessionId();
            if (!string.IsNullOrEmpty(sessionId))
            {
                EditorApplication.delayCall += () =>
                {
                    OnRestoreSession?.Invoke(sessionId);
                };
            }
        }
    }
}
