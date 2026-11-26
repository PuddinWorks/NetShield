using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NetShield
{
    [InitializeOnLoad]
    internal static class NetShieldEditorAssets
    {
        static string root;
        static Texture2D icon;
        static bool queued;

        static NetShieldEditorAssets()
        {
            EditorApplication.delayCall += ConfigureAssets;
            EditorApplication.hierarchyChanged += QueueSceneIcons;
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(root)) return root;
                foreach (string guid in AssetDatabase.FindAssets("NetShieldEditorAssets t:MonoScript"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith("/Editor/NetShieldEditorAssets.cs", StringComparison.Ordinal)) continue;
                    root = Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace('\\', '/');
                    return root;
                }
                return null;
            }
        }

        static Texture2D Icon
        {
            get
            {
                if (icon == null && Root != null) icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Editor/Art/NetShieldIcon.png");
                return icon;
            }
        }

        static void ConfigureAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Root == null) return;
            foreach (string name in new[] { "NetShieldIcon", "NetShieldLogo", "NetShieldLogoDark" })
            {
                string path = Root + "/Editor/Art/" + name + ".png";
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                if (ConfigureTexture(importer)) importer.SaveAndReimport();
            }
            icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Editor/Art/NetShieldIcon.png");
            if (icon == null) return;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { Root + "/Runtime", Root + "/Editor" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!(AssetImporter.GetAtPath(path) is MonoImporter importer) || importer.GetIcon() == icon) continue;
                    importer.SetIcon(icon);
                    importer.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            QueueSceneIcons();
        }

        internal static bool ConfigureTexture(TextureImporter importer)
        {
            var platform = importer.GetDefaultPlatformTextureSettings();
            bool changed = importer.textureType != TextureImporterType.GUI || importer.textureShape != TextureImporterShape.Texture2D ||
                importer.wrapMode != TextureWrapMode.Clamp || importer.filterMode != FilterMode.Bilinear || importer.anisoLevel != 1 ||
                importer.mipmapEnabled || importer.npotScale != TextureImporterNPOTScale.None || !importer.alphaIsTransparency ||
                importer.alphaSource != TextureImporterAlphaSource.FromInput || platform.maxTextureSize != 512 ||
                platform.resizeAlgorithm != TextureResizeAlgorithm.Mitchell || platform.format != TextureImporterFormat.Automatic ||
                platform.textureCompression != TextureImporterCompression.Compressed || platform.crunchedCompression;
            if (!changed) return false;
            importer.textureType = TextureImporterType.GUI;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 1;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            platform.maxTextureSize = 512;
            platform.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            platform.format = TextureImporterFormat.Automatic;
            platform.textureCompression = TextureImporterCompression.Compressed;
            platform.crunchedCompression = false;
            importer.SetPlatformTextureSettings(platform);
            return true;
        }

        static void OnSceneOpened(Scene scene, OpenSceneMode mode) => QueueSceneIcons();

        static void QueueSceneIcons()
        {
            if (queued || EditorApplication.isPlayingOrWillChangePlaymode) return;
            queued = true;
            EditorApplication.delayCall += ApplySceneIcons;
        }

        static void ApplySceneIcons()
        {
            queued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode || Icon == null) return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var controller in go.GetComponentsInChildren<NetShield>(true)) ApplyControllerIcon(controller);
            }
        }

        internal static void ApplyControllerIcon(NetShield controller)
        {
            if (controller == null || EditorApplication.isPlayingOrWillChangePlaymode || Icon == null) return;
            var go = controller.gameObject;
            if (EditorUtility.IsPersistent(go) || !go.scene.IsValid() || EditorSceneManager.IsPreviewScene(go.scene)) return;
            if (EditorGUIUtility.GetIconForObject(go) == icon) return;
            EditorGUIUtility.SetIconForObject(go, icon);
            EditorUtility.SetDirty(go);
            EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }

    internal sealed class NetShieldArtPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Editor/Art/")) return;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            if (name != "NetShieldIcon" && name != "NetShieldLogo" && name != "NetShieldLogoDark") return;
            string packageRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(assetPath))).Replace('\\', '/');
            if (!File.Exists(packageRoot + "/Editor/NetShieldEditorAssets.cs")) return;
            if (assetImporter is TextureImporter importer) NetShieldEditorAssets.ConfigureTexture(importer);
        }
    }
}
