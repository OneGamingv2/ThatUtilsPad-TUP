using System;
using System.Collections.Generic;
using TUPshaders.Utilities;
using UnityEngine;

namespace TUPshaders.Core
{
    /// <summary>
    /// Caches Materials by shader name / asset path. Avoids per-frame Material construction.
    /// </summary>
    public sealed class MaterialCache : IDisposable
    {
        private readonly Dictionary<string, Material> _cache = new(StringComparer.Ordinal);
        private bool _disposed;

        public Material GetOrCreate(string key, Func<Material> factory)
        {
            if (_cache.TryGetValue(key, out var mat) && mat != null)
                return mat;

            mat = factory();
            mat.hideFlags = HideFlags.HideAndDontSave;
            _cache[key] = mat;
            return mat;
        }

        public Material GetShaderMaterial(string shaderName, string fallbackShader = "Hidden/Internal-GUITexture")
        {
            return GetOrCreate(shaderName, () =>
            {
                var shader = ShaderUtil.FindOrLoad(shaderName);
                if (shader == null)
                {
                    shader = ShaderUtil.FindBestBuiltinBlit() ?? Shader.Find(fallbackShader)
                             ?? Shader.Find("Hidden/InternalErrorShader");
                }
                return new Material(shader!);
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var kv in _cache)
            {
                if (kv.Value != null)
                    UnityEngine.Object.Destroy(kv.Value);
            }
            _cache.Clear();
        }
    }
}
