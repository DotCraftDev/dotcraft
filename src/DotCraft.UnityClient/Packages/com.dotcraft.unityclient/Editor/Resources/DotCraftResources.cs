using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotCraft.Editor
{
    /// <summary>
    /// Loads resources for the DotCraft UI.
    /// </summary>
    public static class DotCraftResources
    {
        private const string BasePath = "Packages/com.dotcraft.unityclient/Editor/";

        /// <summary>
        /// Loads a VisualTreeAsset from the UXML folder.
        /// </summary>
        public static VisualTreeAsset LoadUxml(string name)
        {
            var path = $"{BasePath}Window/UXML/{name}.uxml";
            return AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
        }

        /// <summary>
        /// Loads a StyleSheet from the Styles folder.
        /// </summary>
        public static StyleSheet LoadStyleSheet(string name)
        {
            var path = $"{BasePath}Window/Styles/{name}.uss";
            return AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
        }

        /// <summary>
        /// Loads all required stylesheets.
        /// </summary>
        public static StyleSheet[] LoadAllStyleSheets()
        {
            return new[]
            {
                LoadStyleSheet("DotCraftStyles"),
                LoadStyleSheet("ChatPanel"),
                LoadStyleSheet("ApprovalPanel")
            };
        }
    }
}
