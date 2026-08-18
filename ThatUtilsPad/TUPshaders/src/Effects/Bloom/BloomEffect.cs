using TUPshaders.Core;
using TUPshaders.Rendering;
using TUPshaders.Utilities;
using UnityEngine;

namespace TUPshaders.Effects.Bloom
{
    /// <summary>
    /// Dual-filter / Kawase bloom with soft-knee threshold, dirt mask, and adaptive downsample.
    /// Resolution scales with PerformanceManager tier — primary GPU cost lever.
    /// </summary>
    public sealed class BloomEffect : EffectBase
    {
        public override string Id => "bloom";
        public override string DisplayName => "Bloom";
        public override string Category => "Bloom";
        public override int Priority => 40;
        public override float EstimatedCostMs => 0.6f;

        private Material? _mat;
        private readonly RenderTexturePool _pool = new();
        private Texture2D? _dirt;
        private bool _warned;

        public float Threshold = 0.9f;
        public float SoftKneeValue = 0.5f;
        public float Radius = 1f;
        public float DirtIntensity = 0.2f;
        public int Downsample = 4;

        public override void ResetToDefaults()
        {
            base.ResetToDefaults();
            Enabled = true;
            Intensity = 0.35f;
            Threshold = 0.9f;
            SoftKneeValue = 0.5f;
            Radius = 1f;
            DirtIntensity = 0.2f;
            Downsample = 4;
        }

        public override void OnQualityChanged(int quality)
        {
            base.OnQualityChanged(quality);
            // Quality 0..3 → downsample 8,6,4,3
            Downsample = quality switch { 0 => 8, 1 => 6, 2 => 4, _ => 3 };
        }

        public override void Apply(CommandBufferContext ctx)
        {
            PullSettings();

            var perf = Plugin.Instance?.Performance;
            int div = Mathf.Max(Downsample, perf?.BloomResolutionScale ?? Downsample);
            int blurPasses = Mathf.Min(perf?.BlurRadiusCap ?? 3, Mathf.RoundToInt(2 + Radius * 2f));

            StereoHelpers.HalfSize(ctx.Width, ctx.Height, div, out int bw, out int bh);

            _mat ??= EffectMaterials.Get("Hidden/TUPshaders/Bloom");
            if (_mat == null || _mat.shader == null || _mat.shader.name.Contains("InternalError"))
            {
                if (!_warned)
                {
                    /* code-only: volumes drive look */
                    _warned = true;
                }
                Graphics.Blit(ctx.Source, ctx.Destination);
                return;
            }

            var bright = _pool.Get(2, bw, bh);
            var blurA = _pool.Get(3, bw, bh);
            var blurB = _pool.Get(4, bw, bh);

            _mat.SetFloat("_Threshold", Threshold);
            _mat.SetFloat("_SoftKnee", SoftKneeValue);
            _mat.SetFloat("_Intensity", Intensity);
            _mat.SetFloat("_DirtIntensity", DirtIntensity);
            _mat.SetFloat("_Radius", Radius);
            if (_dirt != null) _mat.SetTexture("_DirtTex", _dirt);

            // Pass 0: threshold extract @ half/quarter res
            EffectMaterials.Blit(ctx.Source, bright, _mat, 0);

            // Kawase dual-filter ping-pong
            RenderTexture src = bright;
            RenderTexture dst = blurA;
            for (int i = 0; i < blurPasses; i++)
            {
                float offset = (0.5f + i) * Radius;
                _mat.SetFloat("_Offset", offset);
                EffectMaterials.Blit(src, dst, _mat, 1); // kawase
                src = dst;
                dst = src == blurA ? blurB : blurA;
            }

            // Pass 2: composite bloom over source
            _mat.SetTexture("_BloomTex", src);
            EffectMaterials.Blit(ctx.Source, ctx.Destination, _mat, 2);
        }

        private void PullSettings()
        {
            var s = Plugin.Instance?.Settings;
            if (s == null) return;
            Intensity = s.Get("fx.bloom.intensity", Intensity);
            Threshold = s.Get("fx.bloom.threshold", Threshold);
            SoftKneeValue = s.Get("fx.bloom.softKnee", SoftKneeValue);
            Radius = s.Get("fx.bloom.radius", Radius);
            DirtIntensity = s.Get("fx.bloom.dirtIntensity", DirtIntensity);
            Downsample = s.Get("fx.bloom.downsample", Downsample);
        }

        public void SetDirtMask(Texture2D? dirt) => _dirt = dirt;

        public override void Dispose()
        {
            _pool.ReleaseAll();
            _mat = null;
            _dirt = null;
        }
    }
}
