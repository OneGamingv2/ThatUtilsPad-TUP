using TUPshaders.Core;
using UnityEngine;

namespace TUPshaders.Effects.Ambient
{
    /// <summary>
    /// Cheap ambient / indirect lighting lift and shadow enhancement via luminance remapping.
    /// Avoids full GI — VR-safe single pass.
    /// </summary>
    public sealed class AmbientLightingEffect : EffectBase
    {
        public override string Id => "ambient";
        public override string DisplayName => "Ambient Lighting";
        public override string Category => "Lighting";
        public override int Priority => 30;
        public override float EstimatedCostMs => 0.1f;

        private Material? _mat;
        private bool _warned;
        public float ShadowBoost = 0.15f;
        public Color AmbientTint = new(0.75f, 0.82f, 1f, 1f);

        public override void ResetToDefaults()
        {
            base.ResetToDefaults();
            Enabled = true;
            Intensity = 0.4f;
            ShadowBoost = 0.15f;
        }

        public override void Apply(CommandBufferContext ctx)
        {
            var s = Plugin.Instance?.Settings;
            if (s != null)
            {
                Intensity = s.Get("fx.ambient.intensity", Intensity);
                ShadowBoost = s.Get("fx.ambient.shadowBoost", ShadowBoost);
            }

            _mat ??= EffectMaterials.Get("Hidden/TUPshaders/Ambient");
            if (_mat == null || _mat.shader == null || _mat.shader.name.Contains("InternalError"))
            {
                if (!_warned) { /* code-only: volumes drive look */ _warned = true; }
                Graphics.Blit(ctx.Source, ctx.Destination);
                return;
            }

            _mat.SetFloat("_Intensity", Intensity);
            _mat.SetFloat("_ShadowBoost", ShadowBoost);
            _mat.SetColor("_AmbientTint", AmbientTint);
            EffectMaterials.Blit(ctx.Source, ctx.Destination, _mat);
        }

        public override void Dispose() => _mat = null;
    }
}
