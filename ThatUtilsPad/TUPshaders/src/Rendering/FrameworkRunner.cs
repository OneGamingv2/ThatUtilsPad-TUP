using System;
using TUPshaders.Core;
using TUPshaders.Performance;
using TUPshaders.Settings;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TUPshaders.Rendering
{
    /// <summary>
    /// Drives Volumes + code look + RenderAgent + ScreenGradeOverlay (PC-visible).
    /// </summary>
    public sealed class FrameworkRunner : MonoBehaviour
    {
        private PerformanceManager _performance = null!;
        private SettingsManager _settings = null!;
        private UrpVolumeDriver _volume = null!;
        private RenderAgent _agent = null!;
        private CodeLookComposer _look = null!;
        private ScreenGradeOverlay _overlay;
        private float _discoverTimer;
        private bool _hookStarted;
        private bool _nativeTried;
        private Camera _mainLogged;

        public void Initialize(
            GraphicsFramework framework,
            PostProcessPipeline pipeline,
            PerformanceManager performance,
            SettingsManager settings,
            UrpVolumeDriver volume,
            RenderAgent agent)
        {
            _performance = performance;
            _settings = settings;
            _volume = volume;
            _agent = agent;
            _look = new CodeLookComposer(settings, volume);

            try
            {
                _overlay = gameObject.GetComponent<ScreenGradeOverlay>()
                           ?? gameObject.AddComponent<ScreenGradeOverlay>();
                _overlay.Initialize(settings);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ScreenGradeOverlay init: " + ex.Message);
            }

            TryDisableRenderGraph();
        }

        private static void TryDisableRenderGraph()
        {
            try
            {
                var gsType = Type.GetType(
                    "UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings, Unity.RenderPipelines.Universal.Runtime",
                    throwOnError: false);
                if (gsType == null)
                    return;

                object settings = null;
                var instProp = gsType.GetProperty("instance",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (instProp != null)
                    settings = instProp.GetValue(null, null);

                if (settings == null)
                    return;

                foreach (var f in settings.GetType().GetFields(
                             System.Reflection.BindingFlags.Instance |
                             System.Reflection.BindingFlags.Public |
                             System.Reflection.BindingFlags.NonPublic))
                {
                    if (f.FieldType == typeof(bool) &&
                        f.Name.IndexOf("RenderGraph", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        f.Name.IndexOf("Enable", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        f.SetValue(settings, false);
                        Plugin.Log.LogInfo("Disabled URP Render Graph via " + f.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogInfo("Render Graph disable skipped: " + ex.Message);
            }
        }

        private void LateUpdate()
        {
            if (!_hookStarted && Time.frameCount >= 5)
            {
                _hookStarted = true;
                try { _agent.EnsureSubscribed(); }
                catch (Exception ex) { Plugin.Log.LogError("Failed to start RenderAgent: " + ex); }
            }

            try { _volume.Tick(forceSync: true); }
            catch (Exception ex) { Plugin.Log.LogWarning("Volume tick: " + ex.Message); }

            try { _look.Tick(); }
            catch (Exception ex) { Plugin.Log.LogWarning("Code look: " + ex.Message); }

            try { _agent.TickScene(); }
            catch (Exception ex) { Plugin.Log.LogWarning("RenderAgent scene: " + ex.Message); }

            if (!_nativeTried)
            {
                _nativeTried = true;
                NativeRenderBridge.TryInit();
            }
            try { NativeRenderBridge.Tick(_settings); }
            catch (Exception ex) { Plugin.Log.LogWarning("NativeRenderBridge: " + ex.Message); }

            // Auto-reset PC monitor overview (camera tablet leftover / FOV) — shaders own this.
            try { PcMonitorAutoFix.TickPeriodic(); }
            catch { /* ignore */ }

            _discoverTimer -= Time.unscaledDeltaTime;
            if (_discoverTimer <= 0f)
            {
                _discoverTimer = 1.25f;
                ForcePostOnAllGameCameras();

                var main = Camera.main;
                if (main != null && _mainLogged != main)
                {
                    _mainLogged = main;
                    try { _volume.EnablePostProcessingOnCamera(main); }
                    catch { /* ignore */ }
                    Plugin.Log.LogInfo("Tracking camera: " + main.name + " (" + main.pixelWidth + "x" + main.pixelHeight + ")");
                }
            }

            float cost = _settings.Get("graphics.master", true) ? 1.1f : 0f;
            _performance.Tick(Time.unscaledDeltaTime, cost > 0 ? 4 : 0, cost);
        }

        private void ForcePostOnAllGameCameras()
        {
            if (!_settings.Get("graphics.master", true))
                return;

            Camera[] cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                Camera cam = cams[i];
                if (cam == null || !cam.isActiveAndEnabled)
                    continue;
                if (cam.targetTexture != null)
                    continue;
                string n = cam.name ?? "";
                if (n.IndexOf("prewarm", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (n.IndexOf("Tablet", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (n.IndexOf("UICamera", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                try
                {
                    _volume.EnablePostProcessingOnCamera(cam);
                    var data = cam.GetUniversalAdditionalCameraData();
                    if (data != null)
                    {
                        data.renderPostProcessing = true;
                        data.volumeLayerMask = ~0;
                        data.volumeTrigger = cam.transform;
                    }

                    cam.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.DepthNormals;
                }
                catch
                {
                }
            }
        }
    }
}
