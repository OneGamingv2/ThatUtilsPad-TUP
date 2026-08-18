using TUPshaders.Core;
using TUPshaders.Rendering;
using TUPshaders.Utilities;
using UnityEngine;

namespace TUPshaders.Effects.Advanced
{
    /// <summary>
    /// Lightweight screen-space ambient occlusion. Sample count capped by adaptive quality.
    /// </summary>
    public sealed class LightweightSsaoEffect : EffectBase
    {
        public override string Id => "ssao";
        public override string DisplayName => "SSAO";
        public override string Category => "Ambient Occlusion";
        public override int Priority => 20;
        public override float EstimatedCostMs => 0.9f;

        private Material? _mat;
        private readonly RenderTexturePool _pool = new();
        private bool _warned;
        public int Samples = 8;
        public float Radius = 0.35f;

        public override void ResetToDefaults()
        {
            base.ResetToDefaults();
            Enabled = false;
            Intensity = 0.5f;
            Samples = 8;
            Radius = 0.35f;
            QualityLevel = 1;
        }

        public override void Apply(CommandBufferContext ctx)
        {
            var s = Plugin.Instance?.Settings;
            var perf = Plugin.Instance?.Performance;
            if (s != null)
            {
                Intensity = s.Get("fx.ssao.intensity", Intensity);
                Samples = s.Get("fx.ssao.samples", Samples);
                Radius = s.Get("fx.ssao.radius", Radius);
            }
            Samples = Mathf.Min(Samples, perf?.AoSampleCap ?? Samples);

            _mat ??= EffectMaterials.Get("Hidden/TUPshaders/SSAO");
            if (_mat == null || _mat.shader == null || _mat.shader.name.Contains("InternalError"))
            {
                if (!_warned) { /* code-only: volumes drive look */ _warned = true; }
                Graphics.Blit(ctx.Source, ctx.Destination);
                return;
            }

            if (ctx.Camera != null)
                ctx.Camera.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.DepthNormals;

            StereoHelpers.HalfSize(ctx.Width, ctx.Height, QualityLevel <= 1 ? 2 : 1, out int aw, out int ah);
            var ao = _pool.Get(5, aw, ah, RenderTextureFormat.R8);

            _mat.SetFloat("_Intensity", Intensity);
            _mat.SetFloat("_Radius", Radius);
            _mat.SetInt("_Samples", Samples);
            EffectMaterials.Blit(ctx.Source, ao, _mat, 0);
            _mat.SetTexture("_AOTex", ao);
            EffectMaterials.Blit(ctx.Source, ctx.Destination, _mat, 1);
        }

        public override void Dispose()
        {
            _pool.ReleaseAll();
            _mat = null;
        }
    }

    /// <summary>
    /// Lightweight SSR — short ray march, heavily quality-gated for VR.
    /// </summary>
    public sealed class LightweightSsrEffect : EffectBase
    {
        public override string Id => "ssr";
        public override string DisplayName => "SSR";
        public override string Category => "Reflections";
        public override int Priority => 22;
        public override float EstimatedCostMs => 1.1f;

        private Material? _mat;
        private bool _warned;

        public override void ResetToDefaults()
        {
            base.ResetToDefaults();
            Enabled = false;
            Intensity = 0.35f;
            QualityLevel = 1;
        }

        public override void Apply(CommandBufferContext ctx)
        {
            var perf = Plugin.Instance?.Performance;
            if (perf != null && !perf.AllowSsr)
            {
                Graphics.Blit(ctx.Source, ctx.Destination);
                return;
            }

            var s = Plugin.Instance?.Settings;
            if (s != null) Intensity = s.Get("fx.ssr.intensity", Intensity);

            _mat ??= EffectMaterials.Get("Hidden/TUPshaders/SSR");
            if (_mat == null || _mat.shader == null || _mat.shader.name.Contains("InternalError"))
            {
                if (!_warned) { /* code-only: volumes drive look */ _warned = true; }
                Graphics.Blit(ctx.Source, ctx.Destination);
                return;
            }

            if (ctx.Camera != null)
                ctx.Camera.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.DepthNormals;

            _mat.SetFloat("_Intensity", Intensity);
            _mat.SetInt("_Steps", QualityLevel <= 1 ? 8 : 16);
            EffectMaterials.Blit(ctx.Source, ctx.Destination, _mat);
        }

        public override void Dispose() => _mat = null;
    }

    /// <summary>
    /// Cheap radial god-ray / light-shaft approximation from bright screen center / sun.
    /// </summary>
    public sealed class GodRaysEffect : EffectBase
    {
        public override string Id => "godrays";
        public override string DisplayName => "God Rays";
        public override string Category => "Lighting";
        public override int Priority => 45;
        public override float EstimatedCostMs => 0.55f;

        private Material? _mat;
        private bool _warned;

        public override void ResetToDefaults()
        {
            base.ResetToDefaults();
            Enabled = false;
            Intensity = 0.25f;
            QualityLevel = 1;
        }

        public override void Apply(CommandBufferContext ctx)
        {
            var s = Plugin.Instance?.Settings;
            if (s != null) Intensity = s.Get("fx.godrays.intensity", Intensity);

            _mat ??= EffectMaterials.Get("Hidden/TUPshaders/GodRays");
            if (_mat == null || _mat.shader == null || _mat.shader.name.Contains("InternalError"))
            {
                if (!_warned) { /* code-only: volumes drive look */ _warned = true; }
                Graphics.Blit(ctx.Source, ctx.Destination);
                return;
            }

            _mat.SetFloat("_Intensity", Intensity);
            _mat.SetInt("_Samples", QualityLevel <= 1 ? 8 : 16);
            _mat.SetVector("_LightPos", new Vector4(0.5f, 0.65f, 0f, 0f));
            EffectMaterials.Blit(ctx.Source, ctx.Destination, _mat);
        }

        public override void Dispose() => _mat = null;
    }
}
