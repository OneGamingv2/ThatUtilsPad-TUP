using TUPshaders.Core;
using UnityEngine;

namespace TUPshaders.Effects.Sharpening
{
    /// <summary>
    /// AMD FidelityFX CAS-style contrast-adaptive sharpening. Single pass, minimal taps.
    /// </summary>
    public sealed class CasSharpenEffect : EffectBase
    {
        public override string Id => "cas";
        public override string DisplayName => "CAS Sharpen";
        public override string Category => "Sharpening";
        public override int Priority => 90;
        public override float EstimatedCostMs => 0.12f;

        private Material? _mat;
        private bool _warned;
        public bool Adaptive = true;

        public override void ResetToDefaults()
        {
            base.ResetToDefaults();
            Enabled = true;
            Intensity = 0.45f;
            Adaptive = true;
        }

        public override void Apply(CommandBufferContext ctx)
        {
            var s = Plugin.Instance?.Settings;
            if (s != null)
            {
                Intensity = s.Get("fx.cas.intensity", Intensity);
                Adaptive = s.Get("fx.cas.adaptive", Adaptive);
            }

            _mat ??= EffectMaterials.Get("Hidden/TUPshaders/CAS");
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

            _mat.SetFloat("_Sharpness", Intensity);
            _mat.SetFloat("_Adaptive", Adaptive ? 1f : 0f);
            EffectMaterials.Blit(ctx.Source, ctx.Destination, _mat);
        }

        public override void Dispose() => _mat = null;
    }
}
