using System;
using TUPshaders.Settings;
using UnityEngine;

namespace TUPshaders.Rendering
{
    /// <summary>
    /// Soft PC grade overlay. NO point-filtered grain/dither (those caused the "little cubes").
    /// IRL = cool filmic wash + vignette. Neon = magenta wash. Ray look = cool specular lift.
    /// </summary>
    public sealed class ScreenGradeOverlay : MonoBehaviour
    {
        private SettingsManager _settings;
        private Texture2D _white;
        private Texture2D _soft;
        private Texture2D _glow;
        private bool _ready;

        public void Initialize(SettingsManager settings)
        {
            _settings = settings;
            _white = Solid(Color.white);
            _soft = MakeVignette(128);
            _glow = MakeGlow(64);
            _ready = true;
            Plugin.Log.LogInfo("ScreenGradeOverlay ready — clean washes (no cube grain).");
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear
            };
            t.SetPixel(0, 0, c);
            t.Apply(false, true);
            return t;
        }

        private static Texture2D MakeVignette(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f;
                float v = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Clamp01((d - 0.4f) / 0.95f);
                a = a * a;
                t.SetPixel(x, y, new Color(0f, 0f, 0f, a));
            }
            t.Apply(false, true);
            return t;
        }

        private static Texture2D MakeGlow(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            float mid = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - mid) / mid;
                float dy = (y - mid) / mid;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * a;
                t.SetPixel(x, y, new Color(1f, 0.97f, 0.94f, a));
            }
            t.Apply(false, true);
            return t;
        }

        private void OnGUI()
        {
            if (!_ready || _settings == null || !_settings.Get("graphics.master", true))
                return;
            if (_white == null)
                return;

            // Prefer native D3D Present post when it's actually drawing.
            if (NativeRenderBridge.IsDrawing)
                return;

            int oldDepth = UnityEngine.GUI.depth;
            UnityEngine.GUI.depth = 1200;

            bool neon = _settings.Get("fx.neon.enabled", false);
            bool irl = _settings.Get("fx.look.irl", false)
                       || string.Equals(_settings.Get("preset.startup", ""), "Realistic", StringComparison.OrdinalIgnoreCase);

            float gradeI = (_settings.Get("fx.colorgrading.enabled", true) ? 1f : 0f)
                           * Mathf.Clamp01(_settings.Get("fx.colorgrading.intensity", 1f));
            float sat = _settings.Get("fx.colorgrading.saturation", 1.1f);
            float contrast = _settings.Get("fx.colorgrading.contrast", 1.1f);
            float vibrance = _settings.Get("fx.colorgrading.vibrance", 0.2f);
            float temp = _settings.Get("fx.colorgrading.temperature", 0f);
            float bloom = (_settings.Get("fx.bloom.enabled", true) ? 1f : 0f)
                          * _settings.Get("fx.bloom.intensity", 0.4f);
            float ssao = (_settings.Get("fx.ssao.enabled", false) ? 1f : 0f)
                         * _settings.Get("fx.ssao.intensity", 0.5f);
            float ssr = (_settings.Get("fx.ssr.enabled", false) ? 1f : 0f)
                        * _settings.Get("fx.ssr.intensity", 0.4f);
            float lensVig = (_settings.Get("fx.lens.enabled", true) ? 1f : 0f)
                            * _settings.Get("fx.lens.vignette", 0.3f)
                            * _settings.Get("fx.lens.intensity", 0.7f);

            if (neon)
                DrawNeon(gradeI, bloom, lensVig);
            else if (irl)
                DrawIrl(gradeI, sat, contrast, temp, bloom, ssao, ssr, lensVig);
            else
                DrawMild(gradeI, sat, contrast, vibrance, temp, bloom, ssao, ssr, lensVig);

            UnityEngine.GUI.depth = oldDepth;
        }

        private void DrawNeon(float gradeI, float bloom, float lensVig)
        {
            float neonI = Mathf.Clamp01(_settings.Get("fx.neon.intensity", 0.7f));
            float s = Mathf.Clamp01(neonI * Mathf.Max(0.35f, gradeI));
            var prev = UnityEngine.GUI.color;
            UnityEngine.GUI.color = new Color(0.75f, 0.1f, 0.7f, Mathf.Lerp(0.06f, 0.16f, s));
            UnityEngine.GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white, ScaleMode.StretchToFill);
            if (bloom > 0.1f && _glow != null)
            {
                UnityEngine.GUI.color = new Color(1f, 0.35f, 0.95f, Mathf.Lerp(0.02f, 0.07f, Mathf.Clamp01(bloom) * s));
                float gw = Screen.width * 0.9f, gh = Screen.height * 0.9f;
                UnityEngine.GUI.DrawTexture(new Rect((Screen.width - gw) * 0.5f, (Screen.height - gh) * 0.5f, gw, gh),
                    _glow, ScaleMode.StretchToFill);
            }
            UnityEngine.GUI.color = prev;
            DrawVignette(Mathf.Clamp01(0.12f + lensVig * 0.3f) * 0.6f);
        }

        /// <summary>
        /// Photoreal-ish camera grade without fog haze or cube grain.
        /// SSR stand-in: cool specular lift (public URP SSR uses screen-space ray march;
        /// we approximate the look only — DXR is not available in Gorilla Tag).
        /// </summary>
        private void DrawIrl(float gradeI, float sat, float contrast, float temp, float bloom,
            float ssao, float ssr, float lensVig)
        {
            float s = Mathf.Clamp01(0.5f + gradeI * 0.5f);
            var prev = UnityEngine.GUI.color;

            // Warm golden-hour mid lift when temperature is positive (Realistic)
            float a = Mathf.Lerp(0.04f, 0.09f, s);
            Color mid = temp < 0f
                ? new Color(0.45f, 0.50f, 0.58f, a)
                : temp > 0.05f
                    ? new Color(0.62f, 0.48f, 0.32f, a * 0.85f)
                    : new Color(0.55f, 0.52f, 0.48f, a);
            // Slightly mute oversaturation
            if (sat < 1f)
            {
                mid.r = Mathf.Lerp(mid.r, 0.5f, (1f - sat) * 0.35f);
                mid.g = Mathf.Lerp(mid.g, 0.5f, (1f - sat) * 0.35f);
            }
            UnityEngine.GUI.color = mid;
            UnityEngine.GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white, ScaleMode.StretchToFill);

            // Contact shadow deepen (SSAO stand-in) — soft dark, no blocks
            float shadowA = Mathf.Clamp01(ssao * 0.1f + Mathf.Max(0f, contrast - 1f) * 0.06f) * s;
            if (shadowA > 0.015f)
            {
                UnityEngine.GUI.color = new Color(0.04f, 0.045f, 0.055f, shadowA);
                UnityEngine.GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white, ScaleMode.StretchToFill);
            }

            // Screen-space "ray" reflection cue: cool brighten (SSR look without ray march)
            if (ssr > 0.05f)
            {
                float ra = Mathf.Clamp01(ssr * 0.07f) * s;
                UnityEngine.GUI.color = new Color(0.55f, 0.72f, 0.95f, ra);
                UnityEngine.GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white, ScaleMode.StretchToFill);
            }

            if (bloom > 0.1f && _glow != null)
            {
                UnityEngine.GUI.color = new Color(1f, 0.97f, 0.92f, Mathf.Clamp01(bloom * 0.08f) * s);
                float gw = Screen.width * 0.95f, gh = Screen.height * 0.95f;
                UnityEngine.GUI.DrawTexture(new Rect((Screen.width - gw) * 0.5f, (Screen.height - gh) * 0.5f, gw, gh),
                    _glow, ScaleMode.StretchToFill);
            }

            UnityEngine.GUI.color = prev;
            DrawVignette(Mathf.Clamp01(0.14f + lensVig * 0.4f + ssao * 0.08f) * s * 0.75f);
        }

        private void DrawMild(float gradeI, float sat, float contrast, float vibrance, float temp,
            float bloom, float ssao, float ssr, float lensVig)
        {
            float punch = gradeI * (0.1f + Mathf.Abs(sat - 1f) * 0.35f + Mathf.Abs(contrast - 1f) * 0.45f
                                   + vibrance * 0.3f);
            punch += bloom * 0.05f + ssao * 0.04f + ssr * 0.04f;
            punch = Mathf.Clamp01(punch);
            if (punch < 0.08f)
            {
                DrawVignette(Mathf.Clamp01(lensVig * 0.4f));
                return;
            }

            var prev = UnityEngine.GUI.color;
            UnityEngine.GUI.color = new Color(
                Mathf.Clamp01(0.5f + temp * 0.12f),
                0.46f,
                Mathf.Clamp01(0.55f - temp * 0.08f + ssr * 0.08f),
                Mathf.Lerp(0.04f, 0.12f, punch));
            UnityEngine.GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white, ScaleMode.StretchToFill);
            UnityEngine.GUI.color = prev;
            DrawVignette(Mathf.Clamp01(lensVig * 0.45f + ssao * 0.08f));
        }

        private void DrawVignette(float alpha)
        {
            if (alpha < 0.03f || _soft == null)
                return;
            var prev = UnityEngine.GUI.color;
            UnityEngine.GUI.color = new Color(0f, 0f, 0f, alpha);
            UnityEngine.GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _soft, ScaleMode.StretchToFill);
            UnityEngine.GUI.color = prev;
        }

        private void OnDestroy()
        {
            if (_white != null) Destroy(_white);
            if (_soft != null) Destroy(_soft);
            if (_glow != null) Destroy(_glow);
        }
    }
}
