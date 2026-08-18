using TUPshaders.Core;
using TUPshaders.Settings;
using UnityEngine;

namespace TUPshaders.Effects.ColorGrading
{
    /// <summary>
    /// Filmic tonemap + vibrance/saturation/contrast/gamma + lift/gamma/gain + white balance + LUT.
    /// Single fullscreen pass — half-precision friendly uniforms, branchless tonemap.
    /// </summary>
    public sealed class ColorGradingEffect : EffectBase
    {
        public override string Id => "colorgrading";
        public override string DisplayName => "Color Grading";
        public override string Category => "Color Grading";
        public override int Priority => 50;
        public override float EstimatedCostMs => 0.15f;

        private Material? _mat;
        private Texture2D? _lut;
        private bool _warned;

        public float Vibrance = 0.15f;
        public float Saturation = 1.05f;
        public float Contrast = 1.08f;
        public float Gamma = 1f;
        public float Temperature;
        public float Tint;
        public float Lift;
        public float Gain = 1f;
        public int TonemapMode = 1;
        public float LutStrength;

        public override void ResetToDefaults()
        {
            base.ResetToDefaults();
            Enabled = true;
            Intensity = 1f;
            Vibrance = 0.15f;
            Saturation = 1.05f;
            Contrast = 1.08f;
            Gamma = 1f;
            Temperature = 0f;
            Tint = 0f;
            Lift = 0f;
            Gain = 1f;
            TonemapMode = 1;
            LutStrength = 0f;
        }

        public override void Apply(CommandBufferContext ctx)
        {
            PullSettings();
            _mat ??= EffectMaterials.Get("Hidden/TUPshaders/ColorGrading");

            if (_mat == null || _mat.shader == null || _mat.shader.name.Contains("InternalError"))
            {
                ApplyFallback(ctx);
                return;
            }

            _mat.SetFloat("_Intensity", Intensity);
            _mat.SetFloat("_Vibrance", Vibrance);
            _mat.SetFloat("_Saturation", Saturation);
            _mat.SetFloat("_Contrast", Contrast);
            _mat.SetFloat("_Gamma", Gamma);
            _mat.SetFloat("_Temperature", Temperature);
            _mat.SetFloat("_Tint", Tint);
            _mat.SetFloat("_Lift", Lift);
            _mat.SetFloat("_Gain", Gain);
            _mat.SetInt("_Tonemap", TonemapMode);
            _mat.SetFloat("_LutStrength", LutStrength);
            if (_lut != null) _mat.SetTexture("_LutTex", _lut);

            EffectMaterials.Blit(ctx.Source, ctx.Destination, _mat);
        }

        private void ApplyFallback(CommandBufferContext ctx)
        {
            if (!_warned)
            {
                Plugin.Log.LogInfo("ColorGrading custom shader skipped — using URP Volumes / code look.");
                _warned = true;
            }
            Graphics.Blit(ctx.Source, ctx.Destination);
        }

        private void PullSettings()
        {
            var s = Plugin.Instance?.Settings;
            if (s == null) return;
            Vibrance = s.Get("fx.colorgrading.vibrance", Vibrance);
            Saturation = s.Get("fx.colorgrading.saturation", Saturation);
            Contrast = s.Get("fx.colorgrading.contrast", Contrast);
            Gamma = s.Get("fx.colorgrading.gamma", Gamma);
            Temperature = s.Get("fx.colorgrading.temperature", Temperature);
            Tint = s.Get("fx.colorgrading.tint", Tint);
            Lift = s.Get("fx.colorgrading.lift", Lift);
            Gain = s.Get("fx.colorgrading.gain", Gain);
            TonemapMode = s.Get("fx.colorgrading.tonemap", TonemapMode);
            LutStrength = s.Get("fx.colorgrading.lutStrength", LutStrength);
            Intensity = s.Get("fx.colorgrading.intensity", Intensity);
        }

        public void SetLut(Texture2D? lut) => _lut = lut;

        public override void Dispose()
        {
            _lut = null;
            _mat = null;
        }
    }
}
