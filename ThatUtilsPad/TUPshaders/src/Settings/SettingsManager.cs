using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TUPshaders.Configuration;
using TUPshaders.Core;
using TUPshaders.Utilities;
using UnityEngine;

namespace TUPshaders.Settings
{
    /// <summary>
    /// In-memory + JSON-backed settings store. Categories map to key prefixes (e.g. fx.bloom.*).
    /// </summary>
    public sealed class SettingsManager : ISettingsProvider
    {
        public event Action? OnChanged;

        private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _path;
        private bool _suppressNotify;

        public SettingsManager()
        {
            _path = Path.Combine(PathUtil.ConfigDir, "settings.json");
            ApplyDefaults();
        }

        public static readonly string[] Categories =
        {
            "Graphics", "ReShade Bridge", "Lighting", "Bloom", "Color Grading", "Sharpening",
            "Fog", "Shadows", "Sky", "Ambient Occlusion", "Reflections",
            "Lens Effects", "Ray Look", "Neon / GEffects", "Performance", "Debug"
        };

        public T Get<T>(string key, T defaultValue = default!)
        {
            if (!_values.TryGetValue(key, out var raw) || raw == null)
                return defaultValue;

            try
            {
                if (raw is T t) return t;
                if (typeof(T) == typeof(float)) return (T)(object)Convert.ToSingle(raw, CultureInfo.InvariantCulture);
                if (typeof(T) == typeof(int)) return (T)(object)Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                if (typeof(T) == typeof(bool)) return (T)(object)Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
                if (typeof(T) == typeof(string)) return (T)(object)Convert.ToString(raw, CultureInfo.InvariantCulture)!;
                return (T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture);
            }
            catch
            {
                return defaultValue;
            }
        }

        public void Set<T>(string key, T value)
        {
            _values[key] = value!;
            if (!_suppressNotify)
                OnChanged?.Invoke();
        }

        public bool Has(string key) => _values.ContainsKey(key);

        public void BeginBatch() => _suppressNotify = true;

        public void EndBatch(bool notify = true)
        {
            _suppressNotify = false;
            if (notify) OnChanged?.Invoke();
        }

        public void Save()
        {
            try
            {
                var cfg = ToConfig("Current");
                File.WriteAllText(_path, SimpleJson.SerializeConfig(cfg), Encoding.UTF8);

                var guiPath = Path.Combine(PathUtil.ConfigDir, "gui.json");
                var gui = new GraphicsConfig { Name = "GUI" };
                gui.SetFloat("gui.x", Get("gui.x", 80f));
                gui.SetFloat("gui.y", Get("gui.y", 80f));
                gui.SetFloat("gui.w", Get("gui.w", 960f));
                gui.SetFloat("gui.h", Get("gui.h", 640f));
                gui.SetBool("gui.remember", Get("gui.remember", true));
                File.WriteAllText(guiPath, SimpleJson.SerializeConfig(gui), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Settings save failed: {ex.Message}");
            }
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    if (SimpleJson.TryDeserializeConfig(json, out var cfg) && cfg != null)
                        ApplyConfig(cfg, notify: false);
                }

                var guiPath = Path.Combine(PathUtil.ConfigDir, "gui.json");
                if (File.Exists(guiPath))
                {
                    var json = File.ReadAllText(guiPath);
                    if (SimpleJson.TryDeserializeConfig(json, out var gui) && gui != null)
                        ApplyConfig(gui, notify: false);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Settings load failed: {ex.Message}");
            }
        }

        public void ResetAll()
        {
            _values.Clear();
            ApplyDefaults();
            OnChanged?.Invoke();
        }

        public void ApplyConfig(GraphicsConfig cfg, bool notify = true)
        {
            bool was = _suppressNotify;
            _suppressNotify = true;
            try
            {
                MergeConfig(cfg);
            }
            finally
            {
                _suppressNotify = was;
                if (notify && !was)
                    OnChanged?.Invoke();
            }
        }

