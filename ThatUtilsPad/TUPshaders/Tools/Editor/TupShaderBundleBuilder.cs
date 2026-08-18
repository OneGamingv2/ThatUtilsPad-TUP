#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TUPshaders.Editor
{
    /// <summary>
    /// Builds an AssetBundle containing all Hidden/TUPshaders/* shaders for the IL2CPP player.
    /// Place this script under an Editor/ folder in a Unity 2022.3 project that contains the .shader files.
    /// </summary>
    public static class TupShaderBundleBuilder
    {
        private const string MenuPath = "TUPshaders/Build Shader Bundle";

        [MenuItem(MenuPath)]
        public static void Build()
        {
            string[] guids = AssetDatabase.FindAssets("t:Shader");
            var paths = new System.Collections.Generic.List<string>();
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader != null && shader.name.StartsWith("Hidden/TUPshaders/"))
                    paths.Add(path);
            }

            if (paths.Count == 0)
            {
                EditorUtility.DisplayDialog("TUPshaders", "No Hidden/TUPshaders shaders found in the project.", "OK");
                return;
            }

            string outDir = EditorUtility.SaveFolderPanel("Output folder for tupshaders bundle", "", "");
            if (string.IsNullOrEmpty(outDir)) return;

            var builds = new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = "tupshaders",
                    assetNames = paths.ToArray()
                }
            };

            Directory.CreateDirectory(outDir);
            BuildPipeline.BuildAssetBundles(outDir, builds, BuildAssetBundleOptions.None, EditorUserBuildSettings.activeBuildTarget);
            EditorUtility.DisplayDialog("TUPshaders",
                $"Built bundle with {paths.Count} shaders.\nCopy '{Path.Combine(outDir, "tupshaders")}' to BepInEx/plugins/TUPshaders/Shaders/",
                "OK");
        }
    }
}
#endif
