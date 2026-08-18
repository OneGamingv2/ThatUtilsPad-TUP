using System;
using TUPshaders.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TUPshaders.Rendering
{
    /// <summary>
    /// All-code graphics look composer. Maps every fx.* setting into stock URP Volumes
    /// and RenderSettings — no AssetBundles, no custom .shader files at runtime.
    /// </summary>
    public sealed class CodeLookComposer
    {
        private readonly SettingsManager _settings;
        private readonly UrpVolumeDriver _volume;

        private float _nextCamPass;
        private float _nextFoliagePass;
        private bool _loggedMode;

        public CodeLookComposer(SettingsManager settings, UrpVolumeDriver volume)
        {
            _settings = settings;
            _volume = volume;
        }

        public void Tick()
        {
            if (!_settings.Get("graphics.master", true))
            {
                RestoreSafeDefaults();
                return;
            }

            if (!_loggedMode)
            {
                _loggedMode = true;
                Plugin.Log.LogInfo("Code-only graphics mode (URP Volumes + RenderSettings + camera posts). No AssetBundle.");
            }

            ApplyRenderSettingsLook();
            ApplyQualityLook();
            ApplyFoliageMaterialPolish();

            // Volumes re-synced by UrpVolumeDriver with forceSync; also push advanced stand-ins.
            _volume.ApplyCodeStandIns(_settings);

            if (Time.unscaledTime >= _nextCamPass)
            {
                _nextCamPass = Time.unscaledTime + 0.35f;
                ForceAllCameras();
            }
        }

        private void ApplyRenderSettingsLook()
        {
            float ambientOn = _settings.Get("fx.ambient.enabled", true) ? 1f : 0f;
            float ambientI = _settings.Get("fx.ambient.intensity", 0.55f) * ambientOn;
            float skyI = (_settings.Get("fx.sky.enabled", true) ? 1f : 0f) *
                         _settings.Get("fx.sky.intensity", 0.5f);
            float fogOn = _settings.Get("fx.fog.enabled", false) ? 1f : 0f;
            float fogI = _settings.Get("fx.fog.intensity", 0.5f) * fogOn;
            float gradeI = (_settings.Get("fx.colorgrading.enabled", true) ? 1f : 0f) *
                           Mathf.Clamp01(_settings.Get("fx.colorgrading.intensity", 1f));
            float gain = _settings.Get("fx.colorgrading.gain", 1.12f);
            float ssao = (_settings.Get("fx.ssao.enabled", false) ? 1f : 0f) *
                         _settings.Get("fx.ssao.intensity", 0.45f);
            float ssr = (_settings.Get("fx.ssr.enabled", false) ? 1f : 0f) *
                        _settings.Get("fx.ssr.intensity", 0.35f);
            float godrays = (_settings.Get("fx.godrays.enabled", false) ? 1f : 0f) *
                            _settings.Get("fx.godrays.intensity", 0.4f);
            bool irl = _settings.Get("fx.look.irl", false);

            // Ambient / sky lift
            RenderSettings.ambientMode = AmbientMode.Flat;
            Color baseAmb = irl
                ? new Color(0.22f, 0.24f, 0.28f, 1f)
                : new Color(0.32f, 0.33f, 0.36f, 1f);
            Color lift = irl
                ? new Color(0.38f + skyI * 0.08f, 0.42f + skyI * 0.06f, 0.48f, 1f)
                : new Color(0.48f + skyI * 0.12f, 0.52f + skyI * 0.08f, 0.78f, 1f);
            // SSAO stand-in: darken ambient corners feel
            Color dark = Color.Lerp(lift, new Color(0.08f, 0.09f, 0.11f), Mathf.Clamp01(ssao * (irl ? 1.1f : 0.85f)));
            RenderSettings.ambientLight = Color.Lerp(baseAmb, dark, Mathf.Clamp01(0.45f + ambientI * 0.75f));
            float ambMul = irl ? 0.82f : 1f;
            RenderSettings.ambientIntensity = Mathf.Lerp(0.85f, 1.9f, ambientI) *
                                              Mathf.Lerp(1f, gain, gradeI * 0.55f) *
                                              Mathf.Lerp(1f, 1.35f, godrays * 0.45f) * ambMul;

            // Reflections stand-in for SSR — punchy so monitor / VR both show a glossier map.
            try
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                RenderSettings.reflectionIntensity = Mathf.Clamp01(0.45f + ssr * 1.1f + skyI * 0.15f);
            }
            catch { /* ignore */ }

            // Fog
            if (fogOn > 0.01f)
            {
                float density = _settings.Get("fx.fog.distanceDensity", 0.008f);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                float densMul = irl ? 0.85f : 1f;
                RenderSettings.fogDensity = Mathf.Max(0.0004f, density * Mathf.Max(0.15f, fogI) * densMul);
                RenderSettings.fogColor = irl
                    ? new Color(0.42f, 0.48f, 0.55f, 1f)
                    : new Color(
                        _settings.Get("fx.fog.color.r", 0.55f),
                        _settings.Get("fx.fog.color.g", 0.65f),
                        _settings.Get("fx.fog.color.b", 0.75f),
                        1f);
                try
                {
                    RenderSettings.fogStartDistance = _settings.Get("fx.fog.start", 8f);
                    RenderSettings.fogEndDistance = _settings.Get("fx.fog.end", 120f);
                }
                catch { /* linear fog fields may differ */ }
            }
            else if (irl)
            {
                // Light air only — heavy fog caused the washed stump look.
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = 0.0022f;
                RenderSettings.fogColor = new Color(0.45f, 0.5f, 0.56f, 1f);
            }
        }

        private void ApplyQualityLook()
        {
            try
            {
                float shadows = _settings.Get("fx.ambient.enabled", true)
                    ? _settings.Get("fx.ambient.intensity", 0.55f)
                    : 0f;
                bool irl = _settings.Get("fx.look.irl", false);
                if (irl || shadows > 0.2f)
                {
                    if (QualitySettings.shadows == UnityEngine.ShadowQuality.Disable)
                        QualitySettings.shadows = UnityEngine.ShadowQuality.All;
                    if (irl)
                    {
                        QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, 75f);
                        QualitySettings.shadowResolution = UnityEngine.ShadowResolution.High;
                        QualitySettings.shadowCascades = 2;
                        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                        QualitySettings.antiAliasing = Mathf.Max(QualitySettings.antiAliasing, 2);
                    }
                }
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Soft color polish on green/grass/leaf materials — not full texture replacement.
        /// </summary>
        private void ApplyFoliageMaterialPolish()
        {
            if (!_settings.Get("fx.grass.enabled", true))
                return;
            if (Time.unscaledTime < _nextFoliagePass)
                return;
            _nextFoliagePass = Time.unscaledTime + 2.5f;

            float grassI = _settings.Get("fx.grass.intensity", 0.5f);
            if (grassI < 0.05f)
                return;
            bool irl = _settings.Get("fx.look.irl", false);
            float satMul = irl ? 1.12f + grassI * 0.2f : 1.05f + grassI * 0.1f;

            try
            {
                var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
                int touched = 0;
                for (int i = 0; i < renderers.Length && touched < 48; i++)
                {
                    var r = renderers[i];
                    if (r == null || !r.enabled || r.sharedMaterial == null)
                        continue;
                    string n = r.sharedMaterial.name;
                    if (string.IsNullOrEmpty(n))
                        continue;
                    string ln = n.ToLowerInvariant();
                    bool greenish = ln.Contains("grass") || ln.Contains("leaf") || ln.Contains("foliage")
                                    || ln.Contains("plant") || ln.Contains("moss") || ln.Contains("bush")
                                    || ln.Contains("tree") || ln.Contains("bark");
                    if (!greenish)
                        continue;

                    // Instance material so we don't mutate shared assets permanently across sessions incorrectly
                    var mat = r.material;
                    if (mat == null) continue;
                    if (mat.HasProperty("_Color"))
                    {
                        Color c = mat.GetColor("_Color");
                        // Push toward healthy outdoor green without magenta
                        c.r = Mathf.Clamp01(c.r * 0.94f);
                        c.g = Mathf.Clamp01(c.g * satMul);
                        c.b = Mathf.Clamp01(c.b * 0.92f);
                        mat.SetColor("_Color", c);
                    }
                    if (mat.HasProperty("_BaseColor"))
                    {
                        Color c = mat.GetColor("_BaseColor");
                        c.r = Mathf.Clamp01(c.r * 0.94f);
                        c.g = Mathf.Clamp01(c.g * satMul);
                        c.b = Mathf.Clamp01(c.b * 0.92f);
                        mat.SetColor("_BaseColor", c);
                    }
                    touched++;
                }
            }
            catch { /* IL2CPP / stripped paths */ }
        }

        private void ForceAllCameras()
        {
            Camera[] cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                Camera cam = cams[i];
                if (cam == null || !cam.enabled)
                    continue;
                if (cam.targetTexture != null)
                    continue;

                try
                {
                    _volume.EnablePostProcessingOnCamera(cam);
                    var data = cam.GetUniversalAdditionalCameraData();
                    if (data == null)
                        continue;
                    data.renderPostProcessing = true;
                    data.volumeLayerMask = ~0;
                    data.volumeTrigger = cam.transform;
                }
                catch
                {
                }
            }
        }

        private void RestoreSafeDefaults()
        {
            // UrpVolumeDriver restores when master off; nothing else required.
        }
    }
}
