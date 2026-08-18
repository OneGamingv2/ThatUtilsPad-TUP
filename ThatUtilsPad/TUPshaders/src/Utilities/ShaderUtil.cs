using System;
using System.IO;
using UnityEngine;

namespace TUPshaders.Utilities
{
    /// <summary>
    /// Built-in Unity shaders only. No AssetBundles, no runtime HLSL compile.
    /// </summary>
    public static class ShaderUtil
    {
        private static readonly string[] BuiltinBlitCandidates =
        {
            "Hidden/Universal/CoreBlit",
            "Hidden/Universal Render Pipeline/Blit",
            "Hidden/Universal Render Pipeline/CoreBlit",
            "Hidden/BlitCopy",
            "Hidden/Internal-BlitCopy",
            "Hidden/Internal-GUITexture",
            "Sprites/Default",
            "Unlit/Texture",
            "UI/Default"
        };

        public static Shader FindOrLoad(string shaderName)
        {
            // Prefer exact built-in find; never load AssetBundles.
            Shader s = Shader.Find(shaderName);
            if (s != null)
                return s;

            // Map our old Hidden/TUPshaders/* names → built-ins.
            if (!string.IsNullOrEmpty(shaderName) &&
                shaderName.StartsWith("Hidden/TUPshaders/", StringComparison.OrdinalIgnoreCase))
                return FindBestBuiltinBlit();

            return FindBestBuiltinBlit();
        }

        public static Shader FindBestBuiltinBlit()
        {
            for (int i = 0; i < BuiltinBlitCandidates.Length; i++)
            {
                Shader s = Shader.Find(BuiltinBlitCandidates[i]);
                if (s != null)
                    return s;
            }

            return Shader.Find("Hidden/InternalErrorShader");
        }

        /// <summary>Legacy stub — AssetBundles intentionally unused.</summary>
        public static void EnsureBundle()
        {
            // no-op
        }

        public static bool HasCustomBundle()
        {
            try
            {
                string path = Path.Combine(PathUtil.ShaderDir, "tupshaders");
                return File.Exists(path) || File.Exists(path + ".bundle");
            }
            catch
            {
                return false;
            }
        }
    }
}
