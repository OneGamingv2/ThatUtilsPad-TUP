using System;
using TUPshaders.Core;
using TUPshaders.Performance;
using TUPshaders.Utilities;
using UnityEngine;
using UnityEngine.Rendering;

namespace TUPshaders.Rendering
{
    /// <summary>
    /// Drives the post-process chain via Camera.OnRenderImage / command buffers.
    /// Optimized for Single-Pass Stereo: one blit path, no per-eye allocations.
    /// </summary>
    public sealed class PostProcessPipeline : IDisposable
    {
        private readonly GraphicsFramework _framework;
        private readonly PerformanceManager _performance;
        private readonly RenderTexturePool _pool = new();
        private readonly CommandBufferContext _ctx = new();
        private Material? _blitMaterial;
        private bool _disposed;

        public PostProcessPipeline(GraphicsFramework framework, PerformanceManager performance)
        {
            _framework = framework;
            _performance = performance;
        }

        public void EnsureMaterials()
        {
            _blitMaterial ??= _framework.Materials.GetShaderMaterial(
                "Hidden/TUPshaders/Blit",
                "Hidden/Internal-GUITexture");
        }

        /// <summary>
        /// Called from FrameworkRunner after the game camera finishes rendering.
        /// Uses Graphics.Blit chain — compatible with IL2CPP and Single-Pass Stereo.
        /// </summary>
        public void Execute(Camera camera, RenderTexture source, RenderTexture destination)
        {
            if (_disposed || source == null) return;
            EnsureMaterials();

            int w = source.width;
            int h = source.height;
            bool stereo = camera.stereoEnabled || camera.stereoTargetEye != StereoTargetEyeMask.None;

            _ctx.Camera = camera;
            _ctx.Width = w;
            _ctx.Height = h;
            _ctx.DeltaTime = Time.unscaledDeltaTime;
            _ctx.Stereo = stereo;
            _ctx.EyeIndex = 0;
            _ctx.PropertyBlock = _framework.PropertyBlock;
            _ctx.QualityTier = _performance.CurrentTier.Index;

            float cost = 0f;
            int active = 0;

            // Ping-pong between two RTs when multiple effects are active.
            RenderTexture current = source;
            RenderTexture? tempA = null;
            RenderTexture? tempB = null;
            bool useA = true;
            bool first = true;

            foreach (var effect in _framework.ActiveEffects())
            {
                if (!effect.Enabled) continue;

                RenderTexture destRt;
                if (first)
                {
                    tempA = _pool.Get(0, w, h);
                    destRt = tempA;
                    first = false;
                }
                else
                {
                    if (useA)
                    {
                        tempB ??= _pool.Get(1, w, h);
                        destRt = tempB;
                    }
                    else
                    {
                        tempA ??= _pool.Get(0, w, h);
                        destRt = tempA;
                    }
                }

                _ctx.Source = current;
                _ctx.Destination = destRt;

                try
                {
                    effect.Apply(_ctx);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Effect {effect.Id} failed: {ex.Message}");
                    Graphics.Blit(current, destRt);
                }

                current = destRt;
                useA = !useA;
                cost += effect.EstimatedCostMs * Mathf.Lerp(1f, 0.55f, effect.QualityLevel / 3f);
                active++;
            }

            Graphics.Blit(current, destination);
            _performance.Tick(Time.unscaledDeltaTime, active, cost);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pool.ReleaseAll();
        }
    }
}
