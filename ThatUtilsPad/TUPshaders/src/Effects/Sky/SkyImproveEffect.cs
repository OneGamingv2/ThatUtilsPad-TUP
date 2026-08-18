using TUPshaders.Core;
using UnityEngine;

namespace TUPshaders.Effects.Sky
{
    /// <summary>
    /// Improves sky/sun/cloud coloration via upper-hemisphere luminance grading.
    /// Does not replace the skybox mesh — post-only, VR-safe.
    /// </summary>
    public sealed class SkyImproveEffect : EffectBase
    {
        public override string Id => "sky";
        public override string DisplayName => "Sky Improvements";
        public override string Category => "Sky";
        public override int Priority => 25;
        public override float EstimatedCostMs => 0.1f;

        private Material? _mat;
        private bool _warned;
        public Color SkyTint = new(0.45f, 0.65f, 1f, 1f);
        public Color SunTint = new(1f, 0.92f, 0.75f, 1f);
        public float SunBoost = 0.25f;

        public override void ResetToDefaults()
        {
            base.ResetToDefaults();
            Enabled = true;
            Intensity = 0.5f;
            SunBoost = 0.25f;
        }

        public override void Apply(CommandBufferContext ctx)
        {
            var s = Plugin.Instance?.Settings;
            if (s != null) Intensity = s.Get("fx.sky.intensity", Intensity);

            _mat ??= EffectMaterials.Get("Hidden/TUPshaders/Sky");
            if (_mat == null || _mat.shader == null || _mat.shader.name.Contains("InternalError"))
            {
                if (!_warned) { /* code-only: volumes drive look */ _warned = true; }
                Graphics.Blit(ctx.Source, ctx.Destination);
                return;
            }

            if (ctx.Camera != null)
                ctx.Camera.depthTextureMode |= DepthTextureMode.Depth;

            _mat.SetFloat("_Intensity", Intensity);
            _mat.SetFloat("_SunBoost", SunBoost);
            _mat.SetColor("_SkyTint", SkyTint);
            _mat.SetColor("_SunTint", SunTint);
            EffectMaterials.Blit(ctx.Source, ctx.Destination, _mat);
        }

        public override void Dispose() => _mat = null;
    }
}
