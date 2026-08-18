using System;
using TUPshaders.Core;
using TUPshaders.Settings;
using UnityEngine;

namespace TUPshaders.Performance
{
    /// <summary>
    /// Adaptive quality scaling. Monitors frame timing and steps quality tiers without stuttering.
    /// Targets: RTX 2060+ &lt; 2ms, GTX 1060 &lt; 3ms GPU overhead.
    /// </summary>
    public sealed class PerformanceManager
    {
        public event Action<QualityTier.Tier>? OnTierChanged;

        private readonly SettingsManager _settings;
        private readonly float[] _frameTimes = new float[64];
        private int _frameIndex;
        private float _gpuBudgetMs = 2.5f;
        private float _emaFps = 90f;
        private float _cooldown;
        private int _tierIndex = 2;
        private bool _initialized;

        public QualityTier.Tier CurrentTier => QualityTier.All[_tierIndex];
        public float CurrentFps => _emaFps;
        public float EstimatedGpuMs { get; private set; }
        public float CpuUsageProxy { get; private set; }
        public float VramUsageMb { get; private set; }
        public int ActiveEffectCount { get; private set; }

        public PerformanceManager(SettingsManager settings)
        {
            _settings = settings;
            _gpuBudgetMs = settings.Get("performance.maxGpuMs", 2.5f);
        }

        public void Tick(float deltaTime, int activeEffects, float estimatedCostMs)
        {
            ActiveEffectCount = activeEffects;
            EstimatedGpuMs = estimatedCostMs;

            if (deltaTime <= 0f) return;

            _frameTimes[_frameIndex++ & 63] = deltaTime;
            float fps = 1f / Mathf.Max(deltaTime, 1e-4f);
            _emaFps = Mathf.Lerp(_emaFps, fps, 0.08f);

            // Cheap proxies — real GPU counters aren't available via IL2CPP mods reliably.
            CpuUsageProxy = Mathf.Clamp01(deltaTime / (1f / 72f)) * 100f;
            VramUsageMb = 256f + activeEffects * 24f + CurrentTier.BloomDownsample * 8f;

            if (!_settings.Get("performance.adaptive", false))
                return;

            // Ignore first ~20s while FPS EMA stabilizes (was falsely dropping to Potato)
            if (Time.unscaledTime < 20f) return;

            _cooldown -= deltaTime;
            if (_cooldown > 0f) return;

            float target = _settings.Get("performance.targetFps", 90);
            float headroom = _emaFps - target;

            if (headroom < -8f || EstimatedGpuMs > _gpuBudgetMs * 1.15f)
            {
                StepDown();
                _cooldown = 1.25f; // slow steps — never stutter from thrashing
            }
            else if (headroom > 15f && EstimatedGpuMs < _gpuBudgetMs * 0.65f)
            {
                StepUp();
                _cooldown = 2.5f;
            }

            if (!_initialized)
            {
                _initialized = true;
                OnTierChanged?.Invoke(CurrentTier);
            }
        }

        public bool IsEffectAllowed(IEffect effect)
        {
            if (!_settings.Get("graphics.master", true))
                return false;

            if (effect.EstimatedCostMs <= CurrentTier.MaxSingleEffectMs)
                return true;

            // Expensive optional effects auto-disable on low tiers
            if (_settings.Get("performance.autoDisable", true) && CurrentTier.Index <= 1)
            {
                if (effect.Id is "ssao" or "ssr" or "godrays")
                    return false;
            }

            return CurrentTier.Index >= 1 || effect.EstimatedCostMs < 0.5f;
        }

        public int BloomResolutionScale => CurrentTier.BloomDownsample;
        public int AoSampleCap => CurrentTier.AoSamples;
        public int BlurRadiusCap => CurrentTier.BlurRadius;
        public bool AllowSsr => CurrentTier.AllowSsr;
        public bool AllowTemporal => CurrentTier.AllowTemporal;

        private void StepDown()
        {
            if (_tierIndex <= 0) return;
            _tierIndex--;
            Plugin.Log.LogInfo($"Adaptive quality ↓ {CurrentTier.Name}");
            OnTierChanged?.Invoke(CurrentTier);
        }

        private void StepUp()
        {
            if (_tierIndex >= QualityTier.All.Length - 1) return;
            _tierIndex++;
            Plugin.Log.LogInfo($"Adaptive quality ↑ {CurrentTier.Name}");
            OnTierChanged?.Invoke(CurrentTier);
        }

        public void ForceTier(int index)
        {
            _tierIndex = Mathf.Clamp(index, 0, QualityTier.All.Length - 1);
            OnTierChanged?.Invoke(CurrentTier);
        }
    }

    public static class QualityTier
    {
        public sealed class Tier
        {
            public int Index;
            public string Name = "";
            public int MaxEffectQuality;
            public int BloomDownsample;
            public int AoSamples;
            public int BlurRadius;
            public bool AllowSsr;
            public bool AllowTemporal;
            public float MaxSingleEffectMs;
        }

        public static readonly Tier[] All =
        {
            new() { Index = 0, Name = "Potato", MaxEffectQuality = 0, BloomDownsample = 8, AoSamples = 4, BlurRadius = 1, AllowSsr = false, AllowTemporal = false, MaxSingleEffectMs = 0.4f },
            new() { Index = 1, Name = "Low", MaxEffectQuality = 1, BloomDownsample = 6, AoSamples = 6, BlurRadius = 2, AllowSsr = false, AllowTemporal = false, MaxSingleEffectMs = 0.7f },
            new() { Index = 2, Name = "Medium", MaxEffectQuality = 2, BloomDownsample = 4, AoSamples = 8, BlurRadius = 3, AllowSsr = true, AllowTemporal = false, MaxSingleEffectMs = 1.2f },
            new() { Index = 3, Name = "High", MaxEffectQuality = 3, BloomDownsample = 3, AoSamples = 12, BlurRadius = 4, AllowSsr = true, AllowTemporal = true, MaxSingleEffectMs = 2.0f },
        };
    }
}
