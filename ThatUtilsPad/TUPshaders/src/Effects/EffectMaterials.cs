using TUPshaders.Core;
using UnityEngine;

namespace TUPshaders.Effects
{
    /// <summary>
    /// Shared material access for effects. Prefer cached Materials from <see cref="MaterialCache"/>.
    /// </summary>
    public static class EffectMaterials
    {
        private static MaterialCache? _cache;

        public static void Bind(MaterialCache cache) => _cache = cache;

        public static Material Get(string shaderName) =>
            (_cache ?? Plugin.Instance.Framework.Materials).GetShaderMaterial(shaderName);

        public static void Blit(RenderTexture src, RenderTexture dst, Material mat, int pass = 0)
        {
            if (mat != null)
                Graphics.Blit(src, dst, mat, pass);
            else
                Graphics.Blit(src, dst);
        }
    }
}