        /// <summary>
        /// Replace graphics/look keys with defaults then merge the preset so leftover Ultra/Cinematic
        /// values cannot leak into Competitive / Low-End / etc.
        /// </summary>
        public void ApplyPresetConfig(GraphicsConfig cfg, bool notify = true)
        {
            bool was = _suppressNotify;
            _suppressNotify = true;
            try
            {
                ClearGraphicsKeys();
                ApplyGraphicsDefaultsSilent();
                MergeConfig(cfg);
                if (!string.IsNullOrWhiteSpace(cfg.Name) &&
                    !string.Equals(cfg.Name, "Captured", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(cfg.Name, "Current", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(cfg.Name, "Imported", StringComparison.OrdinalIgnoreCase))
                {
                    _values["preset.startup"] = cfg.Name;
                }
            }
            finally
            {
                _suppressNotify = was;
                if (notify && !was)
                    OnChanged?.Invoke();
            }
        }

        private void MergeConfig(GraphicsConfig cfg)
        {
            foreach (var kv in cfg.Bools) _values[kv.Key] = kv.Value;
            foreach (var kv in cfg.Floats) _values[kv.Key] = kv.Value;
            foreach (var kv in cfg.Ints) _values[kv.Key] = kv.Value;
            foreach (var kv in cfg.Strings) _values[kv.Key] = kv.Value;
            foreach (var kv in cfg.Colors)
            {
                if (kv.Value == null || kv.Value.Length < 3) continue;
                _values[kv.Key + ".r"] = kv.Value[0];
                _values[kv.Key + ".g"] = kv.Value[1];
                _values[kv.Key + ".b"] = kv.Value[2];
                _values[kv.Key + ".a"] = kv.Value.Length > 3 ? kv.Value[3] : 1f;
            }
        }

        private void ClearGraphicsKeys()
        {
            var remove = new List<string>();
            foreach (var key in _values.Keys)
            {
                if (key.StartsWith("fx.", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("performance.", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("graphics.master", StringComparison.OrdinalIgnoreCase))
                {
                    remove.Add(key);
                }
            }

            for (int i = 0; i < remove.Count; i++)
                _values.Remove(remove[i]);
        }

        private void ApplyGraphicsDefaultsSilent()
        {
            // Same look defaults as ApplyDefaults, without touching GUI / debug / preset keys.
            _values["graphics.master"] = true;
            _values["performance.adaptive"] = false;
            _values["performance.targetFps"] = 90;
            _values["performance.maxGpuMs"] = 2.5f;
            _values["performance.autoDisable"] = true;

            _values["fx.colorgrading.enabled"] = true;
            _values["fx.colorgrading.intensity"] = 1f;
            _values["fx.colorgrading.quality"] = 2;
            _values["fx.colorgrading.vibrance"] = 0.45f;
            _values["fx.colorgrading.saturation"] = 1.28f;
            _values["fx.colorgrading.contrast"] = 1.22f;
            _values["fx.colorgrading.gamma"] = 1.0f;
            _values["fx.colorgrading.temperature"] = 0.08f;
            _values["fx.colorgrading.tint"] = 0f;
            _values["fx.colorgrading.lift"] = 0f;
            _values["fx.colorgrading.gain"] = 1.12f;
            _values["fx.colorgrading.tonemap"] = 1;
            _values["fx.colorgrading.lutStrength"] = 0f;

            _values["fx.bloom.enabled"] = true;
            _values["fx.bloom.intensity"] = 0.75f;
            _values["fx.bloom.quality"] = 2;
            _values["fx.bloom.threshold"] = 0.75f;
            _values["fx.bloom.softKnee"] = 0.5f;
            _values["fx.bloom.radius"] = 1f;
            _values["fx.bloom.dirtIntensity"] = 0.15f;
            _values["fx.bloom.downsample"] = 4;

            _values["fx.cas.enabled"] = true;
            _values["fx.cas.intensity"] = 0.45f;
            _values["fx.cas.quality"] = 2;
            _values["fx.cas.adaptive"] = true;

            _values["fx.ambient.enabled"] = true;
            _values["fx.ambient.intensity"] = 0.55f;
            _values["fx.ambient.quality"] = 2;
            _values["fx.ambient.shadowBoost"] = 0.2f;

            _values["fx.fog.enabled"] = false;
            _values["fx.fog.intensity"] = 0.5f;
            _values["fx.fog.quality"] = 2;
            _values["fx.fog.distanceDensity"] = 0.008f;
            _values["fx.fog.height"] = 8f;
            _values["fx.fog.heightDensity"] = 0.02f;
            _values["fx.fog.color.r"] = 0.62f;
            _values["fx.fog.color.g"] = 0.72f;
            _values["fx.fog.color.b"] = 0.82f;
            _values["fx.fog.style"] = 0; // 0 balanced, 1 misty cool, 2 warm dusty, 3 heavy milk
            _values["fx.bridge.boost"] = 1.05f;
            _values["fx.bridge.quality"] = 1;
            _values["fx.grass.enabled"] = true;
            _values["fx.grass.intensity"] = 0.55f;
            _values["fx.bump.enabled"] = true;
            _values["fx.bump.intensity"] = 0.45f;
            _values["fx.detail.intensity"] = 0.35f;
            _values["fx.denoise.intensity"] = 0.35f;
            _values["graphics.nativeBridge"] = true;

            _values["fx.sky.enabled"] = true;
            _values["fx.sky.intensity"] = 0.5f;
            _values["fx.sky.quality"] = 1;

            _values["fx.lens.enabled"] = true;
            _values["fx.lens.intensity"] = 0.75f;
            _values["fx.lens.quality"] = 2;
            _values["fx.lens.vignette"] = 0.3f;
            _values["fx.lens.grain"] = 0.02f;
            _values["fx.lens.dirt"] = 0.15f;
            _values["fx.lens.flare"] = 0.15f;
            _values["fx.lens.ca"] = 0.02f;

            _values["fx.ssao.enabled"] = true;
            _values["fx.ssao.intensity"] = 0.55f;
            _values["fx.ssao.quality"] = 1;
            _values["fx.ssao.samples"] = 8;
            _values["fx.ssao.radius"] = 0.35f;

            _values["fx.ssr.enabled"] = true;
            _values["fx.ssr.intensity"] = 0.4f;
            _values["fx.ssr.quality"] = 1;

            _values["fx.godrays.enabled"] = true;
            _values["fx.godrays.intensity"] = 0.3f;
            _values["fx.godrays.quality"] = 1;

            _values["fx.neon.enabled"] = false;
            _values["fx.neon.intensity"] = 0.85f;
            _values["fx.neon.pixelate"] = 0f;
            _values["fx.look.irl"] = false;
            _values["graphics.nativeBridge"] = true;
        }

        public GraphicsConfig ToConfig(string name)
        {
            var cfg = new GraphicsConfig { Name = name };
            foreach (var kv in _values)
            {
                switch (kv.Value)
                {
                    case bool b: cfg.Bools[kv.Key] = b; break;
                    case float f: cfg.Floats[kv.Key] = f; break;
                    case double d: cfg.Floats[kv.Key] = (float)d; break;
                    case int i: cfg.Ints[kv.Key] = i; break;
                    case string s: cfg.Strings[kv.Key] = s; break;
                }
            }
            return cfg;
        }

        private void ApplyDefaults()
        {
            Set("graphics.master", true);
            Set("performance.adaptive", false);
            Set("performance.targetFps", 90);
            Set("performance.maxGpuMs", 2.5f);
            Set("performance.autoDisable", true);
            Set("gui.toggleKey", (int)KeyCode.F);
            Set("gui.remember", true);
            Set("gui.x", 80f);
            Set("gui.y", 80f);
            Set("gui.w", 960f);
            Set("gui.h", 640f);
            Set("debug.showFps", true);
            Set("debug.showActiveEffects", true);
            Set("preset.startup", "Realistic");

            // Effect defaults (enabled state applied by effect ResetToDefaults + presets)
            Set("fx.colorgrading.enabled", true);
            Set("fx.colorgrading.intensity", 1f);
            Set("fx.colorgrading.quality", 2);
            Set("fx.colorgrading.vibrance", 0.45f);
            Set("fx.colorgrading.saturation", 1.28f);
            Set("fx.colorgrading.contrast", 1.22f);
            Set("fx.colorgrading.gamma", 1.0f);
            Set("fx.colorgrading.temperature", 0.08f);
            Set("fx.colorgrading.tint", 0f);
            Set("fx.colorgrading.lift", 0f);
            Set("fx.colorgrading.gain", 1.12f);
            Set("fx.colorgrading.tonemap", 1); // ACES
            Set("fx.colorgrading.lutStrength", 0f);

            Set("fx.bloom.enabled", true);
            Set("fx.bloom.intensity", 0.75f);
            Set("fx.bloom.quality", 2);
            Set("fx.bloom.threshold", 0.75f);
            Set("fx.bloom.softKnee", 0.5f);
            Set("fx.bloom.radius", 1f);
            Set("fx.bloom.dirtIntensity", 0.15f);
            Set("fx.bloom.downsample", 4);

            Set("fx.cas.enabled", true);
            Set("fx.cas.intensity", 0.45f);
            Set("fx.cas.quality", 2);
            Set("fx.cas.adaptive", true);

            Set("fx.ambient.enabled", true);
            Set("fx.ambient.intensity", 0.55f);
            Set("fx.ambient.quality", 2);
            Set("fx.ambient.shadowBoost", 0.2f);

            Set("fx.fog.enabled", false);
            Set("fx.fog.intensity", 0.5f);
            Set("fx.fog.quality", 2);
            Set("fx.fog.distanceDensity", 0.008f);
            Set("fx.fog.height", 8f);
            Set("fx.fog.heightDensity", 0.02f);
            Set("fx.fog.color.r", 0.62f);
            Set("fx.fog.color.g", 0.72f);
            Set("fx.fog.color.b", 0.82f);
            Set("fx.fog.style", 0);
            Set("fx.bridge.boost", 1.05f);
            Set("fx.bridge.quality", 1);
            Set("fx.grass.enabled", true);
            Set("fx.grass.intensity", 0.55f);
            Set("fx.bump.enabled", true);
            Set("fx.bump.intensity", 0.45f);
            Set("fx.detail.intensity", 0.35f);
            Set("fx.denoise.intensity", 0.35f);
            Set("graphics.nativeBridge", true);

            Set("fx.sky.enabled", true);
            Set("fx.sky.intensity", 0.5f);
            Set("fx.sky.quality", 1);

            Set("fx.lens.enabled", true);
            Set("fx.lens.intensity", 0.75f);
            Set("fx.lens.quality", 2);
            Set("fx.lens.vignette", 0.3f);
            Set("fx.lens.grain", 0.02f);
            Set("fx.lens.dirt", 0.15f);
            Set("fx.lens.flare", 0.15f);
            Set("fx.lens.ca", 0.02f);

            Set("fx.ssao.enabled", true);
            Set("fx.ssao.intensity", 0.55f);
            Set("fx.ssao.quality", 1);
            Set("fx.ssao.samples", 8);
            Set("fx.ssao.radius", 0.35f);

            Set("fx.ssr.enabled", true);
            Set("fx.ssr.intensity", 0.4f);
            Set("fx.ssr.quality", 1);

            Set("fx.godrays.enabled", true);
            Set("fx.godrays.intensity", 0.3f);
            Set("fx.godrays.quality", 1);

            Set("fx.neon.enabled", false);
            Set("fx.neon.intensity", 0.85f);
            Set("fx.neon.pixelate", 0f);
            Set("fx.look.irl", false);
            Set("graphics.nativeBridge", true);
        }
    }
}
