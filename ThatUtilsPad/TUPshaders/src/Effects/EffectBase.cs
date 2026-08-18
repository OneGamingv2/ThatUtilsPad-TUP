using TUPshaders.Core;
using UnityEngine;

namespace TUPshaders.Effects
{
    /// <summary>
    /// Shared base for all TUPshaders effects — defaults, quality hooks, cost estimate.
    /// </summary>
    public abstract class EffectBase : IEffect
    {
        protected MaterialPropertyBlock PropertyBlock = null!;
        protected bool Initialized;

        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract string Category { get; }
        public virtual int Priority => 100;
        public virtual float EstimatedCostMs => 0.2f;

        public bool Enabled { get; set; }
        public int QualityLevel { get; set; } = 2;
        public float Intensity { get; set; } = 1f;

        public virtual void Initialize(MaterialPropertyBlock propertyBlock)
        {
            PropertyBlock = propertyBlock;
            Initialized = true;
            ResetToDefaults();
        }

        public abstract void Apply(CommandBufferContext ctx);

        public virtual void OnQualityChanged(int quality)
        {
            QualityLevel = Mathf.Clamp(quality, 0, 3);
        }

        public virtual void OnDisable() { }

        public virtual void Dispose() { }

        public virtual void ResetToDefaults()
        {
            Enabled = false;
            Intensity = 1f;
            QualityLevel = 2;
        }

        protected static float SoftKnee(float x, float threshold, float knee)
        {
            // Branchless soft-knee curve used by bloom threshold.
            float kneeHalf = knee * 0.5f;
            float rq = Mathf.Clamp01((x - threshold + kneeHalf) / Mathf.Max(knee, 1e-4f));
            return Mathf.Max(rq * rq * kneeHalf, x - threshold);
        }
    }
}
