using TUPshaders.Core;
using UnityEngine;

namespace TUPshaders.Effects.Lens
{
    /// <summary>
    /// Combined lens dirt, flare, film grain, vignette, optional chromatic aberration.
    /// Single uber-pass to minimize fullscreen overdraw in VR.
    /// </summary>
    public sealed class LensEffectsBundle : EffectBase
    {
        public override string Id => "lens";
        public override string DisplayName => "Lens Effects";
        public override string Category => "Lens Effects";
        public override int Priority => 80;
        public override float EstimatedCostMs => 0.2f;

        private Material? _mat;
        private bool _warned;

        public float Vignette = 0.25f;
        public float Grain = 0.08f;
        public float Dirt = 0.15f;
        public float Flare = 0.1f;
        public float ChromaticAberration;

        public override void ResetToDefaults()
        {
            base.ResetToDefaults();
            Enabled = true;
            Intensity = 0.6f;
            Vignette = 0.25f;
            Grain = 0.08f;
            Dirt = 0.15f;
            Flare = 0.1f;
            ChromaticAberration = 0f;
        }

        public override void Apply(CommandBufferContext ctx)
        {
            var s = Plugin.Instance?.Settings;
            if (s != null)
            {
                Intensity = s.Get("fx.lens.intensity", Intensity);
                Vignette = s.Get("fx.lens.vignette", Vignette);
                Grain = s.Get("fx.lens.grain", Grain);
                Dirt = s.Get("fx.lens.dirt", Dirt);
                Flare = s.Get("fx.lens.flare", Flare);
                ChromaticAberration = s.Get("fx.lens.ca", ChromaticAberration);
            }

            _mat ??= EffectMaterials.Get("Hidden/TUPshaders/Lens");
            if (_mat == null || _mat.shader == null || _mat.shader.name.Contains("InternalError"))
            {
                if (!_warned) { /* code-only: volumes drive look */ _warned = true; }
                Graphics.Blit(ctx.Source, ctx.Destination);
                return;
            }

            _mat.SetFloat("_Intensity", Intensity);
            _mat.SetFloat("_Vignette", Vignette);
            _mat.SetFloat("_Grain", Grain);
            _mat.SetFloat("_Dirt", Dirt);
            _mat.SetFloat("_Flare", Flare);
            _mat.SetFloat("_CA", ChromaticAberration);
            _mat.SetFloat("_TimeSeed", Time.unscaledTime);
            EffectMaterials.Blit(ctx.Source, ctx.Destination, _mat);
        }

        public override void Dispose() => _mat = null;
    }
}
