using System;
using System.Collections.Generic;
using TUPshaders.Effects;
using TUPshaders.Effects.Advanced;
using TUPshaders.Effects.Ambient;
using TUPshaders.Effects.Bloom;
using TUPshaders.Effects.ColorGrading;
using TUPshaders.Effects.Fog;
using TUPshaders.Effects.Lens;
using TUPshaders.Effects.Sharpening;
using TUPshaders.Effects.Sky;
using TUPshaders.Performance;
using TUPshaders.Settings;
using UnityEngine;

namespace TUPshaders.Core
{
    /// <summary>
    /// Owns the effect registry, material cache, and sync with settings / performance tiers.
    /// </summary>
    public sealed class GraphicsFramework : IDisposable
    {
        private readonly SettingsManager _settings;
        private readonly PerformanceManager _performance;
        private readonly EffectRegistry _registry = new();
        private readonly MaterialCache _materials = new();
        private readonly MaterialPropertyBlock _propertyBlock = new();
        private bool _disposed;

        public EffectRegistry Registry => _registry;
        public MaterialCache Materials => _materials;
        public MaterialPropertyBlock PropertyBlock => _propertyBlock;

        public GraphicsFramework(SettingsManager settings, PerformanceManager performance)
        {
            _settings = settings;
            _performance = performance;
            _performance.OnTierChanged += OnTierChanged;
        }

        public void RegisterBuiltinEffects()
        {
            EffectMaterials.Bind(_materials);

            Register(new ColorGradingEffect());
            Register(new BloomEffect());
            Register(new CasSharpenEffect());
            Register(new AmbientLightingEffect());
            Register(new FogEffect());
            Register(new SkyImproveEffect());
            Register(new LensEffectsBundle());
            Register(new LightweightSsaoEffect());
            Register(new LightweightSsrEffect());
            Register(new GodRaysEffect());

            SyncFromSettings();
            Plugin.Log.LogInfo($"Registered {_registry.Count} effects.");
        }

        public void Register(IEffect effect)
        {
            effect.Initialize(_propertyBlock);
            _registry.Add(effect);
        }

        public void SyncFromSettings()
        {
            foreach (var effect in _registry.All)
            {
                var prefix = $"fx.{effect.Id}.";
                effect.Enabled = _settings.Get(prefix + "enabled", effect.Enabled);
                effect.Intensity = _settings.Get(prefix + "intensity", effect.Intensity);
                effect.QualityLevel = _settings.Get(prefix + "quality", effect.QualityLevel);
                effect.OnQualityChanged(Mathf.Min(effect.QualityLevel, _performance.CurrentTier.MaxEffectQuality));
            }
        }

        public void WriteEffectToSettings(IEffect effect)
        {
            var prefix = $"fx.{effect.Id}.";
            _settings.Set(prefix + "enabled", effect.Enabled);
            _settings.Set(prefix + "intensity", effect.Intensity);
            _settings.Set(prefix + "quality", effect.QualityLevel);
        }

        public IEnumerable<IEffect> ActiveEffects()
        {
            foreach (var e in _registry.Sorted)
            {
                if (e.Enabled && _performance.IsEffectAllowed(e))
                    yield return e;
            }
        }

        private void OnTierChanged(QualityTier.Tier tier)
        {
            foreach (var effect in _registry.All)
                effect.OnQualityChanged(Mathf.Min(effect.QualityLevel, tier.MaxEffectQuality));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _performance.OnTierChanged -= OnTierChanged;
            foreach (var e in _registry.All)
                e.Dispose();
            _materials.Dispose();
        }
    }
}
