using TUPshaders.Core;
using UnityEngine;

namespace TUPshaders.Effects.Fog
{
    /// <summary>
    /// Distance + height + atmospheric colored fog in one pass (depth-sampled).
    /// </summary>
    public sealed class FogEffect : EffectBase
    {
        public override string Id => "fog";
        public override string DisplayName => "Atmospheric Fog";
        public override string Category => "Fog";
        public override int Priority => 35;
        public override float EstimatedCostMs => 0.25f;

        private Material? _mat;
        private bool _warned;

        public float DistanceDensity = 0.008f;
        public float Height = 8f;
        public float HeightDensity = 0.02f;
        public Color FogColor = new(0.55f, 0.65f, 0.75f, 1f);

        public override void ResetToDefaults()
        {
            base.ResetToDefaults();
            Enabled = false;
            Intensity = 0.5f;
            DistanceDensity = 0.008f;
            Height = 8f;
            HeightDensity = 0.02f;
        }

        public override void Apply(CommandBufferContext ctx)
        {
            var s = Plugin.Instance?.Settings;
            if (s != null)
            {
                Intensity = s.Get("fx.fog.intensity", Intensity);
                DistanceDensity = s.Get("fx.fog.distanceDensity", DistanceDensity);
                Height = s.Get("fx.fog.height", Height);
                HeightDensity = s.Get("fx.fog.heightDensity", HeightDensity);
            }

            _mat ??= EffectMaterials.Get("Hidden/TUPshaders/Fog");
            if (_mat == null || _mat.shader == null || _mat.shader.name.Contains("InternalError"))
            {
                if (!_warned) { /* code-only: volumes drive look */ _warned = true; }
                Graphics.Blit(ctx.Source, ctx.Destination);
                return;
            }

            // Ensure depth texture for camera
            if (ctx.Camera != null)
                ctx.Camera.depthTextureMode |= DepthTextureMode.Depth;

            _mat.SetFloat("_Intensity", Intensity);
            _mat.SetFloat("_DistanceDensity", DistanceDensity);
            _mat.SetFloat("_Height", Height);
            _mat.SetFloat("_HeightDensity", HeightDensity);
            _mat.SetColor("_FogColor", FogColor);
            // Optional GUI color picker overrides
            var s2 = Plugin.Instance?.Settings;
            if (s2 != null && s2.Has("fx.fog.color.r"))
            {
                FogColor = new Color(
                    s2.Get("fx.fog.color.r", FogColor.r),
                    s2.Get("fx.fog.color.g", FogColor.g),
                    s2.Get("fx.fog.color.b", FogColor.b),
                    1f);
                _mat.SetColor("_FogColor", FogColor);
            }
            EffectMaterials.Blit(ctx.Source, ctx.Destination, _mat);
        }

        public override void Dispose() => _mat = null;
    }
}
