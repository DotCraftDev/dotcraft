using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using DotCraft.Editor.Protocol;
using UObject = UnityEngine.Object;

namespace DotCraft.Editor
{
    /// <summary>
    /// Handles context attachment for assets and scene objects.
    /// </summary>
    public static class ContextAttachment
    {
        /// <summary>
        /// Creates an ACP content block for the given Unity object.
        /// </summary>
        public static AcpContentBlock CreateResourceBlock(UObject obj)
        {
            if (!obj) return null;

            var assetPath = AssetDatabase.GetAssetPath(obj);
            string uri;
            string mimeType;

            if (string.IsNullOrEmpty(assetPath))
            {
                // Scene object
                var gameObject = obj as GameObject;
                if (gameObject != null && gameObject.scene.IsValid())
                {
                    var scenePath = gameObject.scene.path;
                    var instanceId = gameObject.GetInstanceID();
                    uri = new Uri($"file://{Path.GetFullPath(scenePath)}?instanceID={instanceId}").AbsoluteUri;
                    mimeType = "application/x-unity-gameobject";
                }
                else
                {
                    return null;
                }
            }
            else
            {
                // Asset
                var fullPath = Path.GetFullPath(assetPath);
                uri = new Uri(fullPath).AbsoluteUri;
                mimeType = GetMimeType(assetPath);
            }

            return new AcpContentBlock
            {
                Type = "resource",
                Resource = new AcpEmbeddedResource
                {
                    Uri = uri,
                    MimeType = mimeType,
                    Text = GetAssetSummary(obj, assetPath)
                }
            };
        }

        private static string GetMimeType(string assetPath)
        {
            var extension = Path.GetExtension(assetPath).ToLowerInvariant();

            return extension switch
            {
                ".cs" => "text/x-csharp",
                ".js" => "text/javascript",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                ".prefab" => "application/x-unity-prefab",
                ".unity" => "application/x-unity-scene",
                ".mat" => "application/x-unity-material",
                ".asset" => "application/x-unity-asset",
                ".fbx" or ".obj" or ".gltf" or ".glb" => "model/gltf+json",
                ".png" or ".jpg" or ".jpeg" => "image/png",
                ".wav" or ".mp3" or ".ogg" => "audio/wav",
                ".shader" => "text/x-shader",
                ".compute" => "text/x-compute-shader",
                _ => "application/octet-stream"
            };
        }

        private static string GetAssetSummary(UObject obj, string assetPath)
        {
            if (obj is GameObject go)
            {
                var components = go.GetComponents<Component>();
                var componentNames = new System.Text.StringBuilder();

                foreach (var comp in components)
                {
                    if (comp)
                    {
                        componentNames.Append(comp.GetType().Name);
                        componentNames.Append(", ");
                    }
                }

                return $"GameObject: {go.name}\nComponents: {componentNames.ToString().TrimEnd(',', ' ')}";
            }

            if (obj is MonoScript script)
            {
                var type = script.GetClass();
                return type != null
                    ? $"MonoScript: {type.FullName}"
                    : $"MonoScript: {script.name}";
            }

            if (obj is Material mat)
            {
                return $"Material: {mat.name}\nShader: {mat.shader?.name}";
            }

            if (!string.IsNullOrEmpty(assetPath))
            {
                return $"Asset: {obj.name} ({assetPath})";
            }

            return obj.name;
        }
    }
}
