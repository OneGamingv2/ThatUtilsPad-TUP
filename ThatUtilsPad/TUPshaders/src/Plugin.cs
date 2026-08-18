using System;
using BepInEx.Logging;
using TUPshaders.Core;
using TUPshaders.GUI.Desktop;
using TUPshaders.Performance;
using TUPshaders.Presets;
using TUPshaders.Rendering;
using TUPshaders.Settings;
using TUPshaders.Utilities;
using UnityEngine;

namespace TUPshaders
{
    /// <summary>
    /// Embedded inside ThatUtilsPad (not a separate BepInEx plugin).
    /// </summary>
    public sealed class Plugin : MonoBehaviour
    {
        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        public GraphicsFramework Framework { get; private set; }
        public SettingsManager Settings { get; private set; }
        public PresetManager Presets { get; private set; }
        public PerformanceManager Performance { get; private set; }
        public PostProcessPipeline Pipeline { get; private set; }
        public UrpVolumeDriver VolumeDriver { get; private set; }
        public RenderAgent RenderAgent { get; private set; }
        public ReshadeFrameHook ReshadeHook { get; private set; }
        public DesktopGuiController DesktopGui { get; private set; }

        public static bool IsReady => Instance != null && Instance.Settings != null;

        public static void Bootstrap()
        {
            if (Instance != null)
                return;

            try
            {
                var go = new GameObject("TUPshaders_Host");
                DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<Plugin>();
            }
            catch (Exception ex)
            {
                Debug.LogError("[TUPshaders] Bootstrap failed: " + ex);
            }
        }

        public static void Shutdown()
        {
            if (Instance == null)
                return;
            try
            {
                Destroy(Instance.gameObject);
            }
            catch
            {
            }
        }

        public static void ToggleDesktopGui()
        {
            if (Instance?.DesktopGui == null)
                return;
            Instance.DesktopGui.SetVisible(!Instance.DesktopGui.IsVisible);
        }

        public static void SetEffectsEnabled(bool enabled)
        {
            if (Instance?.Settings == null)
                return;
            Instance.Settings.Set("graphics.master", enabled);
            Instance.Settings.Save();
        }

        public static bool GetEffectsEnabled()
        {
            if (Instance?.Settings == null)
                return false;
            return Instance.Settings.Get("graphics.master", true);
        }

        public static void CyclePreset()
        {
            Instance?.Presets?.CycleNext();
        }

        private void Awake()
        {
            Instance = this;
            Log = BepInEx.Logging.Logger.CreateLogSource(PluginInfo.Name);
            Log.LogInfo($"{PluginInfo.Name} v{PluginInfo.Version} loading (embedded in ThatUtilsPad)…");

            try
            {
                PathUtil.EnsureDirectories();

                Settings = new SettingsManager();
                Settings.Load();
                Settings.Set("performance.adaptive", false);
                // Default PC GUI toggle to F (was 5 as a standalone mod).
                int toggle = Settings.Get("gui.toggleKey", (int)KeyCode.F);
                if (toggle == (int)KeyCode.Alpha5)
                    toggle = (int)KeyCode.F;
                Settings.Set("gui.toggleKey", toggle);

                Presets = new PresetManager(Settings);
                Performance = new PerformanceManager(Settings);
                Framework = new GraphicsFramework(Settings, Performance);
                Pipeline = new PostProcessPipeline(Framework, Performance);
                VolumeDriver = new UrpVolumeDriver(Settings);
                RenderAgent = new RenderAgent(Settings);
                // Compat alias — older code paths may still mention ReshadeHook.
                ReshadeHook = new ReshadeFrameHook(Settings);

                Framework.RegisterBuiltinEffects();
                // Apply the chosen startup preset as a full replace so Competitive / Ultra / etc. actually differ.
                Presets.ApplyStartupPreset();
                if (!Settings.Has("graphics.renderAgent.seeded"))
                    Settings.Set("graphics.renderAgent.seeded", true);

                var runner = gameObject.AddComponent<FrameworkRunner>();
                runner.Initialize(Framework, Pipeline, Performance, Settings, VolumeDriver, RenderAgent);

                DesktopGui = gameObject.AddComponent<DesktopGuiController>();
                DesktopGui.Initialize(Settings, Presets, Framework, Performance);

                Settings.OnChanged += OnSettingsChanged;

                // Auto-fix PC monitor after shaders load (don't wait for user to press Reset).
                PcMonitorAutoFix.Request("TUPshaders ready");

                Log.LogInfo($"{PluginInfo.Name} ready — RenderAgent (ReShade-style) on VR + PC. Press [F] for graphics GUI.");
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to load {PluginInfo.Name}: {ex}");
            }
        }

        private void OnSettingsChanged()
        {
            Framework?.SyncFromSettings();
            VolumeDriver?.MarkDirty();
            DesktopGui?.RefreshFromSettings();
        }

        private void OnDestroy()
        {
            try
            {
                if (Settings != null)
                    Settings.OnChanged -= OnSettingsChanged;
                Settings?.Save();
                ReshadeHook?.Dispose();
                RenderAgent?.Dispose();
                NativeRenderBridge.Shutdown();
                VolumeDriver?.Dispose();
                Framework?.Dispose();
                Pipeline?.Dispose();
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"Unload error: {ex.Message}");
            }

            if (ReferenceEquals(Instance, this))
                Instance = null;
        }
    }
}
