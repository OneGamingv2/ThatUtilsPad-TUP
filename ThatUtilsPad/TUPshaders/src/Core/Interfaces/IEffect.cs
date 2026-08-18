using UnityEngine;

namespace TUPshaders.Core
{
    /// <summary>
    /// Contract for a single toggleable, quality-scalable visual effect.
    /// Effects are registered with <see cref="EffectRegistry"/> and driven by the post-process pipeline.
    /// </summary>
    public interface IEffect
    {
        string Id { get; }
        string DisplayName { get; }
        string Category { get; }
        bool Enabled { get; set; }
        int QualityLevel { get; set; }
        float Intensity { get; set; }
        int Priority { get; }
        float EstimatedCostMs { get; }

        void Initialize(MaterialPropertyBlock propertyBlock);
        void Apply(CommandBufferContext ctx);
        void OnQualityChanged(int quality);
        void OnDisable();
        void Dispose();
        void ResetToDefaults();
    }

    /// <summary>
    /// Lightweight context passed to effects during the command-buffer pass.
    /// Avoids allocations by reusing a single instance per frame.
    /// </summary>
    public sealed class CommandBufferContext
    {
        public Camera Camera;
        public RenderTexture Source;
        public RenderTexture Destination;
        public int Width;
        public int Height;
        public float DeltaTime;
        public bool Stereo;
        public int EyeIndex;
        public MaterialPropertyBlock PropertyBlock;
        public int QualityTier;
    }
}
