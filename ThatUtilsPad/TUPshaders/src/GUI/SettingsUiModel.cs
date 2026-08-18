using System;
using System.Collections.Generic;
using TUPshaders.Core;
using TUPshaders.Performance;
using TUPshaders.Presets;
using TUPshaders.Rendering;
using TUPshaders.Settings;

namespace TUPshaders.GUI
{
    /// <summary>
    /// View-model for the PC desktop GUI. Backed by SettingsManager for live preview.
    /// </summary>
    public sealed class SettingsUiModel
    {
        public readonly SettingsManager Settings;
        public readonly PresetManager Presets;
        public readonly GraphicsFramework Framework;
        public readonly PerformanceManager Performance;

        public string SearchQuery = "";
        public string SelectedCategory = "Graphics";
        public string SelectedPreset = "Realistic";
        public string StatusMessage = "";
        public float StatusTimer;

        public SettingsUiModel(
            SettingsManager settings,
            PresetManager presets,
            GraphicsFramework framework,
            PerformanceManager performance)
        {
            Settings = settings;
            Presets = presets;
            Framework = framework;
            Performance = performance;
        }

        public IEnumerable<SettingEntry> EntriesForCategory(string category)
        {
            foreach (var e in AllEntries())
            {
                if (!string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(SearchQuery) &&
                    e.Label.IndexOf(SearchQuery, StringComparison.OrdinalIgnoreCase) < 0 &&
                    e.Key.IndexOf(SearchQuery, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                yield return e;
            }
        }

        public IEnumerable<SettingEntry> AllEntries()
        {
            yield return SettingEntry.Toggle("Graphics", "graphics.master", "Master Enable", true);
            yield return SettingEntry.Toggle("Performance", "performance.adaptive", "Adaptive Quality", true);
            yield return SettingEntry.IntSlider("Performance", "performance.targetFps", "Target FPS", 90, 72, 144);
            yield return SettingEntry.FloatSlider("Performance", "performance.maxGpuMs", "Max GPU ms", 2.5f, 1f, 5f);
            yield return SettingEntry.Toggle("Performance", "performance.autoDisable", "Auto-Disable Expensive FX", true);

            yield return SettingEntry.Toggle("Debug", "debug.showFps", "Show FPS", true);
            yield return SettingEntry.Toggle("Debug", "debug.showActiveEffects", "Show Active Effects", true);

            foreach (var fx in Framework.Registry.All)
            {
                string cat = fx.Category;
                string p = $"fx.{fx.Id}.";
                yield return SettingEntry.Toggle(cat, p + "enabled", fx.DisplayName + " Enable", fx.Enabled);
                yield return SettingEntry.FloatSlider(cat, p + "intensity", fx.DisplayName + " Intensity", fx.Intensity, 0f, 2f);
                yield return SettingEntry.IntSlider(cat, p + "quality", fx.DisplayName + " Quality", fx.QualityLevel, 0, 3);
            }

            // Color grading advanced
            yield return SettingEntry.FloatSlider("Color Grading", "fx.colorgrading.vibrance", "Vibrance", 0.15f, -1f, 1f);
            yield return SettingEntry.FloatSlider("Color Grading", "fx.colorgrading.saturation", "Saturation", 1.05f, 0f, 2f);
            yield return SettingEntry.FloatSlider("Color Grading", "fx.colorgrading.contrast", "Contrast", 1.08f, 0.5f, 2f);
            yield return SettingEntry.FloatSlider("Color Grading", "fx.colorgrading.gamma", "Gamma", 1f, 0.5f, 2f);
            yield return SettingEntry.FloatSlider("Color Grading", "fx.colorgrading.temperature", "Temperature", 0f, -1f, 1f);
            yield return SettingEntry.FloatSlider("Color Grading", "fx.colorgrading.tint", "Tint", 0f, -1f, 1f);
            yield return SettingEntry.FloatSlider("Color Grading", "fx.colorgrading.lift", "Lift", 0f, -0.5f, 0.5f);
            yield return SettingEntry.FloatSlider("Color Grading", "fx.colorgrading.gain", "Gain", 1f, 0.5f, 2f);
            yield return SettingEntry.IntSlider("Color Grading", "fx.colorgrading.tonemap", "Tonemap (0/1/2)", 1, 0, 2);
            yield return SettingEntry.FloatSlider("Color Grading", "fx.colorgrading.lutStrength", "LUT Strength", 0f, 0f, 1f);

            // Bloom advanced
            yield return SettingEntry.FloatSlider("Bloom", "fx.bloom.threshold", "Threshold", 0.9f, 0f, 2f);
            yield return SettingEntry.FloatSlider("Bloom", "fx.bloom.softKnee", "Soft Knee", 0.5f, 0f, 1f);
            yield return SettingEntry.FloatSlider("Bloom", "fx.bloom.radius", "Radius", 1f, 0.25f, 3f);
            yield return SettingEntry.FloatSlider("Bloom", "fx.bloom.dirtIntensity", "Dirt Mask", 0.2f, 0f, 1f);
            yield return SettingEntry.IntSlider("Bloom", "fx.bloom.downsample", "Downsample", 4, 2, 8);

            yield return SettingEntry.Toggle("Sharpening", "fx.cas.adaptive", "Adaptive Sharpen", true);

            yield return SettingEntry.Toggle("Ray Look", "fx.ssao.enabled", "Contact Shadows (SSAO)", true);
            yield return SettingEntry.FloatSlider("Ray Look", "fx.ssao.intensity", "AO Strength", 0.55f, 0f, 1.5f);
            yield return SettingEntry.Toggle("Ray Look", "fx.ssr.enabled", "Screen Reflections (SSR)", true);
            yield return SettingEntry.FloatSlider("Ray Look", "fx.ssr.intensity", "Reflection Strength", 0.4f, 0f, 1.5f);
            yield return SettingEntry.Toggle("Ray Look", "fx.godrays.enabled", "God Rays", true);
            yield return SettingEntry.FloatSlider("Ray Look", "fx.godrays.intensity", "God Ray Strength", 0.3f, 0f, 1.5f);
            yield return SettingEntry.FloatSlider("Ray Look", "fx.ambient.shadowBoost", "Shadow Boost", 0.4f, 0f, 1f);

            // ReShade-style pack — drives native Present bridge (live)
            yield return SettingEntry.Toggle("ReShade Bridge", "graphics.nativeBridge", "Native D3D Present Bridge", true);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.bridge.boost", "Overall Punch", 1.05f, 0.6f, 1.35f);
            yield return SettingEntry.IntSlider("ReShade Bridge", "fx.fog.style", "Fog Style (0bal/1mist/2warm/3heavy)", 0, 0, 3);
            yield return SettingEntry.Toggle("ReShade Bridge", "fx.fog.enabled", "Ray Fog / Haze", true);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.fog.intensity", "Fog Strength", 0.55f, 0f, 1.5f);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.fog.distanceDensity", "Aerial Distance", 0.012f, 0f, 0.05f);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.fog.heightDensity", "Ground Mist", 0.03f, 0f, 0.1f);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.fog.color.r", "Fog Color R", 0.62f, 0f, 1f);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.fog.color.g", "Fog Color G", 0.72f, 0f, 1f);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.fog.color.b", "Fog Color B", 0.82f, 0f, 1f);
            yield return SettingEntry.Toggle("ReShade Bridge", "fx.ssao.enabled", "Contact AO", true);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.ssao.intensity", "AO", 0.7f, 0f, 1.5f);
            yield return SettingEntry.Toggle("ReShade Bridge", "fx.ssr.enabled", "Screen Reflections", true);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.ssr.intensity", "SSR", 0.55f, 0f, 1.5f);
            yield return SettingEntry.Toggle("ReShade Bridge", "fx.godrays.enabled", "God / Light Rays", true);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.godrays.intensity", "Rays", 0.45f, 0f, 1.5f);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.bloom.intensity", "Bloom", 0.45f, 0f, 2f);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.colorgrading.contrast", "Contrast", 1.18f, 0.5f, 2f);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.colorgrading.saturation", "Saturation", 1.15f, 0f, 2f);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.cas.intensity", "Sharpen", 0.45f, 0f, 1.5f);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.lens.vignette", "Vignette", 0.28f, 0f, 1f);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.lens.grain", "Film Grain (soft)", 0.02f, 0f, 0.25f);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.lens.ca", "Chromatic Aberration (soft)", 0.02f, 0f, 0.35f);
            yield return SettingEntry.Toggle("ReShade Bridge", "fx.grass.enabled", "Grass / Foliage Revive", true);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.grass.intensity", "Grass Strength", 0.7f, 0f, 1.5f);
            yield return SettingEntry.Toggle("ReShade Bridge", "fx.bump.enabled", "Underside / Contact Bump", true);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.bump.intensity", "Bump Strength", 0.55f, 0f, 1.5f);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.detail.intensity", "Micro Detail", 0.45f, 0f, 1.5f);
            yield return SettingEntry.FloatSlider("ReShade Bridge", "fx.denoise.intensity", "Fix Lost / Blocky Pixels", 0.4f, 0f, 1f);
            yield return SettingEntry.IntSlider("ReShade Bridge", "fx.bridge.quality", "Sample Quality (0-2)", 1, 0, 2);
            yield return SettingEntry.Toggle("ReShade Bridge", "fx.look.irl", "IRL Camera Bias", false);

            yield return SettingEntry.Toggle("Neon / GEffects", "fx.neon.enabled", "Neon Magenta Look", false);
            yield return SettingEntry.FloatSlider("Neon / GEffects", "fx.neon.intensity", "Neon Strength", 0.65f, 0f, 1.2f);

            yield return SettingEntry.FloatSlider("Lighting", "fx.ambient.shadowBoost", "Shadow Boost", 0.15f, 0f, 1f);
            yield return SettingEntry.FloatSlider("Shadows", "fx.ambient.shadowBoost", "Shadow Enhancement", 0.15f, 0f, 1f);
            yield return SettingEntry.Toggle("Shadows", "fx.ssao.enabled", "Use SSAO for Contact Shadows", true);

            yield return SettingEntry.Toggle("Fog", "fx.fog.enabled", "Fog Enable", false);
            yield return SettingEntry.FloatSlider("Fog", "fx.fog.intensity", "Fog Intensity", 0.5f, 0f, 1.5f);
            yield return SettingEntry.FloatSlider("Fog", "fx.fog.distanceDensity", "Distance Density", 0.008f, 0f, 0.05f);
            yield return SettingEntry.FloatSlider("Fog", "fx.fog.height", "Height", 8f, 0f, 40f);
            yield return SettingEntry.FloatSlider("Fog", "fx.fog.heightDensity", "Height Density", 0.02f, 0f, 0.1f);

            yield return SettingEntry.FloatSlider("Lens Effects", "fx.lens.vignette", "Vignette", 0.25f, 0f, 1f);
            yield return SettingEntry.FloatSlider("Lens Effects", "fx.lens.grain", "Film Grain (soft)", 0.02f, 0f, 0.25f);
            yield return SettingEntry.FloatSlider("Lens Effects", "fx.lens.dirt", "Lens Dirt", 0.08f, 0f, 1f);
            yield return SettingEntry.FloatSlider("Lens Effects", "fx.lens.flare", "Lens Flare", 0.06f, 0f, 1f);
            yield return SettingEntry.FloatSlider("Lens Effects", "fx.lens.ca", "Chromatic Aberration (soft)", 0.01f, 0f, 0.35f);

            yield return SettingEntry.IntSlider("Ambient Occlusion", "fx.ssao.samples", "AO Samples", 8, 4, 16);
            yield return SettingEntry.FloatSlider("Ambient Occlusion", "fx.ssao.radius", "AO Radius", 0.35f, 0.05f, 1.5f);
        }

        public void SetStatus(string msg)
        {
            StatusMessage = msg;
            StatusTimer = 3f;
        }

        public void ApplyPreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            SelectedPreset = name;
            try
            {
                var cfg = Presets.GetBuiltIn(name);
                if (cfg == null || string.IsNullOrEmpty(cfg.Name))
                {
                    SetStatus($"Unknown preset: {name}");
                    return;
                }

                Presets.Apply(cfg);
                Settings.Set("preset.startup", name);
                PcMonitorAutoFix.Request("preset " + name);
                SetStatus($"Loaded preset: {name}");
            }
            catch (System.Exception ex)
            {
                SetStatus($"Preset failed: {ex.Message}");
            }
        }

        public void SaveCurrentAs(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                SetStatus("Enter a preset name");
                return;
            }

            try
            {
                var cfg = Presets.CaptureCurrent();
                cfg.Name = name.Trim();
                Presets.SaveUser(cfg.Name, cfg);
                SelectedPreset = cfg.Name;
                Settings.Set("preset.startup", cfg.Name);
                SetStatus($"Saved preset: {cfg.Name}");
            }
            catch (System.Exception ex)
            {
                SetStatus($"Save failed: {ex.Message}");
            }
        }

        public void ResetDefaults()
        {
            Settings.ResetAll();
            SelectedPreset = "Realistic";
            try
            {
                Presets.Apply(Presets.GetBuiltIn("Realistic"));
            }
            catch { /* ignore */ }

            // Also recover PC / monitor camera (zoom, FreeCam stuck in geometry, etc.)
            try
            {
                var cam = CameraMod.Camera.CameraController.Instance;
                if (cam != null)
                    cam.ForceFixZoomedPcView();
                if (CameraMod.Camera.Comps.UI.Instance != null)
                    CameraMod.Camera.Comps.UI.Instance.freecam = false;
            }
            catch (System.Exception ex)
            {
                SetStatus("Graphics reset OK — PC view: " + ex.Message);
                return;
            }

            SetStatus("Reset: Realistic + PC view restored");
        }
    }

    public enum SettingKind { Toggle, FloatSlider, IntSlider, Dropdown, Color }

    public sealed class SettingEntry
    {
        public string Category = "";
        public string Key = "";
        public string Label = "";
        public SettingKind Kind;
        public float FloatDefault;
        public float Min;
        public float Max;
        public int IntDefault;
        public bool BoolDefault;

        public static SettingEntry Toggle(string cat, string key, string label, bool def) => new()
        {
            Category = cat, Key = key, Label = label, Kind = SettingKind.Toggle, BoolDefault = def
        };

        public static SettingEntry FloatSlider(string cat, string key, string label, float def, float min, float max) => new()
        {
            Category = cat, Key = key, Label = label, Kind = SettingKind.FloatSlider,
            FloatDefault = def, Min = min, Max = max
        };

        public static SettingEntry IntSlider(string cat, string key, string label, int def, int min, int max) => new()
        {
            Category = cat, Key = key, Label = label, Kind = SettingKind.IntSlider,
            IntDefault = def, Min = min, Max = max
        };
    }
}
